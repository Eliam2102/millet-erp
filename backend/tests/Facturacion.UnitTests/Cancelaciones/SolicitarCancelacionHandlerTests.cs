using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;
using Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cancelaciones;

public sealed class SolicitarCancelacionHandlerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
    private const string Rfc = "AAA010101AAA";

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private sealed class FakeFiscalRechaza : ICfdiTimbradoPort
    {
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) =>
            Task.FromResult(new CancelacionResultado(CancelacionEstado.Rechazada, "Rechazada", "El receptor rechazó."));
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            Task.FromResult(new EstatusCfdiResultado(true, "Vigente", true, null));
    }

    private sealed class FakeSenderFoliosUnicos : ISender
    {
        private int _n;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            if (request is ReservarFolioCommand)
            {
                _n++;
                return Task.FromResult((TResponse)(object)new ReservarFolioResponse($"DOC-{_n:D6}", _n, ""));
            }
            throw new NotImplementedException();
        }
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeFiscalUuidsUnicos : ICfdiTimbradoPort
    {
        private int _n;
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct)
        {
            _n++;
            return Task.FromResult(new TimbradoResultado(
                TimbradoEstado.Timbrado, $"00000000-0000-0000-0000-{_n:D12}", "S", "S", "0", Ahora, "SAT970701NN3",
                $"<cfdi serie=\"{emision.Serie}\" folio=\"{emision.Folio}\" fake=\"true\" />", null, null));
        }
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) =>
            Task.FromResult(new CancelacionResultado(CancelacionEstado.Aceptada, "Cancelado", null));
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            Task.FromResult(new EstatusCfdiResultado(true, "Vigente", true, null));
    }

    private static SolicitarCancelacionHandler Handler(FacturacionDbContext db, Guid empresaId, ICfdiTimbradoPort? fiscal = null) =>
        new(db, fiscal ?? new FakeFiscalApiClient(), new FakeIntegrationEventPublisher(), new FakeEmpresaContext(empresaId), new FakeClock(Ahora));

    private static async Task<(FacturaVenta, PedidoFacturable)> SembrarFacturaConPedidoAsync(FacturacionDbContext db, Guid empresaId)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(empresaId, "AW-1", Guid.NewGuid(), Guid.NewGuid(),
            "Cliente", (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        var receptor = new DatosFiscalesReceptor(Rfc, "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "03", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, pedido.Id, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado("11111111-1111-1111-1111-111111111111", null, null, null, Ahora, null, null);
        pedido.MarcarFacturado(fv.Id);
        db.PedidosFacturables.Add(pedido);
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();
        return (fv, pedido);
    }

    [Fact]
    public async Task Cancelar_factura_vigente_la_cancela_y_libera_el_pedido()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (fv, pedido) = await SembrarFacturaConPedidoAsync(db, empresaId);

        var resp = await Handler(db, empresaId).Handle(
            new SolicitarCancelacionCommand(fv.Id, "02", null), CancellationToken.None);

        resp.EstadoComprobante.Should().Be("Cancelado");
        resp.EstadoSolicitud.Should().Be("Aceptada");
        (await db.FacturasVenta.SingleAsync()).Estado.Should().Be(EstadoTimbrado.Cancelado);
        (await db.PedidosFacturables.SingleAsync()).Estado.Should().Be(EstadoPedidoFacturable.Importado);
        await db.SolicitudesCancelacion.SingleAsync();
    }

    [Fact]
    public async Task Cancelar_no_timbrado_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var receptor = new DatosFiscalesReceptor(Rfc, "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "03", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();

        var act = () => Handler(db, empresaId).Handle(new SolicitarCancelacionCommand(fv.Id, "02", null), CancellationToken.None);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "COMPROBANTE_NO_CANCELABLE");
    }

    [Fact]
    public async Task Cancelar_inexistente_lanza_NotFound()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var act = () => Handler(db, empresaId).Handle(new SolicitarCancelacionCommand(Guid.NewGuid(), "02", null), CancellationToken.None);
        await act.Should().ThrowAsync<EntityNotFoundException>().Where(e => e.Code == "COMPROBANTE_NO_ENCONTRADO");
    }

    [Fact]
    public async Task Si_el_PAC_rechaza_el_comprobante_vuelve_a_Timbrado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (fv, _) = await SembrarFacturaConPedidoAsync(db, empresaId);

        var resp = await Handler(db, empresaId, new FakeFiscalRechaza()).Handle(
            new SolicitarCancelacionCommand(fv.Id, "02", null), CancellationToken.None);

        resp.EstadoComprobante.Should().Be("Timbrado");
        resp.EstadoSolicitud.Should().Be("Rechazada");
        (await db.FacturasVenta.SingleAsync()).Estado.Should().Be(EstadoTimbrado.Timbrado);
        (await db.PedidosFacturables.SingleAsync()).Estado.Should().Be(EstadoPedidoFacturable.Facturado);
    }

    [Fact]
    public async Task Cancelar_motivo_01_sin_sustituto_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (fv, _) = await SembrarFacturaConPedidoAsync(db, empresaId);

        var act = () => Handler(db, empresaId).Handle(new SolicitarCancelacionCommand(fv.Id, "01", null), CancellationToken.None);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CANCELACION_SUSTITUTO_REQUERIDO");
    }

    [Fact]
    public async Task Cancelar_anticipo_con_NC_de_amortizacion_vigente_es_bloqueado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var fiscal = new FakeFiscalUuidsUnicos();

        // Anticipo emitido y luego amortizado contra una factura final (deja NC vigente).
        var empresasFiscal = new FakeEmpresaFiscalReadPort(
            new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120"));
        var emitAnticipoHandler = new EmitirFacturaAnticipoHandler(db, sender, new FakePeriodoContablePort(),
            new FakeCatalogosSatReadPort(), fiscal, new FakeCfdiRepositorioPort(), empresasFiscal,
            new FakeIntegrationEventPublisher(),
            new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));
        var anticipo = await emitAnticipoHandler.Handle(new EmitirFacturaAnticipoCommand(
            Guid.NewGuid(), Guid.NewGuid(), Rfc, "Cliente", "601", "97000", "G03", "MEX",
            "BBB010101BBB", "601", "PUE", "03", "MXN", null, TipoAnticipo.ClientesMxp, 1000m, 0.16m, null, null, "AW-1", null, null),
            CancellationToken.None);

        var emitFacturaHandler = new EmitirFacturaVentaHandler(db, sender, new FakePeriodoContablePort(),
            new FakeCatalogosSatReadPort(), fiscal, new FakeCfdiRepositorioPort(), empresasFiscal,
            new FakeIntegrationEventPublisher(), new FakeContabilidadAsientoPort(),
            new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));
        await emitFacturaHandler.Handle(new EmitirFacturaVentaCommand(
            Guid.NewGuid(), Rfc, "Cliente", "601", "97000", "G03", "MEX", "BBB010101BBB", "601",
            "PUE", "03", "MXN", null, (short)7, ComportamientoFiscal.ConAnticipo, null, null, false,
            [new EmitirFacturaVentaLinea(null, "01010101", "Maquila", "E48", 1m, 5000m, 0m, "02", 0.16m, null, null)],
            [new AnticipoAAmortizar(anticipo.AnticipoId, 500m)]), CancellationToken.None);

        // Cancelar el CFDI del anticipo debe bloquearse: hay NC de amortización vigente.
        var act = () => Handler(db, empresaId).Handle(
            new SolicitarCancelacionCommand(anticipo.FacturaAnticipoId, "02", null), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_AMORTIZADO_NCS_VIGENTES");
    }
}
