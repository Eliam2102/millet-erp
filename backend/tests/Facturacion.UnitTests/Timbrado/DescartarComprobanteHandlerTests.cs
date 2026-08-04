using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Timbrado.DescartarComprobante;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Timbrado;

/// <summary>
/// Tests del descarte de comprobantes fallidos ([Decisión 01-G] G3): FSM
/// TimbradoFallido → Descartada (terminal), liberación del pedido tomado
/// (G5) y write-back SinFacturar a la tabla-puente A+W.
/// </summary>
public sealed class DescartarComprobanteHandlerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static DescartarComprobanteHandler Handler(FacturacionDbContext db) =>
        new(db, new FakeClock(Ahora));

    private static FacturaVenta CrearFacturaFallida(
        FacturacionDbContext db, Guid empresaId, Guid? pedidoFacturableId = null)
    {
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresaId, "COTT-2026-000007", 7, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 7,
            (short)1, ComportamientoFiscal.MostradorInmediato, pedidoFacturableId, null, null, false);
        fv.AgregarLinea(null, "01010101", "Vidrio", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbradoFallido("CFDI40139", "rechazo CFDI40139");
        db.FacturasVenta.Add(fv);
        db.SaveChanges();
        return fv;
    }

    [Fact]
    public async Task Descartar_fallida_sin_pedido_queda_terminal_conservando_error()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = CrearFacturaFallida(db, empresaId);

        var r = await Handler(db).Handle(new DescartarComprobanteCommand(fv.Id), CancellationToken.None);

        r.Estado.Should().Be("Descartada");
        r.Folio.Should().Be("COTT-2026-000007"); // el folio queda quemado a conciencia
        r.PedidoLiberadoId.Should().BeNull();
        fv.Estado.Should().Be(EstadoTimbrado.Descartada);
        fv.TimbradoErrorCodigo.Should().Be("CFDI40139"); // trazabilidad
    }

    [Fact]
    public async Task Descartar_libera_el_pedido_tomado_por_la_factura()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var pedido = PedidoFacturable.CrearManual(
            empresaId, "P-1", Guid.NewGuid(), Guid.NewGuid(), "Cliente", 1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, Guid.NewGuid());
        db.PedidosFacturables.Add(pedido);
        db.SaveChanges();

        var fv = CrearFacturaFallida(db, empresaId, pedido.Id);
        pedido.MarcarFacturado(fv.Id); // G5: tomado desde el primer intento
        db.SaveChanges();

        var r = await Handler(db).Handle(new DescartarComprobanteCommand(fv.Id), CancellationToken.None);

        r.PedidoLiberadoId.Should().Be(pedido.Id);
        pedido.Estado.Should().Be(EstadoPedidoFacturable.Importado); // re-facturable
        pedido.ComprobanteVigenteId.Should().BeNull();
    }

    [Fact]
    public async Task Descartar_no_toca_un_pedido_tomado_por_otro_comprobante()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var pedido = PedidoFacturable.CrearManual(
            empresaId, "P-2", Guid.NewGuid(), Guid.NewGuid(), "Cliente", 1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, Guid.NewGuid());
        db.PedidosFacturables.Add(pedido);
        db.SaveChanges();

        var fallida = CrearFacturaFallida(db, empresaId, pedido.Id);
        var otroComprobanteId = Guid.NewGuid();
        pedido.MarcarFacturado(otroComprobanteId); // re-facturado por otra vía (pre-G5)
        db.SaveChanges();

        var r = await Handler(db).Handle(new DescartarComprobanteCommand(fallida.Id), CancellationToken.None);

        r.PedidoLiberadoId.Should().BeNull();
        pedido.Estado.Should().Be(EstadoPedidoFacturable.Facturado);
        pedido.ComprobanteVigenteId.Should().Be(otroComprobanteId);
    }

    [Fact]
    public async Task Descartar_pedido_aw_solicita_writeback_sinfacturar()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var pedido = PedidoFacturable.ImportarDesdeAw(
            empresaId, "AW-100", Guid.NewGuid(), Guid.NewGuid(), "Cliente", 1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        db.PedidosFacturables.Add(pedido);
        var control = IngestaControl.Crear(
            empresaId, OrigenPedido.Aw, "AW-100", "hash", 1, EstadoIngesta.Importado, pedido.Id, Ahora);
        db.IngestaControles.Add(control);
        db.SaveChanges();

        var fv = CrearFacturaFallida(db, empresaId, pedido.Id);
        pedido.MarcarFacturado(fv.Id);
        control.CambiarEstado(EstadoIngesta.Facturado);
        db.SaveChanges();

        await Handler(db).Handle(new DescartarComprobanteCommand(fv.Id), CancellationToken.None);

        pedido.Estado.Should().Be(EstadoPedidoFacturable.Importado);
        control.Estado.Should().Be(EstadoIngesta.Importado);
        control.WriteBackPendiente.Should().BeTrue();
        control.WriteBackEstado.Should().Be("SinFacturar");
        control.WriteBackUuid.Should().BeNull(); // nunca hubo CFDI
    }

    [Fact]
    public async Task Descartar_comprobante_no_fallido_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = CrearFacturaFallida(db, empresaId);
        fv.ReabrirParaReintentoTimbrado(); // Borrador — ya no es descartable

        var act = () => Handler(db).Handle(new DescartarComprobanteCommand(fv.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("COMPROBANTE_NO_DESCARTABLE");
    }

    [Fact]
    public async Task Descartar_comprobante_inexistente_es_404()
    {
        using var db = NewDb(Guid.NewGuid());

        var act = () => Handler(db).Handle(new DescartarComprobanteCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>();
    }

    [Fact]
    public async Task Descartada_no_admite_reintento_ni_segundo_descarte()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = CrearFacturaFallida(db, empresaId);
        await Handler(db).Handle(new DescartarComprobanteCommand(fv.Id), CancellationToken.None);

        var reintentar = fv.ReabrirParaReintentoTimbrado;
        reintentar.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("COMPROBANTE_NO_REINTENTABLE");

        var descartar = fv.Descartar;
        descartar.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("COMPROBANTE_NO_DESCARTABLE");
    }
}
