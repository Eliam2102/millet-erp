using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.NotasCredito.SolicitarReembolso;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.NotasCredito;

public sealed class SolicitarReembolsoNotaCreditoHandlerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _cliente = Guid.NewGuid();
    private readonly Guid _sucursal = Guid.NewGuid();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly FakeIntegrationEventPublisher _eventos = new();
    private static DatosFiscalesReceptor Receptor => new("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
    private static DatosFiscalesEmisor Emisor => new("MIL010101AAA", "Millet", "601", "76120");

    private FacturacionDbContext Db() => new(new DbContextOptionsBuilder<FacturacionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(_empresa));

    private SolicitarReembolsoNotaCreditoHandler Handler(FacturacionDbContext db) =>
        new(db, _eventos, new FakeUserContext(_usuario), new FakeClock(Ahora), new FakeAlcanceCajaEvaluator());

    private NotaCredito SembrarNc(FacturacionDbContext db, bool conCliente)
    {
        Guid? pedidoId = null;
        if (conCliente)
        {
            var pedido = PedidoFacturable.ImportarDesdeAw(_empresa, "AW-R", _sucursal, _cliente,
                "Cliente", 1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15", null);
            db.PedidosFacturables.Add(pedido);
            pedidoId = pedido.Id;
        }
        var factura = FacturaVenta.CrearBorrador(_empresa, "F-R", 1, _sucursal, null, null, Receptor, Emisor,
            "PUE", "03", "MXN", null, 2026, 10, 1, ComportamientoFiscal.MostradorInmediato, pedidoId, null, null, false);
        db.FacturasVenta.Add(factura);
        var nc = NotaCredito.CrearBonificacion(_empresa, "NC-R", 2, _sucursal, null, null, Receptor, Emisor,
            "03", "MXN", null, 2026, 10, 1, factura.Id, 116m, 0.16m, "Bonificación");
        nc.MarcarTimbradoEnProceso();
        nc.MarcarTimbrado("44444444-4444-4444-4444-444444444444", null, null, null, Ahora, null, null);
        db.NotasCredito.Add(nc);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return nc;
    }

    [Fact]
    public async Task Publica_un_evento_con_los_datos_de_la_NC()
    {
        using var db = Db();
        var nc = SembrarNc(db, conCliente: true);

        var resp = await Handler(db).Handle(new SolicitarReembolsoNotaCreditoCommand(nc.Id, 100m, " Cliente pagó de más "), default);

        resp.Importe.Should().Be(100m);
        var ev = _eventos.Publicados.OfType<NotaCreditoReembolsoSolicitadoIntegrationEvent>().Should().ContainSingle().Subject;
        ev.NotaCreditoId.Should().Be(nc.Id);
        ev.Folio.Should().Be("NC-R");
        ev.ClienteId.Should().Be(_cliente);
        ev.Importe.Should().Be(100m);
        ev.TotalNotaCredito.Should().Be(116m);
        ev.Moneda.Should().Be("MXN");
        ev.SolicitadoPor.Should().Be(_usuario);
        ev.Motivo.Should().Be("Cliente pagó de más");
    }

    [Fact]
    public async Task Segunda_solicitud_lanza_YA_SOLICITADO_y_no_publica_otro()
    {
        using var db = Db();
        var nc = SembrarNc(db, conCliente: true);
        await Handler(db).Handle(new SolicitarReembolsoNotaCreditoCommand(nc.Id, 100m, "m"), default);

        var act = () => Handler(db).Handle(new SolicitarReembolsoNotaCreditoCommand(nc.Id, 100m, "m"), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "NC_REEMBOLSO_YA_SOLICITADO");
        _eventos.Publicados.Should().ContainSingle();
    }

    [Fact]
    public async Task NC_sin_cliente_resoluble_lanza_SIN_CLIENTE()
    {
        using var db = Db();
        var nc = SembrarNc(db, conCliente: false);

        var act = () => Handler(db).Handle(new SolicitarReembolsoNotaCreditoCommand(nc.Id, 100m, "m"), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "NC_REEMBOLSO_SIN_CLIENTE");
        _eventos.Publicados.Should().BeEmpty();
    }
}
