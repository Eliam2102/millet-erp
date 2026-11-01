using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.NotasCredito.EmitirNotaCreditoBonificacion;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.NotasCredito;

/// <summary>F6.3: la NC de bonificación con «Devolver dinero al cliente» pide el reembolso al timbrar.</summary>
public sealed class BonificacionReembolsoTests
{
    private const string Permiso = "facturacion.notas-credito.reembolso-solicitar";
    private const string ClabeValida = "032180000118359719";
    private static readonly DateTimeOffset Ahora = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private static DatosFiscalesReceptor Receptor => new("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
    private static DatosFiscalesEmisor Emisor => new("MIL010101AAA", "Millet", "601", "76120");

    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _cliente = Guid.NewGuid();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly FakeIntegrationEventPublisher _eventos = new();

    private FacturacionDbContext Db() => new(new DbContextOptionsBuilder<FacturacionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(_empresa));

    private EmitirNotaCreditoBonificacionHandler Handler(FacturacionDbContext db, params string[] permisos) =>
        new(db, new FakeSender(new ReservarFolioResponse("NC-000010", 10, "")), new FakePeriodoContablePort(),
            new FakeFiscalApiClient(), new FakeCfdiRepositorioPort(), _eventos, new FakeEmpresaContext(_empresa),
            new FakeUserContext(_usuario), new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db),
            new FakeCurrentUserPermissions(permisos));

    private async Task<FacturaVenta> FacturaAsync(FacturacionDbContext db, bool conCliente = true)
    {
        var sucursal = Guid.NewGuid();
        Guid? pedidoId = null;
        if (conCliente)
        {
            var pedido = PedidoFacturable.ImportarDesdeAw(_empresa, "AW-F63", sucursal, _cliente,
                "Cliente", 1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15", null);
            db.PedidosFacturables.Add(pedido);
            pedidoId = pedido.Id;
        }
        var fv = FacturaVenta.CrearBorrador(_empresa, "F-F63", 1, sucursal, null, null, Receptor, Emisor,
            "PUE", "03", "MXN", null, 2026, 10, 1, ComportamientoFiscal.MostradorInmediato, pedidoId, null, null, false);
        fv.AgregarLinea(null, "01010101", "Producto", "H87", 1m, 1000m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado("11111111-1111-1111-1111-111111111111", null, null, null, Ahora, null, null);
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();
        return fv;
    }

    private static ReembolsoClienteRequest Destino() => new("BBVA", ClabeValida, "Cliente SA");

    [Fact]
    public async Task CA6_17_bonificacion_con_devolucion_al_timbrar_pide_el_reembolso_con_la_cuenta_destino()
    {
        using var db = Db();
        var factura = await FacturaAsync(db);

        var resp = await Handler(db, Permiso).Handle(
            new EmitirNotaCreditoBonificacionCommand(factura.Id, 116m, 0.16m, "Bonificación", Destino()), default);

        resp.Estado.Should().Be("Timbrado");
        var evento = _eventos.Publicados.OfType<NotaCreditoReembolsoSolicitadoIntegrationEvent>().Single();
        evento.NotaCreditoId.Should().Be(resp.Id);
        evento.ClienteId.Should().Be(_cliente);
        evento.Importe.Should().Be(116m);
        evento.Uuid.Should().Be(resp.Uuid);
        evento.DestinoBanco.Should().Be("BBVA");
        evento.DestinoClabe.Should().Be(ClabeValida);
        evento.DestinoTitular.Should().Be("Cliente SA");
        evento.SolicitadoPor.Should().Be(_usuario);

        var nc = await db.NotasCredito.SingleAsync();
        nc.EstadoReembolso.Should().Be(EstadoReembolsoNotaCredito.Solicitado);
        nc.ImporteReembolsoSolicitado.Should().Be(116m);
    }

    [Fact]
    public async Task F63b_bonificacion_sin_devolucion_no_genera_solicitud()
    {
        using var db = Db();
        var factura = await FacturaAsync(db);

        await Handler(db, Permiso).Handle(new EmitirNotaCreditoBonificacionCommand(factura.Id, 116m, 0.16m, null), default);

        _eventos.Publicados.OfType<NotaCreditoReembolsoSolicitadoIntegrationEvent>().Should().BeEmpty();
        (await db.NotasCredito.SingleAsync()).EstadoReembolso.Should().BeNull();
    }

    [Fact]
    public async Task Sin_permiso_de_reembolso_no_emite_y_responde_prohibido()
    {
        using var db = Db();
        var factura = await FacturaAsync(db);

        var act = () => Handler(db).Handle(
            new EmitirNotaCreditoBonificacionCommand(factura.Id, 116m, 0.16m, null, Destino()), default);

        await act.Should().ThrowAsync<ForbiddenException>().Where(e => e.Code == "NC_REEMBOLSO_SIN_PERMISO");
        (await db.NotasCredito.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Sin_cliente_identificable_se_rechaza_antes_de_timbrar()
    {
        using var db = Db();
        var factura = await FacturaAsync(db, conCliente: false);

        var act = () => Handler(db, Permiso).Handle(
            new EmitirNotaCreditoBonificacionCommand(factura.Id, 116m, 0.16m, null, Destino()), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "NC_REEMBOLSO_SIN_CLIENTE");
        (await db.NotasCredito.AnyAsync()).Should().BeFalse("no se timbra una NC que no podría reembolsarse");
    }
}
