using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>
/// Almacén-por-línea (PR recepción): el sub-almacén ya no viene en cabecera; se
/// deriva del bin de cada línea. El handler valida el invariante
/// "un movimiento = un sub-almacén" (<c>RECEPCION_MULTI_SUBALMACEN</c>) antes de
/// persistir; el trigger PG <c>MOVIMIENTO_MULTI_SUBALMACEN</c> es el backstop.
/// </summary>
public class RegistrarRecepcionMultiSubAlmacenTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();

    private static AlmacenDbContext NuevaDb()
    {
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-recep-multisub-{Guid.NewGuid():N}")
            .Options;
        var db = new AlmacenDbContext(opts, new FakeEmpresa(EmpresaId));
        db.Database.EnsureCreated();
        return db;
    }

    private static RegistrarRecepcionConFacturaHandler NuevoHandler(AlmacenDbContext db) =>
        new(db, new FakeOc(), new FakeArticulos(), new FakeEvents(),
            new FakeUser(), new FakeEmpresa(EmpresaId), new FakeDecimales());

    private static RegistrarRecepcionConFacturaCommand Comando(
        params (Guid articuloId, Guid ubicacionId)[] lineas) => new(
        OrdenCompraId: Guid.NewGuid(),
        FechaMovimiento: new DateOnly(2026, 5, 23),
        CfdiRecibidoId: Guid.NewGuid(),
        CfdiUuidFiscal: null,
        Observaciones: null,
        Lineas: lineas
            .Select(l => new RegistrarRecepcionLineaInput(
                l.articuloId, null, 5m, null, null, l.ubicacionId))
            .ToList());

    [Fact]
    public async Task Recepcion_con_bins_de_subs_distintos_lanza_RECEPCION_MULTI_SUBALMACEN()
    {
        var db = NuevaDb();
        await using var _ = db;
        var subA = Guid.NewGuid();
        var subB = Guid.NewGuid();
        var rackA = Guid.NewGuid();
        var rackB = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        // Un artículo asignado a un rack en CADA sub-almacén.
        db.Ubicaciones.Add(new Ubicacion(rackA, subA, "A-1", "Rack A", esDefault: false));
        db.Ubicaciones.Add(new Ubicacion(rackB, subB, "B-1", "Rack B", esDefault: false));
        db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), rackA, articuloId));
        db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), rackB, articuloId));
        await db.SaveChangesAsync();

        var cmd = Comando((articuloId, rackA), (articuloId, rackB));

        var act = () => NuevoHandler(db).Handle(cmd, CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("RECEPCION_MULTI_SUBALMACEN");
    }

    [Fact]
    public async Task Recepcion_con_bins_del_mismo_sub_registra_ok()
    {
        var db = NuevaDb();
        await using var _ = db;
        var sub = Guid.NewGuid();
        var rack1 = Guid.NewGuid();
        var rack2 = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        db.Ubicaciones.Add(new Ubicacion(rack1, sub, "R-1", "Rack 1", esDefault: false));
        db.Ubicaciones.Add(new Ubicacion(rack2, sub, "R-2", "Rack 2", esDefault: false));
        db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), rack1, articuloId));
        db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), rack2, articuloId));
        await db.SaveChangesAsync();

        var cmd = Comando((articuloId, rack1), (articuloId, rack2));

        var resp = await NuevoHandler(db).Handle(cmd, CancellationToken.None);
        resp.Folio.Should().NotBeNullOrWhiteSpace();
    }

    // ─── Fakes ───────────────────────────────────────────────────────────────

    private sealed class FakeEmpresa(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeOc : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) =>
            Task.FromResult<OcLectura?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
            IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                new Dictionary<Guid, string>());
    }

    private sealed class FakeArticulos : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken ct) =>
            Task.FromResult<ArticuloLectura?>(
                new ArticuloLectura(articuloId, "ART", "Artículo", "PZA", null, null, true));
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(
                new Dictionary<Guid, ArticuloLectura>());
    }

    private sealed class FakeEvents : IIntegrationEventPublisher
    {
        public Task PublishAsync(object integrationEvent, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeUser : ICurrentUserContext
    {
        public Guid? UserId => EmpresaId; // cualquier guid no-nulo
        public string? UserName => "test";
    }

    private sealed class FakeDecimales : IDecimalesUnidadGuard
    {
        public Task ValidarAsync(IEnumerable<CantidadAValidar> cantidades, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
