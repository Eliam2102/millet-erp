using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>Recepción de activos sin bin (folio ENT) y su anulación mientras no estén capitalizados.</summary>
public class AnularRecepcionActivoTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid Oc = Guid.NewGuid(), Articulo = Guid.NewGuid(), Linea = Guid.NewGuid();

    [Fact]
    public async Task Recepcion_solo_de_activos_toma_folio_ENT_consecutivo_y_lo_guarda()
    {
        await using var db = NuevaDb();
        var eventos = new FakeEvents();
        var primera = await Recibir(db, eventos);
        var segunda = await Recibir(db, eventos);
        primera.Folio.Should().Be("M-ENT2026-000001");
        segunda.Folio.Should().Be("M-ENT2026-000002");
        (await db.RecepcionesActivos.SingleAsync(x => x.RecepcionId == segunda.RecepcionId)).Folio.Should().Be(segunda.Folio);
        eventos.Publicados.OfType<OcRecepcionRegistradaIntegrationEvent>().Select(x => x.FolioRecepcion).Should().Equal(primera.Folio, segunda.Folio);
    }

    [Fact]
    public async Task Anular_marca_la_fila_publica_el_evento_y_deja_de_contar_como_recibido()
    {
        await using var db = NuevaDb();
        var eventos = new FakeEvents();
        var primera = await Recibir(db, eventos);
        await Recibir(db, eventos);

        var respuesta = await Anulador(db, eventos, new EstadoActivosRecepcion(0, 0)).Handle(new AnularRecepcionActivoCommand(Oc, primera.RecepcionId, " Llegó otro equipo "), default);

        respuesta.Should().Be(new AnularRecepcionActivoResponse(primera.RecepcionId, primera.Folio, 1, false));
        var fila = await db.RecepcionesActivos.SingleAsync(x => x.RecepcionId == primera.RecepcionId);
        fila.EstaAnulada.Should().BeTrue(); fila.MotivoAnulacion.Should().Be("Llegó otro equipo"); fila.AnuladaPorId.Should().NotBeNull();
        var anulada = eventos.Publicados.OfType<RecepcionActivoAnuladaIntegrationEvent>().Single();
        (anulada.OrdenCompraId, anulada.LineaOcId, anulada.Cantidad, anulada.CantidadRecibidaAcumulada).Should().Be((Oc, Linea, 1m, 1m));
        (await RecibidoPorLineaOc.CalcularAsync(db, new P7Support.Conversion(), OcActivo(), default))[Linea].Should().Be(1m);
    }

    [Fact]
    public async Task Anular_dos_veces_no_hace_nada_la_segunda()
    {
        await using var db = NuevaDb();
        var eventos = new FakeEvents();
        var recepcion = await Recibir(db, eventos);
        var handler = Anulador(db, eventos, new EstadoActivosRecepcion(0, 0));
        await handler.Handle(new AnularRecepcionActivoCommand(Oc, recepcion.RecepcionId, "Error de captura"), default);

        var segunda = await handler.Handle(new AnularRecepcionActivoCommand(Oc, recepcion.RecepcionId, "Error de captura"), default);

        segunda.YaAnulada.Should().BeTrue();
        eventos.Publicados.OfType<RecepcionActivoAnuladaIntegrationEvent>().Should().HaveCount(1);
    }

    [Theory]
    [InlineData(1, 0, "RECEPCION_ACTIVO_CAPITALIZADO")]
    [InlineData(0, 1, "RECEPCION_ACTIVO_FACTURADO")]
    public async Task Anular_se_rechaza_si_ya_se_capitalizo_o_se_facturo(int capitalizados, int facturados, string codigo)
    {
        await using var db = NuevaDb();
        var eventos = new FakeEvents();
        var recepcion = await Recibir(db, eventos);

        var act = () => Anulador(db, eventos, new EstadoActivosRecepcion(capitalizados, facturados)).Handle(new AnularRecepcionActivoCommand(Oc, recepcion.RecepcionId, "Motivo"), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(codigo);
        (await db.RecepcionesActivos.SingleAsync()).EstaAnulada.Should().BeFalse();
        eventos.Publicados.OfType<RecepcionActivoAnuladaIntegrationEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Anular_exige_motivo()
        => new AnularRecepcionActivoValidator().Validate(new AnularRecepcionActivoCommand(Oc, Guid.NewGuid(), " ")).IsValid.Should().BeFalse();

    private static OcLectura OcActivo() => new(Oc, "OC-MID2026-000100", Guid.NewGuid(), EmpresaId, "Autorizada",
        [new(Linea, Articulo, "PZA", 3, 0, 500000, EsActivoFijo: true, ClaseActivoId: Guid.NewGuid())]);

    private static Task<RegistrarRecepcionResponse> Recibir(AlmacenDbContext db, FakeEvents eventos)
        => new RegistrarRecepcionConPackingListHandler(db, new FakeOc(), eventos, new FakeUser(), new FakeEmpresa(), new FakeDecimales(),
            new PeriodoContableStub(abierto: true), new P7Support.Conversion())
            .Handle(new RegistrarRecepcionConPackingListCommand(Oc, new DateOnly(2026, 10, 8), "packing.pdf", null, [new(Articulo, Linea, 1, null, null, null)]), default);

    private static AnularRecepcionActivoHandler Anulador(AlmacenDbContext db, FakeEvents eventos, EstadoActivosRecepcion estado)
        => new(db, new FakeActivos(estado), eventos, new FakeUser());

    private static AlmacenDbContext NuevaDb()
    {
        var db = new AlmacenDbContext(new DbContextOptionsBuilder<AlmacenDbContext>().UseInMemoryDatabase($"almacen-anula-af-{Guid.NewGuid():N}").Options, new FakeEmpresa());
        db.Database.EnsureCreated();
        return db;
    }

    private sealed class FakeOc : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<OcLectura?>(OcActivo());
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string> { [Oc] = "OC-MID2026-000100" });
    }
    private sealed class FakeActivos(EstadoActivosRecepcion estado) : IActivoRecepcionReadPort
    {
        public Task<EstadoActivosRecepcion> ConsultarAsync(Guid empresaId, Guid recepcionId, Guid lineaOcId, CancellationToken ct) => Task.FromResult(estado);
    }
    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
    private sealed class FakeEvents : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct) { Publicados.Add(integrationEvent); return Task.CompletedTask; }
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
