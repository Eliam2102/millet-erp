using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Almacen.Application.EventListeners;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.DevolucionesProveedor;

/// <summary>
/// La NC fiscal 03 (cuentas_por_pagar.nc-fiscal-devolucion.recibida.v1) concilia la devolución EXACTA
/// y publica almacen.oc_devolucion.conciliada.v1 con el acreditado acumulado por línea de OC.
/// </summary>
public class NcFiscalDevolucionRecibidaHandlerTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid OcId = Guid.NewGuid();
    private static readonly Guid LineaOcId = Guid.NewGuid();
    private static readonly Guid ArticuloId = Guid.NewGuid();

    [Fact]
    public async Task Concilia_la_devolucion_exacta_no_la_mas_antigua_y_publica_el_acreditado()
    {
        await using var db = NuevaDb();
        var antigua = await SembrarAsync(db, 3m, EstadoDevolucionProveedor.Registrada);
        var exacta = await SembrarAsync(db, 4m, EstadoDevolucionProveedor.Registrada);
        var ev = new CapturaEventos();

        await Handler(db, ev).Handle(Cmd(exacta.Id), default);

        db.ChangeTracker.Clear();
        (await db.DevolucionesProveedor.AsNoTracking().SingleAsync(d => d.Id == exacta.Id)).Estado
            .Should().Be(EstadoDevolucionProveedor.ConciliadaConNcFiscal);
        (await db.DevolucionesProveedor.AsNoTracking().SingleAsync(d => d.Id == antigua.Id)).Estado
            .Should().Be(EstadoDevolucionProveedor.Registrada);
        var pub = ev.Publicados.Should().ContainSingle().Which.Should().BeOfType<OcDevolucionConciliadaIntegrationEvent>().Subject;
        pub.EventType.Should().Be("almacen.oc_devolucion.conciliada.v1");
        pub.OrdenCompraId.Should().Be(OcId);
        pub.DevolucionId.Should().Be(exacta.Id);
        pub.LineasAcreditadasOc.Should().ContainSingle().Which.Should().Be(new LineaOcAcreditadaPayload(LineaOcId, 4m));
    }

    [Fact]
    public async Task Dos_devoluciones_conciliadas_de_la_misma_linea_acumulan()
    {
        await using var db = NuevaDb();
        var d1 = await SembrarAsync(db, 3m, EstadoDevolucionProveedor.Registrada);
        var d2 = await SembrarAsync(db, 4m, EstadoDevolucionProveedor.Registrada);
        var ev = new CapturaEventos();

        await Handler(db, ev).Handle(Cmd(d1.Id), default);
        await Handler(db, ev).Handle(Cmd(d2.Id), default);

        var ultimo = ev.Publicados.Last().Should().BeOfType<OcDevolucionConciliadaIntegrationEvent>().Subject;
        ultimo.LineasAcreditadasOc.Should().ContainSingle().Which.CantidadAcreditadaAcumulada.Should().Be(7m);
    }

    [Fact]
    public async Task Ya_conciliada_no_falla_y_vuelve_a_publicar()
    {
        await using var db = NuevaDb();
        var d = await SembrarAsync(db, 3m, EstadoDevolucionProveedor.ConciliadaConNcFiscal);
        var ev = new CapturaEventos();

        await Handler(db, ev).Handle(Cmd(d.Id), default);

        ev.Publicados.Should().ContainSingle().Which.Should().BeOfType<OcDevolucionConciliadaIntegrationEvent>()
            .Which.LineasAcreditadasOc.Single().CantidadAcreditadaAcumulada.Should().Be(3m);
    }

    [Fact]
    public async Task Registra_evento_procesado_para_dedupe()
    {
        await using var db = NuevaDb();
        var d = await SembrarAsync(db, 3m, EstadoDevolucionProveedor.Registrada);
        var eventId = Guid.NewGuid();

        await Handler(db, new CapturaEventos()).Handle(Cmd(d.Id, eventId), default);

        (await db.Set<Millet.Almacen.Domain.Idempotencia.EventoProcesado>().AnyAsync(e =>
            e.EventoId == eventId && e.EventoTipo == NcFiscalDevolucionRecibidaHandler.EventType)).Should().BeTrue();
    }

    [Fact]
    public async Task El_handler_viejo_de_NC_registrada_ya_no_concilia()
    {
        await using var db = NuevaDb();
        var d = await SembrarAsync(db, 3m, EstadoDevolucionProveedor.Registrada);
        var h = new NotaCreditoProveedorRegistradaHandler(db, NullLogger<NotaCreditoProveedorRegistradaHandler>.Instance);

        await h.Handle(new NotaCreditoProveedorRegistradaCommand(Guid.NewGuid(), new NotaCreditoProveedorRegistradaPayload(
            EmpresaId, DateTimeOffset.UtcNow, Guid.NewGuid(), d.ProveedorId, null, 3, 100m)), default);

        db.ChangeTracker.Clear();
        (await db.DevolucionesProveedor.AsNoTracking().SingleAsync(x => x.Id == d.Id)).Estado
            .Should().Be(EstadoDevolucionProveedor.Registrada);
    }

    private static NcFiscalDevolucionRecibidaCommand Cmd(Guid devId, Guid? eventId = null) =>
        new(eventId ?? Guid.NewGuid(), new NcFiscalDevolucionRecibidaPayload(
            EmpresaId, DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid(), devId, Guid.NewGuid(), 100m, "UUID"));

    private static NcFiscalDevolucionRecibidaHandler Handler(AlmacenDbContext db, IIntegrationEventPublisher ev) =>
        new(db, ev, new OcPort(), new P7Support.Conversion(), NullLogger<NcFiscalDevolucionRecibidaHandler>.Instance);

    private static async Task<DevolucionAProveedor> SembrarAsync(AlmacenDbContext db, decimal cantidad, EstadoDevolucionProveedor destino)
    {
        var dev = new DevolucionAProveedor(Guid.NewGuid(), EmpresaId, Guid.NewGuid(), "No conforme", Guid.NewGuid(),
            recepcionOrigenId: Guid.NewGuid(), ordenCompraOrigenId: OcId);
        var origen = new MovimientoInventario(dev.RecepcionOrigenId!.Value, TipoMovimiento.EntradaCompra, EmpresaId, new DateOnly(2027, 3, 1));
        var recibida = new LineaMovimiento(Guid.NewGuid(), origen.Id, 1, ArticuloId, 10, "PZA", 100,
            ubicacionId: Guid.NewGuid(), lineaOcId: LineaOcId);
        origen.AgregarLinea(recibida);
        db.Movimientos.Add(origen);
        dev.AgregarLinea(new LineaDevolucionProveedor(Guid.NewGuid(), dev.Id, 1, ArticuloId, cantidad, "PZA", 100m,
            lineaRecepcionOrigenId: recibida.Id));
        dev.AgregarEvidencia(new EvidenciaDevolucionProveedor(Guid.NewGuid(), dev.Id, "Foto", "e.jpg", "blob://e.jpg"));
        dev.SolicitarAutorizacion();
        dev.Autorizar(Guid.NewGuid());
        dev.MarcarRegistrada(Guid.NewGuid(), "SDP-1");
        if (destino == EstadoDevolucionProveedor.ConciliadaConNcFiscal) dev.ConciliarConNcFiscal(Guid.NewGuid());
        db.DevolucionesProveedor.Add(dev);
        await db.SaveChangesAsync();
        return dev;
    }

    private static AlmacenDbContext NuevaDb()
    {
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-ncfiscal-{Guid.NewGuid():N}").Options;
        var db = new AlmacenDbContext(opts, new FakeEmpresa());
        db.Database.EnsureCreated();
        return db;
    }

    private sealed class OcPort : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) =>
            Task.FromResult<OcLectura?>(new(ocId, "OC-1", EmpresaId, EmpresaId, "Autorizada",
                [new OcLineaLectura(LineaOcId, ArticuloId, "PZA", 100m, 10m, 10m)]));

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class CapturaEventos : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = new();
        public Task PublishAsync(object integrationEvent, CancellationToken ct) { Publicados.Add(integrationEvent); return Task.CompletedTask; }
    }

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }
}
