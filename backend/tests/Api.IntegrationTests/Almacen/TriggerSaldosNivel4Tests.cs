using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Tests de integración del trigger <c>tg_movimientos_actualizar_saldo</c>.
/// Verifica ENTRADA (upsert + promedio ponderado) y SALIDA (FOR UPDATE +
/// SALDO_INEXISTENTE / SALDO_INSUFICIENTE) sobre la PK
/// <c>(ubicacion_id, articulo_id)</c>, enrutando por un <b>bin explícito</b>.
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

    // Inserta un movimiento Registrado (estado=2) + una línea → dispara el
    // trigger. Cada statement es autocommit; al insertar la línea el trigger
    // ve el movimiento padre ya persistido.
    private static async Task RegistrarMovimientoAsync(
        AlmacenDbContext db, short tipo, Guid subAlmacenId, Guid empresaId,
        Guid ubicacionId, Guid articuloId, decimal cantidad, decimal costo)
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
                 costo_unitario_mxn, monto_total_mxn, moneda_original, ubicacion_id, version, created_at, updated_at)
            VALUES
                ({Guid.NewGuid()}, {movimientoId}, 1, {articuloId}, {cantidad}, 'PZA',
                 {costo}, {cantidad * costo}, 'MXN', {ubicacionId}, 0, NOW(), NOW())");
    }

    private static Task<SaldoInventario?> LeerSaldoAsync(AlmacenDbContext db, Guid ubicacionId, Guid articuloId) =>
        db.SaldosInventario.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UbicacionId == ubicacionId && s.ArticuloId == articuloId);

    private static string MensajeCompleto(Exception ex) =>
        ex.Message + " | " + (ex.InnerException?.Message ?? string.Empty);
}
