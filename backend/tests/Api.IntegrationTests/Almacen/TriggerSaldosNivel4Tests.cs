using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Tests de integración del trigger <c>tg_movimientos_actualizar_saldo</c>.
/// Verifica ENTRADA (upsert + promedio ponderado) y SALIDA (FOR UPDATE +
/// SALDO_INEXISTENTE / SALDO_INSUFICIENTE) sobre la llave
/// <c>(ubicacion_id, articulo_id, producto_aw_variante_id)</c> (la variante se suma en
/// F1-MP-01; NULL = artículo sin medida), enrutando por un <b>bin explícito</b>.
///
/// <para>Almacén-por-línea PR6a: el trigger dejó de tener fallback a la ÚNICA
/// para líneas sin bin — <c>lineas_movimiento.ubicacion_id</c> es NOT NULL, así
/// que toda línea trae bin. Este test se reescribió para enrutar por un rack
/// real (no la ÚNICA): las entradas a la ÚNICA (es_default) las rechaza el
/// trigger por diseño (<c>ENTRADA_A_UBICACION_UNICA</c>). La cobertura de
/// promedio ponderado y de los guards de salida no cambia.</para>
///
/// <para>Es un test de nivel BD (el trigger es PG puro): inserta un fixture
/// aislado por GUIDs (almacén + sub-almacén + rack) vía SQL crudo sobre la
/// conexión del <see cref="AlmacenDbContext"/>, ejercita el trigger insertando
/// movimientos+líneas, y limpia todo al final. No toca el seed de millet_dev.</para>
/// </summary>
public class TriggerSaldosNivel4Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TriggerSaldosNivel4Tests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Trigger_entrada_y_salida_sobre_pk_ubicacion()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        var almacenId = Guid.NewGuid();
        var subAlmacenId = Guid.NewGuid();
        var unicaId = Guid.NewGuid();          // ÚNICA (es_default) — no recibe entradas
        var ubicacionId = Guid.NewGuid();      // rack real (activo) al que se enruta
        var articuloId = Guid.NewGuid();
        var articuloSinSaldo = Guid.NewGuid();
        var empresaId = Guid.NewGuid();
        var clave = $"T{Guid.NewGuid():N}".Substring(0, 12);

        try
        {
            // ── Fixture aislado ────────────────────────────────────────────
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
                VALUES ({almacenId}, {clave}, 'Test PR2', {Guid.NewGuid()}, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                VALUES ({subAlmacenId}, {almacenId}, {clave}, 'Sub Test', 0, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({unicaId}, {subAlmacenId}, 'ÚNICA', 'Única test', 0, true, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({ubicacionId}, {subAlmacenId}, 'RACK-N4', 'Rack test', 0, false, 0, NOW(), NOW())");

            // ── 1. ENTRADA: crea el saldo en el rack (10 @ 100) ────────────
            await RegistrarMovimientoAsync(db, tipo: 0, subAlmacenId, empresaId, ubicacionId, articuloId, cantidad: 10m, costo: 100m);

            var saldo = await LeerSaldoAsync(db, ubicacionId, articuloId);
            Assert.NotNull(saldo);
            Assert.Equal(ubicacionId, saldo!.UbicacionId);
            Assert.Equal(subAlmacenId, saldo.SubAlmacenId);   // denormalizado, derivado del bin por el trigger
            Assert.Equal(10m, saldo.Cantidad);
            Assert.Equal(100m, saldo.CostoPromedioMxn);
            Assert.Equal(1000m, saldo.ValorInventarioMxn);    // generada = cantidad * costo

            // ── 2. ENTRADA 2: promedio ponderado (10@100 + 10@200 → 20@150) ─
            await RegistrarMovimientoAsync(db, tipo: 0, subAlmacenId, empresaId, ubicacionId, articuloId, cantidad: 10m, costo: 200m);

            saldo = await LeerSaldoAsync(db, ubicacionId, articuloId);
            Assert.Equal(20m, saldo!.Cantidad);
            Assert.Equal(150m, saldo.CostoPromedioMxn);       // (10*100 + 10*200)/20

            // ── 3. SALIDA: decrementa (20 - 5 = 15); costo intacto ─────────
            await RegistrarMovimientoAsync(db, tipo: 1, subAlmacenId, empresaId, ubicacionId, articuloId, cantidad: 5m, costo: 0m);

            saldo = await LeerSaldoAsync(db, ubicacionId, articuloId);
            Assert.Equal(15m, saldo!.Cantidad);
            Assert.Equal(150m, saldo.CostoPromedioMxn);

            // ── 4. SALIDA INSUFICIENTE (15 disponible, pide 1000) ──────────
            var exInsuf = await Assert.ThrowsAnyAsync<Exception>(() =>
                RegistrarMovimientoAsync(db, tipo: 1, subAlmacenId, empresaId, ubicacionId, articuloId, cantidad: 1000m, costo: 0m));
            Assert.Contains("SALDO_INSUFICIENTE", MensajeCompleto(exInsuf));

            // El saldo no cambió tras el fallo.
            saldo = await LeerSaldoAsync(db, ubicacionId, articuloId);
            Assert.Equal(15m, saldo!.Cantidad);

            // ── 5. SALIDA de un artículo SIN saldo → SALDO_INEXISTENTE ─────
            var exInex = await Assert.ThrowsAnyAsync<Exception>(() =>
                RegistrarMovimientoAsync(db, tipo: 1, subAlmacenId, empresaId, ubicacionId, articuloSinSaldo, cantidad: 1m, costo: 0m));
            Assert.Contains("SALDO_INEXISTENTE", MensajeCompleto(exInex));
        }
        finally
        {
            // Limpieza en orden FK-safe (movimientos cascada líneas).
            // PR6a: el movimiento se borra vía la ubicación de sus líneas.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM almacen.movimientos_inventario WHERE id IN (
                     SELECT lm.movimiento_id FROM almacen.lineas_movimiento lm
                     JOIN almacen.ubicaciones u ON u.id = lm.ubicacion_id
                     WHERE u.sub_almacen_id = {subAlmacenId})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.saldos_inventario WHERE sub_almacen_id = {subAlmacenId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE sub_almacen_id = {subAlmacenId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id = {subAlmacenId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {almacenId}");
        }
    }

    // ── F1-MP-01 S2: saldo por variante de medida ─────────────────────────────

    [Fact]
    public async Task Trigger_dos_variantes_del_mismo_articulo_son_dos_filas_y_una_fila_sin_variante()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var (almacenId, subAlmacenId, rackId) = await CrearFixtureAsync(db);
        var articulo = Guid.NewGuid();
        var varA = Guid.NewGuid();
        var varB = Guid.NewGuid();
        try
        {
            // 20 hojas 2440x3660 y 15 hojas 2140x3300 (m²: 178.6080 y 105.9300) en el mismo rack.
            await RegistrarMovimientoAsync(db, 0, subAlmacenId, Guid.NewGuid(), rackId, articulo, 178.6080m, 100m, varA, 2440m, 3660m);
            await RegistrarMovimientoAsync(db, 0, subAlmacenId, Guid.NewGuid(), rackId, articulo, 105.9300m, 200m, varB, 2140m, 3300m);
            // Fila sin variante del mismo artículo: NULLS NOT DISTINCT la mantiene como una sola fila propia.
            await RegistrarMovimientoAsync(db, 0, subAlmacenId, Guid.NewGuid(), rackId, articulo, 1m, 50m);
            await RegistrarMovimientoAsync(db, 0, subAlmacenId, Guid.NewGuid(), rackId, articulo, 2m, 50m);

            var filas = await db.SaldosInventario.AsNoTracking().Where(s => s.UbicacionId == rackId).ToListAsync();
            Assert.Equal(3, filas.Count);
            Assert.Equal(178.6080m, filas.Single(f => f.ProductoAwVarianteId == varA).Cantidad);
            Assert.Equal(105.9300m, filas.Single(f => f.ProductoAwVarianteId == varB).Cantidad);
            Assert.Equal(3m, filas.Single(f => f.ProductoAwVarianteId == null).Cantidad);
            Assert.Equal(3, filas.Select(f => f.Id).Distinct().Count());

            // Salida de A: B y la fila sin variante no se tocan.
            await RegistrarMovimientoAsync(db, 1, subAlmacenId, Guid.NewGuid(), rackId, articulo, 8.9304m, 0m, varA, 2440m, 3660m);
            filas = await db.SaldosInventario.AsNoTracking().Where(s => s.UbicacionId == rackId).ToListAsync();
            Assert.Equal(169.6776m, filas.Single(f => f.ProductoAwVarianteId == varA).Cantidad);
            Assert.Equal(105.9300m, filas.Single(f => f.ProductoAwVarianteId == varB).Cantidad);
        }
        finally { await LimpiarFixtureAsync(db, almacenId, subAlmacenId); }
    }

    [Fact]
    public async Task Trigger_salida_de_una_variante_sin_existencia_en_ella_falla_aunque_otra_tenga()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var (almacenId, subAlmacenId, rackId) = await CrearFixtureAsync(db);
        var articulo = Guid.NewGuid();
        var varA = Guid.NewGuid();
        var varB = Guid.NewGuid();
        try
        {
            await RegistrarMovimientoAsync(db, 0, subAlmacenId, Guid.NewGuid(), rackId, articulo, 100m, 10m, varB, 2140m, 3300m);

            var inexistente = await Assert.ThrowsAnyAsync<Exception>(() =>
                RegistrarMovimientoAsync(db, 1, subAlmacenId, Guid.NewGuid(), rackId, articulo, 1m, 0m, varA, 2440m, 3660m));
            Assert.Contains("SALDO_INEXISTENTE", MensajeCompleto(inexistente));
            Assert.Contains($"variante={varA}", MensajeCompleto(inexistente));

            // Sin variante tampoco hay saldo: la existencia de B no cubre la fila NULL.
            var sinVariante = await Assert.ThrowsAnyAsync<Exception>(() =>
                RegistrarMovimientoAsync(db, 1, subAlmacenId, Guid.NewGuid(), rackId, articulo, 1m, 0m));
            Assert.Contains("SALDO_INEXISTENTE", MensajeCompleto(sinVariante));

            var insuficiente = await Assert.ThrowsAnyAsync<Exception>(() =>
                RegistrarMovimientoAsync(db, 1, subAlmacenId, Guid.NewGuid(), rackId, articulo, 100.0001m, 0m, varB, 2140m, 3300m));
            Assert.Contains("SALDO_INSUFICIENTE", MensajeCompleto(insuficiente));
            Assert.Equal(100m, (await LeerSaldoAsync(db, rackId, articulo, varB))!.Cantidad);
        }
        finally { await LimpiarFixtureAsync(db, almacenId, subAlmacenId); }
    }

    [Fact]
    public async Task Trigger_costo_promedio_se_calcula_por_variante_a_12_decimales()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var (almacenId, subAlmacenId, rackId) = await CrearFixtureAsync(db);
        var articulo = Guid.NewGuid();
        var varA = Guid.NewGuid();
        var varB = Guid.NewGuid();
        try
        {
            // E07 §8: 100 m² a 170 + 353.1 m² a 180 → 453.1 m². La otra medida conserva su propio costo.
            await RegistrarMovimientoAsync(db, 0, subAlmacenId, Guid.NewGuid(), rackId, articulo, 100m, 170m, varA, 3300m, 2140m);
            await RegistrarMovimientoAsync(db, 0, subAlmacenId, Guid.NewGuid(), rackId, articulo, 40m, 90m, varB, 2440m, 1830m);
            await RegistrarMovimientoAsync(db, 0, subAlmacenId, Guid.NewGuid(), rackId, articulo, 353.1m, 180m, varA, 3300m, 2140m);

            var a = (await LeerSaldoAsync(db, rackId, articulo, varA))!;
            Assert.Equal(453.1m, a.Cantidad);
            // 80,558 / 453.1 = 177.792981...; el cuerpo vigente del trigger redondea a 12 decimales (no a 4).
            Assert.Equal(Math.Round(80558m / 453.1m, 12), a.CostoPromedioMxn);
            Assert.Equal(177.7930m, Math.Round(a.CostoPromedioMxn, 4));
            Assert.Equal(90m, (await LeerSaldoAsync(db, rackId, articulo, varB))!.CostoPromedioMxn);
        }
        finally { await LimpiarFixtureAsync(db, almacenId, subAlmacenId); }
    }

    // ── F1-MP-01 S4: lote del proveedor en la llave del saldo (E07 S7) ────────

    [Fact]
    public async Task Trigger_dos_recepciones_de_la_misma_variante_con_lotes_distintos_son_dos_filas_con_costo_compartido()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var (almacenId, subAlmacenId, rackId) = await CrearFixtureAsync(db);
        var articulo = Guid.NewGuid();
        var varA = Guid.NewGuid();
        try
        {
            await RegistrarMovimientoAsync(db, 0, subAlmacenId, Guid.NewGuid(), rackId, articulo, 100m, 170m, varA, 3300m, 2140m, "L-1");
            await RegistrarMovimientoAsync(db, 0, subAlmacenId, Guid.NewGuid(), rackId, articulo, 353.1m, 180m, varA, 3300m, 2140m, "L-2");

            var filas = await db.SaldosInventario.AsNoTracking().Where(s => s.UbicacionId == rackId).ToListAsync();
            Assert.Equal(2, filas.Count);
            var l1 = filas.Single(f => f.Lote == "L-1");
            var l2 = filas.Single(f => f.Lote == "L-2");
            // Cada lote conserva su cantidad; el costo promedio es de la variante y lo comparten las dos filas (E07 S7).
            Assert.Equal(100m, l1.Cantidad);
            Assert.Equal(353.1m, l2.Cantidad);
            var promedio = Math.Round(80558m / 453.1m, 12);
            Assert.Equal(promedio, l1.CostoPromedioMxn);
            Assert.Equal(promedio, l2.CostoPromedioMxn);

            // Una segunda entrada del mismo lote suma en su fila (misma llave) y reparte el nuevo promedio a ambas.
            await RegistrarMovimientoAsync(db, 0, subAlmacenId, Guid.NewGuid(), rackId, articulo, 46.9m, 200m, varA, 3300m, 2140m, "L-1");
            filas = await db.SaldosInventario.AsNoTracking().Where(s => s.UbicacionId == rackId).ToListAsync();
            Assert.Equal(2, filas.Count);
            Assert.Equal(146.9m, filas.Single(f => f.Lote == "L-1").Cantidad);
            var nuevo = Math.Round((80558m + 46.9m * 200m) / 500m, 12);
            // El valor previo ya viene de un promedio redondeado a 12 decimales: se compara con tolerancia de 1e-9.
            Assert.All(filas, f => Assert.True(Math.Abs(nuevo - f.CostoPromedioMxn) < 0.000000001m, $"costo {f.CostoPromedioMxn} vs {nuevo}"));

            // La salida del lote L-1 no toca L-2, y pedir de un lote sin existencia nombra el lote.
            await RegistrarMovimientoAsync(db, 1, subAlmacenId, Guid.NewGuid(), rackId, articulo, 6.9m, 0m, varA, 3300m, 2140m, "L-1");
            Assert.Equal(140m, (await LeerSaldoAsync(db, rackId, articulo, varA, "L-1"))!.Cantidad);
            Assert.Equal(353.1m, (await LeerSaldoAsync(db, rackId, articulo, varA, "L-2"))!.Cantidad);
            var inexistente = await Assert.ThrowsAnyAsync<Exception>(() =>
                RegistrarMovimientoAsync(db, 1, subAlmacenId, Guid.NewGuid(), rackId, articulo, 1m, 0m, varA, 3300m, 2140m, "L-3"));
            Assert.Contains("SALDO_INEXISTENTE", MensajeCompleto(inexistente));
            Assert.Contains("lote=L-3", MensajeCompleto(inexistente));
            // Una salida sin lote no consume las filas con lote (S4b agrega la elección del lote).
            var sinLote = await Assert.ThrowsAnyAsync<Exception>(() =>
                RegistrarMovimientoAsync(db, 1, subAlmacenId, Guid.NewGuid(), rackId, articulo, 1m, 0m, varA, 3300m, 2140m));
            Assert.Contains("SALDO_INEXISTENTE", MensajeCompleto(sinLote));
        }
        finally { await LimpiarFixtureAsync(db, almacenId, subAlmacenId); }
    }

    [Fact]
    public async Task Trigger_la_fila_sin_lote_sigue_funcionando_junto_a_filas_con_lote()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var (almacenId, subAlmacenId, rackId) = await CrearFixtureAsync(db);
        var articulo = Guid.NewGuid();
        try
        {
            // Sin lote (existencia heredada / artículo sin A+W): NULLS NOT DISTINCT la mantiene como una sola fila.
            await RegistrarMovimientoAsync(db, 0, subAlmacenId, Guid.NewGuid(), rackId, articulo, 10m, 100m);
            await RegistrarMovimientoAsync(db, 0, subAlmacenId, Guid.NewGuid(), rackId, articulo, 10m, 200m);
            await RegistrarMovimientoAsync(db, 0, subAlmacenId, Guid.NewGuid(), rackId, articulo, 5m, 300m, lote: "ABC");

            var filas = await db.SaldosInventario.AsNoTracking().Where(s => s.UbicacionId == rackId).ToListAsync();
            Assert.Equal(2, filas.Count);
            Assert.Equal(20m, filas.Single(f => f.Lote == null).Cantidad);
            Assert.Equal(5m, filas.Single(f => f.Lote == "ABC").Cantidad);
            // Costo compartido por (ubicación, artículo, variante NULL): (10×100 + 10×200 + 5×300) / 25 = 180.
            Assert.All(filas, f => Assert.Equal(180m, f.CostoPromedioMxn));

            await RegistrarMovimientoAsync(db, 1, subAlmacenId, Guid.NewGuid(), rackId, articulo, 8m, 0m);
            Assert.Equal(12m, (await LeerSaldoAsync(db, rackId, articulo))!.Cantidad);
            Assert.Equal(5m, (await LeerSaldoAsync(db, rackId, articulo, lote: "ABC"))!.Cantidad);
        }
        finally { await LimpiarFixtureAsync(db, almacenId, subAlmacenId); }
    }

    [Fact]
    public async Task Linea_con_variante_exige_ancho_y_alto_y_viceversa()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var (almacenId, subAlmacenId, rackId) = await CrearFixtureAsync(db);
        try
        {
            var sinMedida = await Assert.ThrowsAnyAsync<Exception>(() =>
                RegistrarMovimientoAsync(db, 0, subAlmacenId, Guid.NewGuid(), rackId, Guid.NewGuid(), 1m, 1m, Guid.NewGuid(), null, null));
            Assert.Contains("ck_lineas_variante_medida", MensajeCompleto(sinMedida));
            var sinVariante = await Assert.ThrowsAnyAsync<Exception>(() =>
                RegistrarMovimientoAsync(db, 0, subAlmacenId, Guid.NewGuid(), rackId, Guid.NewGuid(), 1m, 1m, null, 2440m, 3660m));
            Assert.Contains("ck_lineas_variante_medida", MensajeCompleto(sinVariante));
        }
        finally { await LimpiarFixtureAsync(db, almacenId, subAlmacenId); }
    }

    private static async Task<(Guid AlmacenId, Guid SubAlmacenId, Guid RackId)> CrearFixtureAsync(AlmacenDbContext db)
    {
        var almacenId = Guid.NewGuid();
        var subAlmacenId = Guid.NewGuid();
        var unicaId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        var clave = $"T{Guid.NewGuid():N}".Substring(0, 12);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
            VALUES ({almacenId}, {clave}, 'Test variante', {Guid.NewGuid()}, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
            VALUES ({subAlmacenId}, {almacenId}, {clave}, 'Sub Test', 0, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
            VALUES ({unicaId}, {subAlmacenId}, 'ÚNICA', 'Única test', 0, true, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
            VALUES ({rackId}, {subAlmacenId}, 'RACK-VAR', 'Rack variante', 0, false, 0, NOW(), NOW())");
        return (almacenId, subAlmacenId, rackId);
    }

    private static async Task LimpiarFixtureAsync(AlmacenDbContext db, Guid almacenId, Guid subAlmacenId)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"DELETE FROM almacen.movimientos_inventario WHERE id IN (
                 SELECT lm.movimiento_id FROM almacen.lineas_movimiento lm
                 JOIN almacen.ubicaciones u ON u.id = lm.ubicacion_id
                 WHERE u.sub_almacen_id = {subAlmacenId})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.saldos_inventario WHERE sub_almacen_id = {subAlmacenId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE sub_almacen_id = {subAlmacenId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id = {subAlmacenId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {almacenId}");
    }

    // Inserta un movimiento Registrado (estado=2) + una línea → dispara el
    // trigger. Cada statement es autocommit; al insertar la línea el trigger
    // ve el movimiento padre ya persistido.
    private static async Task RegistrarMovimientoAsync(
        AlmacenDbContext db, short tipo, Guid subAlmacenId, Guid empresaId,
        Guid ubicacionId, Guid articuloId, decimal cantidad, decimal costo,
        Guid? varianteId = null, decimal? anchoMm = null, decimal? altoMm = null, string? lote = null)
    {
        var movimientoId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.movimientos_inventario
                (id, tipo, estado, fecha_movimiento, fecha_registro,
                 empresa_id, pendiente_regularizacion, version, created_at, updated_at)
            VALUES
                ({movimientoId}, {tipo}, 2, CURRENT_DATE, NOW(),
                 {empresaId}, false, 0, NOW(), NOW())");

        // PR6a: ubicacion_id es NOT NULL — la línea SIEMPRE trae bin explícito.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.lineas_movimiento
                (id, movimiento_id, posicion, articulo_id, cantidad, unidad_medida,
                 costo_unitario_mxn, monto_total_mxn, moneda_original, ubicacion_id,
                 producto_aw_variante_id, ancho_mm, alto_mm, lote, version, created_at, updated_at)
            VALUES
                ({Guid.NewGuid()}, {movimientoId}, 1, {articuloId}, {cantidad}, 'PZA',
                 {costo}, {cantidad * costo}, 'MXN', {ubicacionId},
                 {varianteId}, {anchoMm}, {altoMm}, {lote}, 0, NOW(), NOW())");
    }

    private static Task<SaldoInventario?> LeerSaldoAsync(AlmacenDbContext db, Guid ubicacionId, Guid articuloId, Guid? varianteId = null, string? lote = null) =>
        db.SaldosInventario.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UbicacionId == ubicacionId && s.ArticuloId == articuloId && s.ProductoAwVarianteId == varianteId && s.Lote == lote);

    private static string MensajeCompleto(Exception ex) =>
        ex.Message + " | " + (ex.InnerException?.Message ?? string.Empty);
}
