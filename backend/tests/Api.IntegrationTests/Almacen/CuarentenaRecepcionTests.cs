using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Application.Vales;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Calendario;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// ALM-04 CA4.2 con Postgres real y los triggers del ledger: una OC de 10 con 2 dañadas deja 8 en el rack
/// disponible y 2 en el rack de MAT-REV, la OC ve 10 recibidas, el disponible es 8 y lo que está en revisión
/// no se puede surtir. Fixture aislado por GUIDs (SQL crudo) con limpieza en <c>finally</c>; no toca el seed.
/// </summary>
[Collection(PeriodoContableSinParalelo.Nombre)]
public class CuarentenaRecepcionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private static readonly DateOnly Fecha = new(2027, 4, 12);

    public CuarentenaRecepcionTests(WebApplicationFactory<Program> factory) => _factory = factory;

    internal sealed record Ctx(
        Guid EmpresaId, Guid UserId, Guid AlmacenId, Guid SucursalId,
        Guid SubDisponibleId, Guid SubRevisionId,
        Guid RackId, Guid UnicaRevisionId, Guid RackRevisionId,
        Guid ArticuloId, Guid OcId, Guid LineaOcId,
        IPeriodoContableReadPort Periodos, ICalendarioHabil Calendario, ICentroCostoElegibilidadPort Centros);

    [Fact]
    public async Task Recepcion_con_dano_deja_8_disponibles_2_en_revision_y_no_surtibles()
    {
        await EjecutarConFixtureAsync(async (db, ctx) =>
        {
            var eventos = new CapturaEventos();
            await HandlerRecepcion(db, eventos, ctx).Handle(
                new RegistrarRecepcionConFacturaCommand(ctx.OcId, Fecha, null, "AD662D33-6934-459C-A128-BDF0393E0062", null,
                    [new RegistrarRecepcionLineaInput(ctx.ArticuloId, ctx.LineaOcId, 10m, null, null, ctx.RackId,
                        CantidadDanada: 2m, MotivoDano: "Hojas rotas")]),
                CancellationToken.None);

            // (1) Saldos reales del trigger + evento con el total.
            (await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId)).Should().Be(8m);
            (await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId)).Should().Be(2m);
            var evento = eventos.Unico<OcRecepcionRegistradaIntegrationEvent>();
            evento.Lineas.Sum(l => l.Cantidad).Should().Be(10m);
            evento.Lineas.Should().OnlyContain(l => l.CantidadRecibidaAcumulada == 10m);

            // (2) Físico 10, disponible 8.
            var disp = await new AlmacenSaldoQueryAdapter(db)
                .ConsultarDisponibilidadPorAlmacenAsync(ctx.AlmacenId, ctx.ArticuloId, CancellationToken.None);
            disp.Cantidad.Should().Be(10m);
            disp.CantidadDisponible.Should().Be(8m);

            // (3) Surtir desde MAT-REV se rechaza y el saldo no cambia.
            var act = () => HandlerVale(db, new CapturaEventos(), ctx).Handle(
                new RegistrarSalidaPorValeCommand(Fecha, "vales/alm04.pdf", null, null,
                    [new RegistrarSalidaLineaInput(ctx.ArticuloId, ctx.LineaOcId, 1m, null, null, null, null, ctx.RackRevisionId)]),
                CancellationToken.None);
            (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("SALIDA_DESDE_REVISION");
            (await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId)).Should().Be(2m);
        });
    }

    // ─────────────────────────── Fixture (internal para reutilizar en rebanadas siguientes) ───────────────────────────

    internal async Task EjecutarConFixtureAsync(Func<AlmacenDbContext, Ctx, Task> cuerpo, Guid? empresaFija = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var empresaCtx = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        // empresaFija: las pruebas HTTP necesitan la empresa del usuario autenticado para ver el documento.
        var empresaId = empresaFija ?? Guid.NewGuid();
        await using var calendario = new PeriodoContableFixture(db.Database.GetConnectionString()!, empresaId, Fecha.Year);
        await calendario.SembrarAsync(Fecha.Month);

        var ctx = new Ctx(empresaId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            calendario.Port,
            scope.ServiceProvider.GetRequiredService<ICalendarioHabil>(),
            scope.ServiceProvider.GetRequiredService<ICentroCostoElegibilidadPort>());
        var clave = $"A4{Guid.NewGuid():N}"[..12];

        try
        {
            await SembrarAsync(db, ctx, clave);
            using (empresaCtx.Bypass()) await cuerpo(db, ctx);
        }
        finally
        {
            db.ChangeTracker.Clear();
            // Los dictámenes (ALM-04 R3) apuntan a lineas_movimiento con FK restrict: primero ellos.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM almacen.dictamenes_revision WHERE linea_revision_id IN (
                     SELECT lm.id FROM almacen.lineas_movimiento lm
                     JOIN almacen.ubicaciones u ON u.id = lm.ubicacion_id
                     JOIN almacen.sub_almacenes s ON s.id = u.sub_almacen_id
                     WHERE s.almacen_id = {ctx.AlmacenId})");
            // Rechazo por dictamen (ALM-04 R4): la devolución a proveedor referencia el movimiento de revisión.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM almacen.devoluciones_proveedor WHERE recepcion_origen_id IN (
                     SELECT lm.movimiento_id FROM almacen.lineas_movimiento lm
                     JOIN almacen.ubicaciones u ON u.id = lm.ubicacion_id
                     JOIN almacen.sub_almacenes s ON s.id = u.sub_almacen_id
                     WHERE s.almacen_id = {ctx.AlmacenId})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM almacen.movimientos_inventario WHERE id IN (
                     SELECT lm.movimiento_id FROM almacen.lineas_movimiento lm
                     JOIN almacen.ubicaciones u ON u.id = lm.ubicacion_id
                     JOIN almacen.sub_almacenes s ON s.id = u.sub_almacen_id
                     WHERE s.almacen_id = {ctx.AlmacenId})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.saldos_inventario WHERE sub_almacen_id IN ({ctx.SubDisponibleId}, {ctx.SubRevisionId})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM almacen.asignaciones_articulo_ubicacion WHERE ubicacion_id = {ctx.RackId}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.ubicaciones WHERE sub_almacen_id IN ({ctx.SubDisponibleId}, {ctx.SubRevisionId})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.sub_almacenes WHERE almacen_id = {ctx.AlmacenId}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.almacenes WHERE id = {ctx.AlmacenId}");
        }
    }

    private static async Task SembrarAsync(AlmacenDbContext db, Ctx ctx, string clave)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
            VALUES ({ctx.AlmacenId}, {clave}, 'Test ALM-04', {ctx.SucursalId}, 0, 0, NOW(), NOW())");
        var claveRev = clave + "R";
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
            VALUES ({ctx.SubDisponibleId}, {ctx.AlmacenId}, {clave}, 'Insumos ALM-04', 0, 0, 0, NOW(), NOW()),
                   ({ctx.SubRevisionId}, {ctx.AlmacenId}, {claveRev}, 'MAT-REV ALM-04', 2, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
            VALUES ({ctx.RackId}, {ctx.SubDisponibleId}, 'R-1', 'Rack 1 ALM-04', 0, false, 0, NOW(), NOW()),
                   ({ctx.UnicaRevisionId}, {ctx.SubRevisionId}, 'UNICA', 'Única MAT-REV', 0, true, 0, NOW(), NOW()),
                   ({ctx.RackRevisionId}, {ctx.SubRevisionId}, 'REV-01', 'Revisión 1', 0, false, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.asignaciones_articulo_ubicacion (id, ubicacion_id, articulo_id, estatus, version, created_at, updated_at)
            VALUES ({Guid.NewGuid()}, {ctx.RackId}, {ctx.ArticuloId}, 0, 0, NOW(), NOW())");
    }

    internal static RegistrarRecepcionConFacturaHandler HandlerRecepcion(AlmacenDbContext db, IIntegrationEventPublisher eventos, Ctx ctx) =>
        new(db, new OcFake(ctx), eventos, new UserFake(ctx.UserId), new EmpresaFake(ctx.EmpresaId),
            new DecimalesNulo(), ctx.Periodos, new ConversionIdentidad(), new CxpFake(), new ProveedorFake());

    internal static RegistrarSalidaPorValeHandler HandlerVale(AlmacenDbContext db, IIntegrationEventPublisher eventos, Ctx ctx) =>
        new(db, eventos, new UserFake(ctx.UserId), new EmpresaFake(ctx.EmpresaId), new DecimalesNulo(), ctx.Periodos,
            ctx.Calendario, ctx.Centros, new ConversionIdentidad(), new ApartadosRequisicionService(db, new AlmacenSaldoQueryAdapter(db)));

    internal static async Task<decimal?> LeerSaldoAsync(AlmacenDbContext db, Guid ubicacionId, Guid articuloId)
    {
        db.ChangeTracker.Clear();
        return await db.SaldosInventario.AsNoTracking()
            .Where(s => s.UbicacionId == ubicacionId && s.ArticuloId == articuloId)
            .Select(s => (decimal?)s.Cantidad).FirstOrDefaultAsync();
    }

    // ─────────────────────────── Dobles ───────────────────────────

    internal sealed class CapturaEventos : IIntegrationEventPublisher
    {
        public List<object> Eventos { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct) { Eventos.Add(integrationEvent); return Task.CompletedTask; }
        public T Unico<T>() => Eventos.OfType<T>().Single();
    }

    private sealed class OcFake(Ctx ctx) : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) => Task.FromResult<OcLectura?>(
            new(ocId, "OC-ALM04", Guid.NewGuid(), ctx.EmpresaId, "Autorizada",
                [new OcLineaLectura(ctx.LineaOcId, ctx.ArticuloId, "PZA", 10m, 0m, 100m)]));
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class ArticuloFake : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken ct) =>
            Task.FromResult<ArticuloLectura?>(new ArticuloLectura(articuloId, "ART", "Artículo", "PZA", null, true));
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(new Dictionary<Guid, ArticuloLectura>());
    }

    /// <summary>Puerto de artículos donde todo id existe con la unidad base indicada (handlers 8.A, ALM-04).</summary>
    internal sealed class ArticulosUnidad(string unidad = "PZA") : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken ct) =>
            Task.FromResult<ArticuloLectura?>(new ArticuloLectura(articuloId, "ART", "Artículo", unidad, null, true));
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(
                ids.ToDictionary(i => i, i => new ArticuloLectura(i, "ART", "Artículo", unidad, null, true)));
    }

    private sealed class CxpFake : ICxpDocumentosReadPort
    {
        public Task<CfdiRecibidoLectura?> ObtenerCfdiAsync(Guid id, CancellationToken ct) => Task.FromResult<CfdiRecibidoLectura?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosFacturaAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Vacio();
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerUuidsFiscalesCfdiAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Vacio();
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosNotaCreditoAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Vacio();
        public Task<IReadOnlyList<FacturaLigadaRecepcionLectura>> ObtenerFacturasLigadasRecepcionAsync(
            IReadOnlyCollection<Guid> recepcionIds, IReadOnlyCollection<Guid> facturaIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FacturaLigadaRecepcionLectura>>([]);
        private static Task<IReadOnlyDictionary<Guid, string>> Vacio() => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class ProveedorFake : IProveedorReadPort
    {
        public Task<ProveedorLectura?> ObtenerAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ProveedorLectura?>(new ProveedorLectura(id, "P", "Proveedor", "AAA010101AAA", true));
        public Task<IReadOnlyDictionary<Guid, ProveedorLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorLectura>>(new Dictionary<Guid, ProveedorLectura>());
    }

    internal sealed class DecimalesNulo : IDecimalesUnidadGuard
    {
        public Task ValidarAsync(IEnumerable<CantidadAValidar> cantidades, CancellationToken ct) => Task.CompletedTask;
    }

    internal sealed class UserFake(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public string? UserName => "Test ALM-04";
    }

    internal sealed class EmpresaFake(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }

    private sealed class ConversionIdentidad : IConversionUnidadPort
    {
        public Task<ConversionUnidad> ConvertirAsync(Guid articuloId, decimal cantidad, string? capturada, string documento, CancellationToken ct)
        {
            var unidad = string.IsNullOrWhiteSpace(documento) ? "PZA" : documento;
            return Task.FromResult(new ConversionUnidad(cantidad, unidad, cantidad, 1, unidad, cantidad));
        }
    }
}
