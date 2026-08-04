using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Conteos;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Tests de integración de C7.2a (ADR-0047): el trigger
/// <c>tg_movimientos_actualizar_saldo</c> enrutando por bin EXPLÍCITO
/// (<c>lineas_movimiento.ubicacion_id</c>) + los puentes deterministas ante
/// N bins (snapshot de conteo agregado, adapter público de Compras).
///
/// <para>El path de compatibilidad (línea sin bin → ÚNICA) lo cubre la clase
/// <see cref="TriggerSaldosNivel4Tests"/>, que debe seguir verde sin tocarse.
/// Mismo molde: fixture aislado por GUIDs vía SQL crudo, cleanup en finally,
/// no toca el seed de millet_dev.</para>
/// </summary>
public class TriggerBinExplicitoTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TriggerBinExplicitoTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Trigger_enruta_por_bin_explicito_y_valida()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        var almacenId = Guid.NewGuid();
        var subS1 = Guid.NewGuid();
        var subS2 = Guid.NewGuid();
        var unicaS1 = Guid.NewGuid();     // ÚNICA (es_default) de S1
        var unicaS2 = Guid.NewGuid();     // ÚNICA de S2
        var binB1 = Guid.NewGuid();       // bin real activo de S1
        var binB2 = Guid.NewGuid();       // bin real activo de S1
        var binB3 = Guid.NewGuid();       // bin real INACTIVO de S1
        var binAjeno = Guid.NewGuid();    // bin real de S2 (otro sub-almacén)
        var articuloX = Guid.NewGuid();
        var articuloY = Guid.NewGuid();
        var empresaId = Guid.NewGuid();
        var clave = $"T{Guid.NewGuid():N}".Substring(0, 12);

        try
        {
            // ── Fixture aislado ────────────────────────────────────────────
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
                VALUES ({almacenId}, {clave}, 'Test C7.2a', {Guid.NewGuid()}, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                VALUES ({subS1}, {almacenId}, {clave + "1"}, 'Sub S1', 0, 0, 0, NOW(), NOW()),
                       ({subS2}, {almacenId}, {clave + "2"}, 'Sub S2', 0, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({unicaS1}, {subS1}, 'ÚNICA', 'Única S1', 0, true,  0, NOW(), NOW()),
                       ({unicaS2}, {subS2}, 'ÚNICA', 'Única S2', 0, true,  0, NOW(), NOW()),
                       ({binB1},   {subS1}, 'B1',    'Rack B1',  0, false, 0, NOW(), NOW()),
                       ({binB2},   {subS1}, 'B2',    'Rack B2',  0, false, 0, NOW(), NOW()),
                       ({binB3},   {subS1}, 'B3',    'Rack B3 inactivo', 1, false, 0, NOW(), NOW()),
                       ({binAjeno}, {subS2}, 'BX',   'Rack de S2', 0, false, 0, NOW(), NOW())");

            // ── 1. La ÚNICA arranca con saldo histórico (10@100). PR6a retiró
            //       el fallback 'entrada sin bin → ÚNICA', y una entrada CON bin
            //       = ÚNICA la rechaza el trigger (caso 5). El stock de la ÚNICA
            //       solo existe por backfill/histórico → se siembra directo. ──
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.saldos_inventario
                    (ubicacion_id, sub_almacen_id, articulo_id, cantidad,
                     costo_promedio_mxn, ultima_actualizacion_at, ultimo_movimiento_id)
                VALUES ({unicaS1}, {subS1}, {articuloX}, 10, 100, NOW(), NULL)");
            var saldoUnica = await LeerSaldoAsync(db, unicaS1, articuloX);
            Assert.Equal(10m, saldoUnica!.Cantidad);

            // ── 2. Entrada CON bin B1 → aterriza en B1, la ÚNICA no cambia ─
            await RegistrarMovimientoAsync(db, tipo: 0, subS1, empresaId, articuloX, 10m, 100m, binB1);
            var saldoB1 = await LeerSaldoAsync(db, binB1, articuloX);
            Assert.Equal(10m, saldoB1!.Cantidad);
            Assert.Equal(100m, saldoB1.CostoPromedioMxn);
            Assert.Equal(10m, (await LeerSaldoAsync(db, unicaS1, articuloX))!.Cantidad);

            // ── 3. Mismo artículo en B2 a otro costo → promedios
            //       INDEPENDIENTES por bin; la suma del sub-almacén cuadra ──
            await RegistrarMovimientoAsync(db, tipo: 0, subS1, empresaId, articuloX, 10m, 300m, binB2);
            var saldoB2 = await LeerSaldoAsync(db, binB2, articuloX);
            Assert.Equal(300m, saldoB2!.CostoPromedioMxn);
            Assert.Equal(100m, (await LeerSaldoAsync(db, binB1, articuloX))!.CostoPromedioMxn);
            var sumaSub = await db.SaldosInventario.AsNoTracking()
                .Where(s => s.SubAlmacenId == subS1 && s.ArticuloId == articuloX)
                .SumAsync(s => s.Cantidad);
            Assert.Equal(30m, sumaSub);

            // ── 4. (PR6a) El check de pertenencia contra la cabecera
            //       (UBICACION_NO_PERTENECE_AL_SUBALMACEN) DESAPARECE: sin
            //       cabecera, una línea única a cualquier bin es válida (su sub
            //       se deriva del bin). La consistencia pasa a ser entre líneas
            //       hermanas → MOVIMIENTO_MULTI_SUBALMACEN, cubierto por
            //       Pr6aNotNullYMultiSubalmacenTests. Aquí ya no hay caso 4.

            // ── 5. Entrada a la ÚNICA con bin explícito → rechazo
            //       (la ÚNICA solo se drena; muere sola) ─────────────────────
            var exUnica = await Assert.ThrowsAnyAsync<Exception>(() =>
                RegistrarMovimientoAsync(db, tipo: 0, subS1, empresaId, articuloX, 1m, 100m, unicaS1));
            Assert.Contains("ENTRADA_A_UBICACION_UNICA", MensajeCompleto(exUnica));

            // ── 6. Entrada a bin INACTIVO → rechazo ────────────────────────
            var exInactiva = await Assert.ThrowsAnyAsync<Exception>(() =>
                RegistrarMovimientoAsync(db, tipo: 0, subS1, empresaId, articuloX, 1m, 100m, binB3));
            Assert.Contains("UBICACION_INACTIVA", MensajeCompleto(exInactiva));

            // ── 7. SALIDA con bin explícito = ÚNICA → PERMITIDA (drenaje
            //       del stock histórico, la asimetría del modelo) ────────────
            await RegistrarMovimientoAsync(db, tipo: 1, subS1, empresaId, articuloX, 5m, 0m, unicaS1);
            Assert.Equal(5m, (await LeerSaldoAsync(db, unicaS1, articuloX))!.Cantidad);

            // ── 8. Guard POR BIN: salida de bin sin fila de saldo falla
            //       aunque la ÚNICA del mismo sub-almacén tenga stock ────────
            //       (Y solo en la ÚNICA — sembrado directo, no vía entrada).
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.saldos_inventario
                    (ubicacion_id, sub_almacen_id, articulo_id, cantidad,
                     costo_promedio_mxn, ultima_actualizacion_at, ultimo_movimiento_id)
                VALUES ({unicaS1}, {subS1}, {articuloY}, 10, 50, NOW(), NULL)");
            var exInex = await Assert.ThrowsAnyAsync<Exception>(() =>
                RegistrarMovimientoAsync(db, tipo: 1, subS1, empresaId, articuloY, 1m, 0m, binB1));
            Assert.Contains("SALDO_INEXISTENTE", MensajeCompleto(exInex));

            //       ... y salida de bin con MENOS stock que el pedido falla
            //       aunque el total del sub-almacén alcance.
            var exInsuf = await Assert.ThrowsAnyAsync<Exception>(() =>
                RegistrarMovimientoAsync(db, tipo: 1, subS1, empresaId, articuloX, 50m, 0m, binB2));
            Assert.Contains("SALDO_INSUFICIENTE", MensajeCompleto(exInsuf));

            // ── 9. Salida normal de bin real → descuenta solo ese bin ──────
            await RegistrarMovimientoAsync(db, tipo: 1, subS1, empresaId, articuloX, 4m, 0m, binB2);
            Assert.Equal(6m, (await LeerSaldoAsync(db, binB2, articuloX))!.Cantidad);
            Assert.Equal(10m, (await LeerSaldoAsync(db, binB1, articuloX))!.Cantidad);
        }
        finally
        {
            await LimpiarAsync(db, almacenId, new[] { subS1, subS2 },
                new[] { unicaS1, unicaS2, binB1, binB2, binB3, binAjeno });
        }
    }

    [Fact]
    public async Task Conteo_snapshot_con_dos_bins_genera_una_linea_por_rack()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        var almacenId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var unicaId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var conteoId = Guid.NewGuid();
        var clave = $"T{Guid.NewGuid():N}".Substring(0, 12);

        try
        {
            await SembrarDosBinsConSaldoAsync(
                db, almacenId, subId, unicaId, binId, articuloId, clave);

            // Conteo Rotativo (Anual crearía bloqueos, fuera de alcance aquí).
            var conteo = new ConteoInventario(
                id: conteoId,
                empresaId: Guid.NewGuid(),
                tipo: TipoConteo.Rotativo,
                fechaPlanificada: DateOnly.FromDateTime(DateTime.UtcNow.Date),
                responsableId: Guid.NewGuid(),
                subAlmacenId: subId,
                filtroFamilia: null);
            db.Set<ConteoInventario>().Add(conteo);
            // ConteoInventario es IPerteneceAEmpresa; fuera de un request el
            // interceptor exige el Bypass (mismo que usan seeds/migraciones).
            var empresaCtx = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
            using (empresaCtx.Bypass())
            {
                await db.SaveChangesAsync();
                await new IniciarConteoHandler(db).Handle(
                    new IniciarConteoCommand(conteoId), CancellationToken.None);
            }

            // C7.2c: UNA línea POR RACK — cada fila de saldo genera su propia
            // línea con su cantidad y su costo, sin agregar ni ponderar.
            var lineas = await db.Set<LineaConteo>().AsNoTracking()
                .Where(l => l.ConteoId == conteoId && l.ArticuloId == articuloId)
                .ToListAsync();
            Assert.Equal(2, lineas.Count);

            var lineaUnica = Assert.Single(lineas, l => l.UbicacionId == unicaId);
            Assert.Equal(10m, lineaUnica.CantidadTeorica);
            Assert.Equal(100m, lineaUnica.CostoPromedioSnapshot);

            var lineaBin = Assert.Single(lineas, l => l.UbicacionId == binId);
            Assert.Equal(20m, lineaBin.CantidadTeorica);
            Assert.Equal(250m, lineaBin.CostoPromedioSnapshot);
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.lineas_conteo WHERE conteo_id = {conteoId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.conteos_inventario WHERE id = {conteoId}");
            await LimpiarAsync(db, almacenId, new[] { subId }, new[] { unicaId, binId });
        }
    }

    [Fact]
    public async Task Adapter_compras_suma_los_bins_del_sub_almacen()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        var almacenId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var unicaId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var clave = $"T{Guid.NewGuid():N}".Substring(0, 12);

        try
        {
            await SembrarDosBinsConSaldoAsync(
                db, almacenId, subId, unicaId, binId, articuloId, clave);

            // El saldo del sub-almacén = agregado de sus bins, no una fila:
            // 10@100 (ÚNICA) + 20@250 (bin) → 30 piezas @ ponderado 200.
            var saldo = await new AlmacenSaldoQueryAdapter(db)
                .ConsultarAsync(articuloId, subId, CancellationToken.None);

            Assert.NotNull(saldo);
            Assert.Equal(30m, saldo!.Cantidad);
            Assert.Equal(30m, saldo.CantidadDisponible);
            Assert.Equal(200m, saldo.CostoPromedioMxn);
            Assert.Equal(6000m, saldo.ValorInventarioMxn);
        }
        finally
        {
            await LimpiarAsync(db, almacenId, new[] { subId }, new[] { unicaId, binId });
        }
    }

    // ─── Helpers (molde TriggerSaldosNivel4Tests) ────────────────────────────

    /// <summary>
    /// Fixture compartido de los puentes: almacén + sub-almacén + ÚNICA con
    /// saldo 10@100 + bin real con saldo 20@250 del mismo artículo. Los saldos
    /// se insertan directo (mismas columnas que el INSERT del trigger).
    /// </summary>
    private static async Task SembrarDosBinsConSaldoAsync(
        AlmacenDbContext db, Guid almacenId, Guid subId, Guid unicaId,
        Guid binId, Guid articuloId, string clave)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
            VALUES ({almacenId}, {clave}, 'Test C7.2a', {Guid.NewGuid()}, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
            VALUES ({subId}, {almacenId}, {clave}, 'Sub Test', 0, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
            VALUES ({unicaId}, {subId}, 'ÚNICA', 'Única test', 0, true,  0, NOW(), NOW()),
                   ({binId},   {subId}, 'B1',    'Rack test',  0, false, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.saldos_inventario
                (ubicacion_id, sub_almacen_id, articulo_id, cantidad,
                 costo_promedio_mxn, ultima_actualizacion_at, ultimo_movimiento_id)
            VALUES ({unicaId}, {subId}, {articuloId}, 10, 100, NOW(), NULL),
                   ({binId},   {subId}, {articuloId}, 20, 250, NOW(), NULL)");
    }

    /// <summary>
    /// Inserta un movimiento Registrado (estado=2) + una línea con bin
    /// opcional → dispara el trigger con NEW.ubicacion_id poblado o NULL.
    /// </summary>
    private static async Task RegistrarMovimientoAsync(
        AlmacenDbContext db, short tipo, Guid subAlmacenId, Guid empresaId,
        Guid articuloId, decimal cantidad, decimal costo, Guid? ubicacionId)
    {
        var movimientoId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.movimientos_inventario
                (id, tipo, estado, fecha_movimiento, fecha_registro,
                 empresa_id, pendiente_regularizacion, version, created_at, updated_at)
            VALUES
                ({movimientoId}, {tipo}, 2, CURRENT_DATE, NOW(),
                 {empresaId}, false, 0, NOW(), NOW())");

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.lineas_movimiento
                (id, movimiento_id, posicion, articulo_id, cantidad, unidad_medida,
                 costo_unitario_mxn, monto_total_mxn, moneda_original, ubicacion_id, version, created_at, updated_at)
            VALUES
                ({Guid.NewGuid()}, {movimientoId}, 1, {articuloId}, {cantidad}, 'PZA',
                 {costo}, {cantidad * costo}, 'MXN', {ubicacionId}, 0, NOW(), NOW())");
    }

    // ─── C7.2c: ajustes de conteo por rack (el linchpin) ─────────────────────

    [Fact]
    public async Task Conteo_ajuste_positivo_siembra_el_rack_real_no_la_unica()
    {
        await EjercitarAjusteDeConteoAsync(
            teoricaRack: 5m, real: 8m,
            asserts: async (db, unicaId, rackId, articuloId, movimientos) =>
            {
                // El AjustePositivo (rack real, no default) lo permite el
                // trigger: el saldo del rack sube 5 → 8. Sin excepción.
                var saldoRack = await LeerSaldoAsync(db, rackId, articuloId);
                Assert.Equal(8m, saldoRack!.Cantidad);
                // La ÚNICA sigue vacía — ningún ajuste aterrizó ahí.
                Assert.Null(await LeerSaldoAsync(db, unicaId, articuloId));
                // El movimiento generado es AjustePositivo sobre el rack.
                var mov = Assert.Single(movimientos);
                Assert.Equal(TipoMovimiento.AjustePositivo, mov.Tipo);
                Assert.Equal(rackId, mov.Lineas.Single().UbicacionId);
            });
    }

    [Fact]
    public async Task Conteo_ajuste_negativo_drena_el_rack_contado()
    {
        await EjercitarAjusteDeConteoAsync(
            teoricaRack: 5m, real: 3m,
            asserts: async (db, _, rackId, articuloId, movimientos) =>
            {
                // AjusteNegativo: descuenta 2 del rack (5 → 3). El guard SALDO_*
                // del trigger es por bin; el rack tiene 5, alcanza.
                var saldoRack = await LeerSaldoAsync(db, rackId, articuloId);
                Assert.Equal(3m, saldoRack!.Cantidad);
                var mov = Assert.Single(movimientos);
                Assert.Equal(TipoMovimiento.AjusteNegativo, mov.Tipo);
                Assert.Equal(rackId, mov.Lineas.Single().UbicacionId);
            });
    }

    /// <summary>
    /// Flujo E2E del conteo por rack (ADR-0047 C7.2c) con la ÚNICA VACÍA
    /// (arranque nuevo): siembra un rack real con <paramref name="teoricaRack"/>,
    /// corre el snapshot (una línea por rack), captura <paramref name="real"/>,
    /// aprueba y aplica — el trigger recibe el ajuste con el bin explícito.
    /// </summary>
    private async Task EjercitarAjusteDeConteoAsync(
        decimal teoricaRack, decimal real,
        Func<AlmacenDbContext, Guid, Guid, Guid, List<MovimientoInventario>, Task> asserts)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var empresaCtx = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();

        var almacenId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var unicaId = Guid.NewGuid();  // ÚNICA es_default, SIN saldo (arranque nuevo)
        var rackId = Guid.NewGuid();   // rack real con saldo
        var articuloId = Guid.NewGuid();
        var empresaId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var conteoId = Guid.NewGuid();
        var clave = $"T{Guid.NewGuid():N}".Substring(0, 12);

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
                VALUES ({almacenId}, {clave}, 'Test C7.2c', {Guid.NewGuid()}, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                VALUES ({subId}, {almacenId}, {clave}, 'Sub Test', 0, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({unicaId}, {subId}, 'ÚNICA', 'Única test', 0, true,  0, NOW(), NOW()),
                       ({rackId},  {subId}, 'B1',    'Rack test',  0, false, 0, NOW(), NOW())");
            // Solo el rack tiene saldo; la ÚNICA nace vacía.
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.saldos_inventario
                    (ubicacion_id, sub_almacen_id, articulo_id, cantidad,
                     costo_promedio_mxn, ultima_actualizacion_at, ultimo_movimiento_id)
                VALUES ({rackId}, {subId}, {articuloId}, {teoricaRack}, 100, NOW(), NULL)");

            var conteo = new ConteoInventario(
                id: conteoId, empresaId: empresaId, tipo: TipoConteo.Rotativo,
                fechaPlanificada: DateOnly.FromDateTime(DateTime.UtcNow.Date),
                responsableId: Guid.NewGuid(), subAlmacenId: subId, filtroFamilia: null);
            db.Set<ConteoInventario>().Add(conteo);

            using (empresaCtx.Bypass())
            {
                await db.SaveChangesAsync();
                await new IniciarConteoHandler(db).Handle(
                    new IniciarConteoCommand(conteoId), CancellationToken.None);

                // Snapshot: una línea, la del rack (la ÚNICA vacía no genera línea).
                var lineas = await db.Set<LineaConteo>()
                    .Where(l => l.ConteoId == conteoId).ToListAsync();
                var linea = Assert.Single(lineas);
                Assert.Equal(rackId, linea.UbicacionId);
                Assert.Equal(teoricaRack, linea.CantidadTeorica);

                var conteoTracked = await db.Set<ConteoInventario>()
                    .Include(c => c.Lineas)
                    .FirstAsync(c => c.Id == conteoId);
                conteoTracked.Lineas.Single().Capturar(real, userId);
                conteoTracked.EnviarAConciliacion();
                conteoTracked.Aprobar(Guid.NewGuid());
                await db.SaveChangesAsync();

                var resp = await new AplicarConteoHandler(
                    db, new NoOpEvents(), new FakeUserCtx(userId), new FakeEmpresaCtx(empresaId))
                    .Handle(new AplicarConteoCommand(conteoId), CancellationToken.None);
                Assert.Equal(1, resp.MovimientosGenerados);

                // La consulta de movimientos (IPerteneceAEmpresa) ignora el
                // filtro global de empresa (el ajuste se creó con un empresaId
                // sintético del test).
                var movimientos = await db.Set<MovimientoInventario>().AsNoTracking()
                    .IgnoreQueryFilters()
                    .Include(m => m.Lineas)
                    .Where(m => m.ConteoId == conteoId)
                    .ToListAsync();
                await asserts(db, unicaId, rackId, articuloId, movimientos);
            }
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.movimientos_inventario WHERE conteo_id = {conteoId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.lineas_conteo WHERE conteo_id = {conteoId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.conteos_inventario WHERE id = {conteoId}");
            await LimpiarAsync(db, almacenId, new[] { subId }, new[] { unicaId, rackId });
        }
    }

    private static Task<SaldoInventario?> LeerSaldoAsync(AlmacenDbContext db, Guid ubicacionId, Guid articuloId) =>
        db.SaldosInventario.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UbicacionId == ubicacionId && s.ArticuloId == articuloId);

    private sealed class NoOpEvents : IIntegrationEventPublisher
    {
        public Task PublishAsync(object integrationEvent, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeUserCtx(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public string? UserName => "Test C7.2c";
    }

    private sealed class FakeEmpresaCtx(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }

    private static async Task LimpiarAsync(
        AlmacenDbContext db, Guid almacenId, Guid[] subAlmacenes, Guid[] ubicaciones)
    {
        foreach (var subId in subAlmacenes)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM almacen.movimientos_inventario WHERE id IN (
                     SELECT lm.movimiento_id FROM almacen.lineas_movimiento lm
                     JOIN almacen.ubicaciones u ON u.id = lm.ubicacion_id
                     WHERE u.sub_almacen_id = {subId})");
        foreach (var ubicacionId in ubicaciones)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.saldos_inventario WHERE ubicacion_id = {ubicacionId}");
        foreach (var ubicacionId in ubicaciones)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE id = {ubicacionId}");
        foreach (var subId in subAlmacenes)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id = {subId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {almacenId}");
    }

    private static string MensajeCompleto(Exception ex) =>
        ex.Message + " | " + (ex.InnerException?.Message ?? string.Empty);
}
