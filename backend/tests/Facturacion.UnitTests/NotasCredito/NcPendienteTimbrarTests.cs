using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Facturas;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Facturas.Queries;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.Timbrado.DescartarComprobante;
using Millet.Facturacion.Application.Timbrado.ReintentarTimbrado;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.NotasCredito;

/// <summary>
/// U-1 (86e3yxn2p): si la NC de ranura no timbra, la factura timbrada no se
/// pierde. La NC queda pendiente de timbrar ligada a la factura, con el motivo,
/// y se reintenta desde la bandeja. Datos FIX.
/// </summary>
public sealed class NcPendienteTimbrarTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

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

    /// <summary>PAC con guion: la llamada N falla si está en <see cref="Fallan"/>; el resto timbra con UUID único.</summary>
    internal sealed class FakeFiscalGuion(params int[] fallan) : ICfdiTimbradoPort
    {
        private int _n;
        public HashSet<int> Fallan { get; } = [.. fallan];
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct)
        {
            _n++;
            if (Fallan.Contains(_n))
                return Task.FromResult(new TimbradoResultado(
                    TimbradoEstado.Fallido, null, null, null, null, null, null, null, "PAC-99", "PAC caído (FIX)"));
            return Task.FromResult(new TimbradoResultado(
                TimbradoEstado.Timbrado, $"10000000-0000-0000-0000-{_n:D12}", "S", "S", "0",
                Ahora, "SAT970701NN3", "<cfdi fake=\"true\" />", null, null));
        }
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) =>
            Task.FromResult(new CancelacionResultado(CancelacionEstado.Aceptada, "ok", null));
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            Task.FromResult(new EstatusCfdiResultado(true, "Vigente", true, null));
    }

    /// <summary>Pedido A+W de $11,600 (2 × 5,000 + IVA) con ranura de $580.</summary>
    private static async Task<PedidoFacturable> SembrarPedidoAsync(FacturacionDbContext db, Guid empresaId)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(empresaId, "AW-FIX-1", Guid.NewGuid(), Guid.NewGuid(),
            "Cliente FIX", (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15", 580m);
        pedido.AgregarLinea(null, "Producto FIX", "01010101", "H87", 2m, 5_000m, 0m, false, tasaIva: 0.16m);
        pedido.RecalcularTotal();
        db.PedidosFacturables.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    private static EmitirFacturaVentaHandler EmitirHandler(FacturacionDbContext db, Guid empresaId, ICfdiTimbradoPort fiscal,
        FakeIntegrationEventPublisher eventos) =>
        new(db, new FakeSenderFoliosUnicos(), new FakePeriodoContablePort(),
            new FakeCatalogosSatReadPort(), fiscal, new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            eventos,
            new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db));

    private static ReintentarTimbradoHandler ReintentoHandler(FacturacionDbContext db, ICfdiTimbradoPort fiscal,
        FakeIntegrationEventPublisher eventos) =>
        new(db, new FakeSenderFoliosUnicos(), new FakePeriodoContablePort(true), fiscal, new FakeCfdiRepositorioPort(), eventos,
            new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db));

    private static EmitirFacturaVentaCommand Command(Guid pedidoId) => new(
        Guid.NewGuid(), "AAA010101AAA", "Cliente FIX", "601", "97000", "G03", "MEX",
        "BBB010101BBB", "601", "PUE", "01", "MXN", null,
        (short)1, ComportamientoFiscal.MostradorInmediato, null, null, false,
        [new EmitirFacturaVentaLinea(null, "01010101", "Producto FIX", "H87", 2m, 5_000m, 0m, "02", 0.16m, null, null)],
        Anticipos: null, Cce: null, AutorizacionId: null, PedidoFacturableId: pedidoId);

    private static async Task<(FacturacionDbContext Db, PedidoFacturable Pedido, EmitirFacturaVentaResponse Resp,
        FakeFiscalGuion Fiscal, FakeIntegrationEventPublisher Eventos, Guid EmpresaId)> EmitirConNcFallidaAsync()
    {
        var empresaId = Guid.NewGuid();
        var db = NewDb(empresaId);
        var pedido = await SembrarPedidoAsync(db, empresaId);
        var fiscal = new FakeFiscalGuion(2); // 1 = factura timbra; 2 = la NC de ranura falla
        var eventos = new FakeIntegrationEventPublisher();
        var resp = await EmitirHandler(db, empresaId, fiscal, eventos).Handle(Command(pedido.Id), CancellationToken.None);
        return (db, pedido, resp, fiscal, eventos, empresaId);
    }

    [Fact]
    public async Task Criterio1_si_el_PAC_rechaza_la_NC_la_factura_queda_timbrada_y_la_NC_pendiente_con_motivo()
    {
        var (db, _, resp, _, eventos, _) = await EmitirConNcFallidaAsync();
        using var _db = db;

        resp.Estado.Should().Be("Timbrado");
        resp.Uuid.Should().NotBeNullOrWhiteSpace();
        resp.Total.Should().Be(11_600m);
        resp.NotaCreditoRanura!.Estado.Should().Be("TimbradoFallido");
        resp.NotaCreditoRanura.TimbradoErrorCodigo.Should().Be("PAC-99");

        var factura = await db.FacturasVenta.AsNoTracking().SingleAsync();
        (factura.Estado, factura.Uuid).Should().Be((EstadoTimbrado.Timbrado, resp.Uuid));

        var nc = await db.NotasCredito.AsNoTracking().SingleAsync();
        nc.Motivo.Should().Be(MotivoNotaCredito.Ranura);
        nc.Estado.Should().Be(EstadoTimbrado.TimbradoFallido);
        nc.FacturaRelacionadaId.Should().Be(factura.Id);
        nc.Total.Should().Be(580m);
        nc.TimbradoErrorMensaje.Should().Be("PAC caído (FIX)");

        // La póliza de la factura nace; la de la NC todavía no.
        eventos.Publicados.Should().ContainSingle(e => e is FacturaVentaTimbradaIntegrationEvent);
        eventos.Publicados.Should().NotContain(e => e is NotaCreditoTimbradaIntegrationEvent);
    }

    [Fact]
    public async Task Criterio2_al_reintentar_la_NC_queda_timbrada_relacionada_con_la_factura_y_nace_su_poliza()
    {
        var (db, _, resp, fiscal, eventos, _) = await EmitirConNcFallidaAsync();
        using var _db = db;
        var ncId = resp.NotaCreditoRanura!.Id;

        var reintento = await ReintentoHandler(db, fiscal, eventos).Handle(new ReintentarTimbradoCommand(ncId), CancellationToken.None);

        reintento.Estado.Should().Be("Timbrado");
        var nc = await db.NotasCredito.Include(n => n.Relaciones).SingleAsync(n => n.Id == ncId);
        nc.Relaciones.Should().ContainSingle(r => r.TipoRelacion == "01" && r.UuidRelacionado == resp.Uuid);
        eventos.Publicados.OfType<NotaCreditoTimbradaIntegrationEvent>().Should().ContainSingle(e => e.NotaCreditoId == ncId);

        var detalle = await new ComprobanteDetalleHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new ComprobanteDetalleQuery(resp.Id), CancellationToken.None);
        detalle.NotasCreditoPendientes.Should().BeEmpty();
        detalle.TotalAcreditado.Should().Be(580m);
    }

    [Fact]
    public async Task Criterio4_con_la_NC_pendiente_el_por_cobrar_es_la_factura_completa_y_hay_aviso()
    {
        var (db, _, resp, _, _, _) = await EmitirConNcFallidaAsync();
        using var _db = db;

        var detalle = await new ComprobanteDetalleHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new ComprobanteDetalleQuery(resp.Id), CancellationToken.None);
        detalle.TotalAcreditado.Should().Be(0m);
        detalle.TotalPorCobrar.Should().Be(11_600m);
        detalle.NotasCreditoPendientes.Should().ContainSingle()
            .Which.Should().Match<NotaCreditoPendienteDetalle>(n => n.Motivo == "Ranura" && n.TimbradoErrorCodigo == "PAC-99");
        (await SaldoPorCobrar.AcreditadoAsync(db, resp.Id, CancellationToken.None)).Should().Be(0m);

        var bandeja = await new BandejaFacturasHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new BandejaFacturasQuery(null, 0, 50, SoloNcPendiente: true), CancellationToken.None);
        bandeja.Items.Should().ContainSingle(i => i.Id == resp.Id && i.NotaCreditoPendiente);
    }

    [Fact]
    public async Task Criterio5_con_la_NC_pendiente_emitir_otra_vez_la_misma_factura_se_rechaza()
    {
        var (db, pedido, _, fiscal, eventos, empresaId) = await EmitirConNcFallidaAsync();
        using var _db = db;

        var act = () => EmitirHandler(db, empresaId, fiscal, eventos).Handle(Command(pedido.Id), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PEDIDO_NO_FACTURABLE");
        (await db.FacturasVenta.CountAsync()).Should().Be(1);
        (await db.NotasCredito.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Reintentar_la_factura_no_duplica_la_NC_pendiente()
    {
        var (db, _, resp, fiscal, eventos, _) = await EmitirConNcFallidaAsync();
        using var _db = db;

        // La NC pendiente cuenta como emitida: el emisor de ranura no crea otra.
        var factura = await db.FacturasVenta.Include(f => f.Lineas).SingleAsync();
        var otra = await Millet.Facturacion.Application.NotasCredito.NcRanuraEmisor.EmitirSiAplicaAsync(
            db, new FakeSenderFoliosUnicos(), fiscal, new FakeCfdiRepositorioPort(), eventos, factura, null, null, Ahora,
            ReceptorFiscalTestFactory.Crear(db), CancellationToken.None);
        otra.Should().BeNull();
        resp.NotaCreditoRanura.Should().NotBeNull();
    }

    [Fact]
    public async Task Descartar_la_NC_pendiente_quita_el_aviso_de_la_factura()
    {
        var (db, _, resp, _, _, _) = await EmitirConNcFallidaAsync();
        using var _db = db;

        await new DescartarComprobanteHandler(db, new FakeClock(Ahora))
            .Handle(new DescartarComprobanteCommand(resp.NotaCreditoRanura!.Id), CancellationToken.None);

        var detalle = await new ComprobanteDetalleHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new ComprobanteDetalleQuery(resp.Id), CancellationToken.None);
        detalle.NotasCreditoPendientes.Should().BeEmpty();
    }
}
