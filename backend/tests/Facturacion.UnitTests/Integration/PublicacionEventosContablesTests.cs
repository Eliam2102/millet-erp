using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;
using Millet.Facturacion.Application.Facturas.AplicarPedimento;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.NotasCredito.EmitirNotaCreditoBonificacion;
using Millet.Facturacion.Application.Timbrado.ReintentarTimbrado;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Integration;

/// <summary>U1.6: verifica los eventos capturados al ejecutar los handlers, no solo el mapper.</summary>
public sealed class PublicacionEventosContablesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 1, 3, 30, 0, TimeSpan.Zero);
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _cliente = Guid.NewGuid();
    private readonly Guid _sucursal = Guid.NewGuid();
    private readonly Guid _producto = Guid.NewGuid();
    private readonly FakeIntegrationEventPublisher _eventos = new();
    private readonly FakeFiscalApiClient _pac = new();
    private static DatosFiscalesReceptor Receptor => new("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
    private static DatosFiscalesEmisor Emisor => new("MIL010101AAA", "Millet", "601", "76120");

    private FacturacionDbContext Db() => new(new DbContextOptionsBuilder<FacturacionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(_empresa));

    private FakeProductosReadPort Productos() => new(new ProductoFiscalLectura(
        _producto, "Vidrio", "30171500", "H87", "02", 0.16m, null, null, "Nacional", "TEMPLADO"));

    private PedidoFacturable Pedido(FacturacionDbContext db, decimal? ranura = null)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(_empresa, "AW-U16", _sucursal, _cliente,
            "Cliente", 1, ComportamientoFiscal.MostradorInmediato, "USD", null, null, null, 1, "15", ranura);
        pedido.AgregarLinea(_producto, "Vidrio", "30171500", "H87", 1m, 10000m, 0m, false, tasaIva: 0.16m);
        pedido.RecalcularTotal();
        db.PedidosFacturables.Add(pedido);
        db.SaveChanges();
        return pedido;
    }

    private FacturaVenta Factura(FacturacionDbContext db, Guid? pedidoId, bool pedimento = false)
    {
        var f = FacturaVenta.CrearBorrador(_empresa, "F-U16", 1, _sucursal, null, null, Receptor, Emisor,
            "PUE", "03", "USD", 18.25m, 2026, 9, 1, ComportamientoFiscal.MostradorInmediato,
            pedidoId, null, null, false);
        f.AgregarLinea(_producto, "30171500", "Vidrio", "H87", 1m, 10000m, 0m, "02", 0.16m, null, null,
            requierePedimento: pedimento);
        f.RecalcularTotales();
        if (pedimento) f.MarcarPendientePedimento();
        else
        {
            f.MarcarTimbradoEnProceso();
            f.MarcarTimbradoFallido("CFDI40139", "Rechazo");
        }
        db.FacturasVenta.Add(f);
        db.SaveChanges();
        return f;
    }

    private ReintentarTimbradoHandler Reintentar(FacturacionDbContext db) => new(db,
        new FakeSender(new ReservarFolioResponse("NC-U16", 2, "")), new FakePeriodoContablePort(), _pac,
        new FakeCfdiRepositorioPort(), _eventos, new FakeContabilidadAsientoPort(),
        new FakeClock(Ahora.AddDays(1)), ReceptorFiscalTestFactory.Crear(db), Productos());

    private EmitirFacturaVentaCommand Venta(Guid? pedidoId = null) => new(
        _sucursal, Receptor.Rfc, Receptor.Nombre, "601", "97000", "G03", "MEX", Emisor.Rfc, "601",
        "PUE", "03", "USD", 18.25m, 1, ComportamientoFiscal.MostradorInmediato, null, null, false,
        [new EmitirFacturaVentaLinea(_producto, "30171500", "Vidrio", "H87", 1m, 10000m, 0m, "02", 0.16m, null, null)],
        PedidoFacturableId: pedidoId);

    private EmitirFacturaVentaHandler EmitirVenta(FacturacionDbContext db, FakeCatalogosSatReadPort catalogos) =>
        new(db, new FakeSender(new ReservarFolioResponse("F-U16", 1, "")), new FakePeriodoContablePort(),
            catalogos, _pac, new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(_empresa, Emisor.Rfc, "Millet", "601", 0.16m, "76120")),
            _eventos, new FakeContabilidadAsientoPort(), new FakeEmpresaContext(_empresa),
            new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db), Productos());

    [Fact]
    public async Task Emision_11600_ranura_1160_publica_dimensiones_y_desglose_en_ambos_eventos()
    {
        using var db = Db();
        var pedido = Pedido(db, 1160m);
        var catalogos = new FakeCatalogosSatReadPort(tipoCambio: 18.25m);
        await EmitirVenta(db, catalogos).Handle(Venta(pedido.Id), default);

        var venta = _eventos.Publicados.OfType<FacturaVentaTimbradaIntegrationEvent>().Single();
        venta.Total.Should().Be(11600m);
        venta.Subtotal.Should().Be(10000m);
        venta.Iva.Should().Be(1600m);
        venta.ClienteId.Should().Be(_cliente);
        venta.SucursalId.Should().Be(_sucursal);
        venta.TipoCambio.Should().Be(18.25m);
        venta.Lineas.Should().ContainSingle().Which.TipoProducto.Should().Be("TEMPLADO");
        var nc = _eventos.Publicados.OfType<NotaCreditoTimbradaIntegrationEvent>().Single();
        nc.Motivo.Should().Be("Ranura");
        nc.FacturaRelacionadaId.Should().Be(venta.FacturaVentaId);
        nc.Total.Should().Be(1160m);
        nc.Subtotal.Should().Be(1000m);
        nc.Iva.Should().Be(160m);
        nc.ClienteId.Should().Be(_cliente);
        nc.SucursalId.Should().Be(_sucursal);
        nc.TipoCambio.Should().Be(18.25m);
        catalogos.ConsultaTipoCambio.Should().Be(("USD", new DateOnly(2026, 9, 30)), "el día de emisión es local");
    }

    [Theory]
    [InlineData("reintento", true)]
    [InlineData("reintento", false)]
    [InlineData("pedimento", true)]
    [InlineData("pedimento", false)]
    public async Task Timbrado_diferido_publica_cliente_del_pedido_o_null_y_conserva_TC(string camino, bool conPedido)
    {
        using var db = Db();
        var pedido = conPedido ? Pedido(db, 1160m) : null;
        var factura = Factura(db, pedido?.Id, camino == "pedimento");
        var id = factura.Id;
        db.ChangeTracker.Clear();

        if (camino == "reintento") await Reintentar(db).Handle(new ReintentarTimbradoCommand(id), default);
        else await new AplicarPedimentoHandler(db, new FakeSender(new ReservarFolioResponse("NC-U16", 2, "")),
            new FakePeriodoContablePort(), _pac, new FakeCfdiRepositorioPort(), _eventos,
            new FakeClock(Ahora.AddDays(1)), ReceptorFiscalTestFactory.Crear(db), Productos())
            .Handle(new AplicarPedimentoCommand(id, "15  47  3001  0001234", new DateOnly(2026, 9, 30), "ID-1"), default);

        var evento = _eventos.Publicados.OfType<FacturaVentaTimbradaIntegrationEvent>().Single();
        evento.ClienteId.Should().Be(conPedido ? _cliente : null);
        evento.TipoCambio.Should().Be(18.25m, "no se cambia al TC del día del reintento/pedimento");
        evento.Lineas.Should().ContainSingle().Which.TipoProducto.Should().Be("TEMPLADO");
        if (conPedido)
        {
            var nc = _eventos.Publicados.OfType<NotaCreditoTimbradaIntegrationEvent>().Single();
            nc.ClienteId.Should().Be(_cliente);
            nc.Total.Should().Be(1160m);
            nc.TipoCambio.Should().Be(18.25m);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Bonificacion_publica_cliente_de_factura_relacionada_o_null(bool conPedido)
    {
        using var db = Db();
        var pedido = conPedido ? Pedido(db) : null;
        var factura = Factura(db, pedido?.Id);
        factura.ReabrirParaReintentoTimbrado();
        factura.MarcarTimbradoEnProceso();
        factura.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        await new EmitirNotaCreditoBonificacionHandler(db, new FakeSender(new ReservarFolioResponse("NC-U16", 2, "")),
            new FakePeriodoContablePort(), _pac, new FakeCfdiRepositorioPort(), _eventos,
            new FakeEmpresaContext(_empresa), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora.AddDays(1)),
            ReceptorFiscalTestFactory.Crear(db))
            .Handle(new EmitirNotaCreditoBonificacionCommand(factura.Id, 1160m, 0.16m, "Descuento"), default);
        var nc = _eventos.Publicados.OfType<NotaCreditoTimbradaIntegrationEvent>().Single();
        nc.ClienteId.Should().Be(conPedido ? _cliente : null);
        nc.TipoCambio.Should().Be(18.25m);
    }

    [Theory]
    [InlineData("Ranura", true)]
    [InlineData("Bonificacion", true)]
    [InlineData("Amortizacion", true)]
    [InlineData("Amortizacion", false)]
    [InlineData("Bonificacion", false)]
    public async Task Reintento_NC_resuelve_cliente_del_anticipo_o_factura_y_conserva_desglose(string motivo, bool conOrigen)
    {
        using var db = Db();
        var pedido = conOrigen && motivo != "Amortizacion" ? Pedido(db) : null;
        var factura = Factura(db, pedido?.Id);
        var anticipo = Anticipo.Crear(_empresa, _cliente, Receptor.Rfc, TipoAnticipo.ClientesMxp,
            "USD", 1160m, Guid.NewGuid());
        if (conOrigen && motivo == "Amortizacion") db.Anticipos.Add(anticipo);
        var nc = motivo switch
        {
            "Ranura" => NotaCredito.CrearRanura(_empresa, "NC-U16", 2, _sucursal, null, null, Receptor, Emisor,
                "03", "USD", 18.25m, 2026, 9, 1, factura.Id, 1160m, 0.16m, "Ranura"),
            "Amortizacion" => NotaCredito.CrearAmortizacion(_empresa, "NC-U16", 2, _sucursal, null, null, Receptor, Emisor,
                "03", "USD", 18.25m, 2026, 9, 1, anticipo.Id, factura.Id, 1160m, 0.16m),
            _ => NotaCredito.CrearBonificacion(_empresa, "NC-U16", 2, _sucursal, null, null, Receptor, Emisor,
                "03", "USD", 18.25m, 2026, 9, 1, factura.Id, 1160m, 0.16m, "Bonificación")
        };
        nc.MarcarTimbradoEnProceso();
        nc.MarcarTimbradoFallido("CFDI40139", "Rechazo");
        db.NotasCredito.Add(nc);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        await Reintentar(db).Handle(new ReintentarTimbradoCommand(nc.Id), default);
        var evento = _eventos.Publicados.OfType<NotaCreditoTimbradaIntegrationEvent>().Single();
        evento.ClienteId.Should().Be(conOrigen ? _cliente : null);
        evento.Subtotal.Should().Be(1000m);
        evento.Iva.Should().Be(160m);
        evento.TipoCambio.Should().Be(18.25m);
    }

    private EmitirFacturaAnticipoCommand AnticipoCommand(decimal? tc) => new(
        _sucursal, _cliente, Receptor.Rfc, Receptor.Nombre, "601", "97000", "G03", "MEX", Emisor.Rfc, "601",
        "PUE", "03", "USD", tc, TipoAnticipo.ClientesMxp, 1000m, 0.16m, null, null, null, null, null);

    private EmitirFacturaAnticipoHandler EmitirAnticipo(FacturacionDbContext db, FakeCatalogosSatReadPort catalogos) =>
        new(db, new FakeSender(new ReservarFolioResponse("FANT-U16", 1, "")), new FakePeriodoContablePort(), catalogos,
            _pac, new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(_empresa, Emisor.Rfc, "Millet", "601", 0.16m, "76120")),
            _eventos, new FakeEmpresaContext(_empresa), new FakeUserContext(Guid.NewGuid()),
            new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db));

    [Fact]
    public async Task Venta_directa_publica_cliente_seleccionado_sin_pedido()
    {
        using var db = Db();
        await EmitirVenta(db, new FakeCatalogosSatReadPort(tipoCambio: 18.25m))
            .Handle(Venta() with { ClienteId = _cliente }, default);
        _eventos.Publicados.OfType<FacturaVentaTimbradaIntegrationEvent>().Single().ClienteId.Should().Be(_cliente);
    }

    [Fact]
    public async Task Anticipo_publica_TC_del_dia_local_y_cliente()
    {
        using var db = Db();
        var catalogos = new FakeCatalogosSatReadPort(tipoCambio: 18.25m);
        await EmitirAnticipo(db, catalogos).Handle(AnticipoCommand(18.25m), default);
        var evento = _eventos.Publicados.OfType<FacturaAnticipoTimbradaIntegrationEvent>().Single();
        evento.ClienteId.Should().Be(_cliente);
        evento.TipoCambio.Should().Be(18.25m);
        catalogos.ConsultaTipoCambio.Should().Be(("USD", new DateOnly(2026, 9, 30)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TC_faltante_o_de_otro_dia_impide_emision_sin_PAC_ni_persistencia(bool anticipo, bool falta)
    {
        using var db = Db();
        var catalogos = new FakeCatalogosSatReadPort(tipoCambio: falta ? null : 19m);
        Func<Task> act = async () =>
        {
            if (anticipo) await EmitirAnticipo(db, catalogos).Handle(AnticipoCommand(18.25m), default);
            else await EmitirVenta(db, catalogos).Handle(Venta(), default);
        };
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(
            falta ? "TIPO_CAMBIO_NO_REGISTRADO" : "TIPO_CAMBIO_NO_CORRESPONDE_FECHA");
        _pac.UltimaEmision.Should().BeNull();
        _eventos.Publicados.Should().BeEmpty();
        (await db.Comprobantes.CountAsync()).Should().Be(0);
        (await db.Anticipos.CountAsync()).Should().Be(0);
    }
}
