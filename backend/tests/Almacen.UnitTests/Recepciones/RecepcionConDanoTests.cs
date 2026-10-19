using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>ALM-04: lo dañado de una recepción entra a MAT-REV en un segundo movimiento; un solo evento con el total.</summary>
public class RecepcionConDanoTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid SucursalId = Guid.NewGuid();
    private static readonly Guid AlmacenId = Guid.NewGuid();
    private static readonly Guid SubId = Guid.NewGuid();
    private static readonly Guid SubRevId = Guid.NewGuid();
    private static readonly Guid BinId = Guid.NewGuid();
    private static readonly Guid RevBinId = Guid.NewGuid();
    private static readonly Guid ArticuloId = Guid.NewGuid();

    private static async Task<InMemoryAlmacenDbContext> NuevaDbAsync(bool conRackRevision = true)
    {
        var opts = new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-dano-{Guid.NewGuid():N}").Options;
        var db = new InMemoryAlmacenDbContext(opts, new FakeEmpresa());
        await db.Database.EnsureCreatedAsync();
        db.Almacenes.Add(new Almacen.Domain.Catalogo.Almacen(AlmacenId, "ALM", "Almacén", SucursalId));
        db.SubAlmacenes.Add(new SubAlmacen(SubId, AlmacenId, "SUB", "Sub", TipoSubAlmacen.Insumos));
        db.SubAlmacenes.Add(new SubAlmacen(SubRevId, AlmacenId, "MAT-REV", "Material en revisión", TipoSubAlmacen.MaterialEnRevision));
        db.Ubicaciones.Add(new Ubicacion(BinId, SubId, "R-1", "Rack 1"));
        db.Ubicaciones.Add(new Ubicacion(Guid.NewGuid(), SubRevId, "UNICA", "Única", esDefault: true));
        if (conRackRevision) db.Ubicaciones.Add(new Ubicacion(RevBinId, SubRevId, "REV-01", "Revisión 1"));
        db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), BinId, ArticuloId));
        await db.SaveChangesAsync();
        return db;
    }

    private static RegistrarRecepcionConFacturaHandler HandlerA(AlmacenDbContext db, CapturaEventos events) =>
        new(db, new FakeOc(), new FakeArticulos(), events, new FakeUser(), new FakeEmpresa(), new FakeDecimales(),
            new PeriodoContableStub(true), new P7Support.Conversion(), CfdiFake.Valido(), new ProveedorRfcFake());

    private static RegistrarRecepcionConPackingListHandler HandlerB(AlmacenDbContext db, CapturaEventos events) =>
        new(db, new FakeOc(), new FakeArticulos(), events, new FakeUser(), new FakeEmpresa(), new FakeDecimales(),
            new PeriodoContableStub(true), new P7Support.Conversion());

    private static RegistrarRecepcionConFacturaCommand CmdA(decimal danada) => new(Guid.NewGuid(), new DateOnly(2026, 10, 17),
        Guid.NewGuid(), null, null,
        [new RegistrarRecepcionLineaInput(ArticuloId, ArticuloId, 10m, null, null, BinId, CantidadDanada: danada, MotivoDano: danada > 0 ? "Hojas rotas" : null)]);

    [Fact]
    public async Task Variante_A_con_dano_crea_dos_movimientos_y_un_evento_con_el_total()
    {
        await using var db = await NuevaDbAsync();
        var events = new CapturaEventos();

        var resp = await HandlerA(db, events).Handle(CmdA(2m), default);

        var movs = await db.Movimientos.Include(m => m.Lineas).ToListAsync();
        movs.Should().HaveCount(2);
        var principal = movs.Single(m => m.Id == resp.RecepcionId);
        principal.Lineas.Single().Cantidad.Should().Be(8m);
        principal.Lineas.Single().UbicacionId.Should().Be(BinId);
        var rev = movs.Single(m => m.Id != resp.RecepcionId);
        rev.RecepcionOrigenId.Should().Be(principal.Id);
        rev.Lineas.Single().Cantidad.Should().Be(2m);
        rev.Lineas.Single().UbicacionId.Should().Be(RevBinId);
        rev.Lineas.Single().ComentarioLinea.Should().Be("Hojas rotas");
        rev.Folio.Should().NotBe(principal.Folio);

        var e = events.Unico<OcRecepcionRegistradaIntegrationEvent>();
        e.RecepcionId.Should().Be(principal.Id);
        e.Lineas.Should().HaveCount(2);
        e.Lineas.Sum(l => l.Cantidad).Should().Be(10m);
        e.Lineas.Should().OnlyContain(l => l.CantidadRecibidaAcumulada == 10m);
        e.Lineas.Single(l => l.SubAlmacenId == SubRevId).UbicacionId.Should().Be(RevBinId);
    }

    [Fact]
    public async Task Sin_dano_crea_un_solo_movimiento()
    {
        await using var db = await NuevaDbAsync();
        var events = new CapturaEventos();

        await HandlerA(db, events).Handle(CmdA(0m), default);

        (await db.Movimientos.ToListAsync()).Should().ContainSingle().Which.RecepcionOrigenId.Should().BeNull();
        events.Unico<OcRecepcionRegistradaIntegrationEvent>().Lineas.Should().ContainSingle();
    }

    [Fact]
    public async Task Todo_danado_deja_solo_el_movimiento_de_revision_como_principal()
    {
        await using var db = await NuevaDbAsync();
        var events = new CapturaEventos();

        var resp = await HandlerA(db, events).Handle(CmdA(10m), default);

        var mov = (await db.Movimientos.Include(m => m.Lineas).ToListAsync()).Should().ContainSingle().Subject;
        mov.Id.Should().Be(resp.RecepcionId);
        mov.RecepcionOrigenId.Should().BeNull();
        mov.Lineas.Single().UbicacionId.Should().Be(RevBinId);
        events.Unico<OcRecepcionRegistradaIntegrationEvent>().RecepcionId.Should().Be(resp.RecepcionId);
    }

    [Fact]
    public async Task Dano_sin_rack_de_revision_falla_y_no_registra_nada()
    {
        await using var db = await NuevaDbAsync(conRackRevision: false);

        var act = () => HandlerA(db, new CapturaEventos()).Handle(CmdA(2m), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("RECEPCION_SIN_UBICACION_REVISION");
        (await db.Movimientos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Variante_B_con_dano_vincula_ambos_con_el_packing_list_y_mantiene_factura_pendiente()
    {
        await using var db = await NuevaDbAsync();
        var events = new CapturaEventos();

        await HandlerB(db, events).Handle(new RegistrarRecepcionConPackingListCommand(Guid.NewGuid(), new DateOnly(2026, 10, 17),
            "blob://pl", null,
            [new RegistrarRecepcionLineaInput(ArticuloId, ArticuloId, 10m, null, null, BinId, CantidadDanada: 2m, MotivoDano: "Hojas rotas")]), default);

        var movs = await db.Movimientos.ToListAsync();
        movs.Should().HaveCount(2);
        movs.Should().OnlyContain(m => m.PackingListBlobRef == "blob://pl");
        var e = events.Unico<OcRecepcionRegistradaIntegrationEvent>();
        e.FacturaPendiente.Should().BeTrue();
        e.Lineas.Should().HaveCount(2);
        e.Lineas.Sum(l => l.Cantidad).Should().Be(10m);
    }

    private sealed class CapturaEventos : IIntegrationEventPublisher
    {
        public List<object> Eventos { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct)
        {
            Eventos.Add(integrationEvent);
            return Task.CompletedTask;
        }
        public T Unico<T>() => Eventos.OfType<T>().Single();
    }

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeOc : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) => Task.FromResult<OcLectura?>(new(ocId, "OC-P1", EmpresaId, EmpresaId, "Autorizada", [new(ArticuloId, ArticuloId, "PZA", 100, 0, 10)]));
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
            IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class FakeArticulos : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken ct) =>
            Task.FromResult<ArticuloLectura?>(new ArticuloLectura(articuloId, "ART", "Artículo", "PZA", null, null, true));
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(new Dictionary<Guid, ArticuloLectura>());
    }

    private sealed class FakeUser : ICurrentUserContext
    {
        public Guid? UserId => EmpresaId;
        public string? UserName => "test";
    }

    private sealed class FakeDecimales : IDecimalesUnidadGuard
    {
        public Task ValidarAsync(IEnumerable<CantidadAValidar> cantidades, CancellationToken ct) => Task.CompletedTask;
    }
}
