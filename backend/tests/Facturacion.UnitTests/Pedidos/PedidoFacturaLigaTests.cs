using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Pedidos;

/// <summary>B1/B2/B3/B5 — liga pedido ↔ factura (FE-F1).</summary>
public sealed class PedidoFacturaLigaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static async Task<PedidoFacturable> SembrarPedidoAsync(FacturacionDbContext db, Guid empresaId)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(empresaId, "AW-1", Guid.NewGuid(), Guid.NewGuid(),
            "Cliente", (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        pedido.AgregarLinea(null, "Producto", "01010101", "H87", 2m, 100m, 0m, false);
        pedido.RecalcularTotal();
        db.PedidosFacturables.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    private static EmitirFacturaVentaHandler EmitirHandler(FacturacionDbContext db, Guid empresaId) =>
        new(db, new FakeSender(new ReservarFolioResponse("F-1", 1, "")), new FakePeriodoContablePort(),
            new FakeCatalogosSatReadPort(), new FakeFiscalApiClient(), new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            new FakeIntegrationEventPublisher(), new FakeContabilidadAsientoPort(),
            new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

    private static EmitirFacturaVentaCommand Command(Guid? pedidoId) => new(
        Guid.NewGuid(), "AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX",
        "BBB010101BBB", "601", "PUE", "01", "MXN", null,
        (short)1, ComportamientoFiscal.MostradorInmediato, null, null, false,
        [new EmitirFacturaVentaLinea(null, "01010101", "P", "H87", 2m, 100m, 0m, "02", 0.16m, null, null)],
        Anticipos: null, Cce: null, AutorizacionId: null, PedidoFacturableId: pedidoId);

    // ---- B1: detalle ----

    [Fact]
    public async Task Detalle_devuelve_lineas_y_version()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var pedido = await SembrarPedidoAsync(db, empresaId);

        var detalle = await new PedidoFacturableDetalleHandler(db, new FakeClientesReadPort(), new FakeProductosReadPort(), new FakeCanalesVentaReadPort(), new FakeAlcanceCajaEvaluator())
            .Handle(new PedidoFacturableDetalleQuery(pedido.Id), CancellationToken.None);

        detalle.Estado.Should().Be("Importado");
        detalle.Origen.Should().Be("Aw");
        detalle.Lineas.Should().HaveCount(1);
        detalle.Version.Should().Be(pedido.Version);
    }

    // ---- B2/B3: emisión liga + transiciona ----

    [Fact]
    public async Task Emitir_desde_pedido_lo_marca_Facturado_y_liga_la_factura()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var pedido = await SembrarPedidoAsync(db, empresaId);

        var resp = await EmitirHandler(db, empresaId).Handle(Command(pedido.Id), CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        var factura = await db.FacturasVenta.SingleAsync();
        factura.PedidoFacturableId.Should().Be(pedido.Id);
        var pedidoBd = await db.PedidosFacturables.SingleAsync();
        pedidoBd.Estado.Should().Be(EstadoPedidoFacturable.Facturado);
        pedidoBd.ComprobanteVigenteId.Should().Be(factura.Id);
    }

    [Fact]
    public async Task Emitir_desde_pedido_ya_facturado_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var pedido = await SembrarPedidoAsync(db, empresaId);
        await EmitirHandler(db, empresaId).Handle(Command(pedido.Id), CancellationToken.None);

        var act = () => EmitirHandler(db, empresaId).Handle(Command(pedido.Id), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PEDIDO_NO_FACTURABLE");
    }

    [Fact]
    public async Task Emitir_con_pedido_inexistente_lanza_NotFound()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var act = () => EmitirHandler(db, empresaId).Handle(Command(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>().Where(e => e.Code == "PEDIDO_NO_ENCONTRADO");
    }

    [Fact]
    public async Task Cancelar_la_factura_devuelve_el_pedido_a_Importado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var pedido = await SembrarPedidoAsync(db, empresaId);
        var emit = await EmitirHandler(db, empresaId).Handle(Command(pedido.Id), CancellationToken.None);

        await new SolicitarCancelacionHandler(db, new FakeFiscalApiClient(), new FakeIntegrationEventPublisher(),
            new FakeEmpresaContext(empresaId), new FakeClock(Ahora))
            .Handle(new SolicitarCancelacionCommand(emit.Id, "02", null), CancellationToken.None);

        var pedidoBd = await db.PedidosFacturables.SingleAsync();
        pedidoBd.Estado.Should().Be(EstadoPedidoFacturable.Importado);
        pedidoBd.ComprobanteVigenteId.Should().BeNull();
    }

    // ---- B5: historial ----

    [Fact]
    public async Task Comprobantes_del_pedido_marca_el_vigente()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var pedido = await SembrarPedidoAsync(db, empresaId);
        var emit = await EmitirHandler(db, empresaId).Handle(Command(pedido.Id), CancellationToken.None);

        var items = await new PedidoComprobantesHandler(db)
            .Handle(new PedidoComprobantesQuery(pedido.Id), CancellationToken.None);

        items.Should().ContainSingle();
        items[0].Id.Should().Be(emit.Id);
        items[0].Vigente.Should().BeTrue();
    }
}
