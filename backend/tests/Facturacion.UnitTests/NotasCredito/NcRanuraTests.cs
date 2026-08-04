using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Facturas;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.NotasCredito;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;

namespace Millet.Facturacion.UnitTests.NotasCredito;

/// <summary>
/// RANURA-PR2 — NC automática de la "ranura" del pedido A+W: la factura va
/// por el total, la NC (motivo Ranura, relación 01) documenta el descuento
/// y la caja cobra <c>total − NC acreditadas</c> ([Decisión 13-K]).
/// </summary>
public sealed class NcRanuraTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

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

    private sealed class FakeFiscalUuidsUnicos : ICfdiTimbradoPort
    {
        private int _n;
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct)
        {
            _n++;
            return Task.FromResult(new TimbradoResultado(
                TimbradoEstado.Timbrado, $"00000000-0000-0000-0000-{_n:D12}", "S", "S", "0",
                Ahora, "SAT970701NN3", "<cfdi fake=\"true\" />", null, null));
        }
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) =>
            Task.FromResult(new CancelacionResultado(CancelacionEstado.Aceptada, "ok", null));
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            Task.FromResult(new EstatusCfdiResultado(true, "Vigente", true, null));
    }

    private static async Task<PedidoFacturable> SembrarPedidoAsync(
        FacturacionDbContext db, Guid empresaId, decimal? ranura)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(empresaId, "AW-1", Guid.NewGuid(), Guid.NewGuid(),
            "Cliente", (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15",
            ranura);
        pedido.AgregarLinea(null, "Producto", "01010101", "H87", 2m, 100m, 0m, false, tasaIva: 0.16m);
        pedido.RecalcularTotal();
        db.PedidosFacturables.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    private static EmitirFacturaVentaHandler EmitirHandler(FacturacionDbContext db, Guid empresaId) =>
        new(db, new FakeSenderFoliosUnicos(), new FakePeriodoContablePort(),
            new FakeCatalogosSatReadPort(), new FakeFiscalUuidsUnicos(), new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            new FakeIntegrationEventPublisher(), new FakeContabilidadAsientoPort(),
            new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

    private static EmitirFacturaVentaCommand Command(Guid? pedidoId) => new(
        Guid.NewGuid(), "AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX",
        "BBB010101BBB", "601", "PUE", "01", "MXN", null,
        (short)1, ComportamientoFiscal.MostradorInmediato, null, null, false,
        [new EmitirFacturaVentaLinea(null, "01010101", "P", "H87", 2m, 100m, 0m, "02", 0.16m, null, null)],
        Anticipos: null, Cce: null, AutorizacionId: null, PedidoFacturableId: pedidoId);

    [Fact]
    public async Task Emitir_desde_pedido_con_ranura_autogenera_NC_timbrada_con_relacion_01()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var pedido = await SembrarPedidoAsync(db, empresaId, ranura: 58m);

        var resp = await EmitirHandler(db, empresaId).Handle(Command(pedido.Id), CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        resp.NotaCreditoRanura.Should().NotBeNull();

        var factura = await db.FacturasVenta.SingleAsync();
        var nc = await db.NotasCredito.Include(n => n.Relaciones).SingleAsync();
        nc.Motivo.Should().Be(MotivoNotaCredito.Ranura);
        nc.Estado.Should().Be(EstadoTimbrado.Timbrado);
        nc.FacturaRelacionadaId.Should().Be(factura.Id);
        nc.Total.Should().Be(58m);
        // Ranura BRUTA desglosada con la tasa del documento (16%).
        nc.Subtotal.Should().Be(50m);
        nc.ImpuestosTrasladados.Should().Be(8m);
        nc.Descripcion.Should().Contain("AW-1");
        nc.Relaciones.Should().ContainSingle(r => r.UuidRelacionado == factura.Uuid && r.TipoRelacion == "01");

        // [Decisión 13-K]: la caja cobrará total − NC sin cambios en caja.
        var acreditado = await SaldoPorCobrar.AcreditadoAsync(db, factura.Id, CancellationToken.None);
        (factura.Total - acreditado).Should().Be(factura.Total - 58m);
    }

    [Fact]
    public async Task Emitir_desde_pedido_sin_ranura_no_genera_NC()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var pedido = await SembrarPedidoAsync(db, empresaId, ranura: null);

        var resp = await EmitirHandler(db, empresaId).Handle(Command(pedido.Id), CancellationToken.None);

        resp.NotaCreditoRanura.Should().BeNull();
        (await db.NotasCredito.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Emisor_es_idempotente_sobre_la_misma_factura()
    {
        // Cubre el reintento/pedimento diferido: si la NC de ranura vigente ya
        // existe, un segundo paso por el emisor no duplica.
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var pedido = await SembrarPedidoAsync(db, empresaId, ranura: 58m);
        await EmitirHandler(db, empresaId).Handle(Command(pedido.Id), CancellationToken.None);
        var factura = await db.FacturasVenta.Include(f => f.Lineas).SingleAsync();

        var segunda = await NcRanuraEmisor.EmitirSiAplicaAsync(
            db, new FakeSenderFoliosUnicos(), new FakeFiscalUuidsUnicos(), new FakeCfdiRepositorioPort(),
            new FakeIntegrationEventPublisher(), factura, pedido: null, usuarioEmisorId: null,
            Ahora, CancellationToken.None);

        segunda.Should().BeNull();
        (await db.NotasCredito.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Emisor_no_aplica_sobre_factura_no_timbrada()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 7,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);

        var r = await NcRanuraEmisor.EmitirSiAplicaAsync(
            db, new FakeSenderFoliosUnicos(), new FakeFiscalUuidsUnicos(), new FakeCfdiRepositorioPort(),
            new FakeIntegrationEventPublisher(), fv, pedido: null, usuarioEmisorId: null,
            Ahora, CancellationToken.None);

        r.Should().BeNull();
        (await db.NotasCredito.CountAsync()).Should().Be(0);
    }
}
