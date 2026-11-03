using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.PublicAdapters;

/// <summary>86e3jvv6z: pagos acumulados por factura A+W (forma SAT + moneda) para el retorno tipo 4.</summary>
public sealed class AwPagosReadAdapterTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly DatosFiscalesEmisor Emisor = new("AAA010101AAA", "Millet", "601", "76120");
    private static readonly DatosFiscalesReceptor Receptor = new("BBB010101BBB", "Cliente SA", "601", "64000", "G03", "MEX", false);

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(Empresa));

    private static AwPagosReadAdapter Adapter(FacturacionDbContext db) =>
        new(db, new FakeEmpresaContext(null, isBypassed: true));

    private static PedidoFacturable Pedido(FacturacionDbContext db, string numero, bool aw = true, Guid? clienteId = null)
    {
        var p = aw
            ? PedidoFacturable.ImportarDesdeAw(Empresa, numero, Guid.NewGuid(), clienteId ?? Guid.NewGuid(), "Cliente", (short)1,
                ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "42")
            : PedidoFacturable.CrearManual(Empresa, numero, Sucursal, clienteId ?? Guid.NewGuid(), "Cliente", 1,
                ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, Guid.NewGuid(),
                ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Servicio, null, null);
        db.PedidosFacturables.Add(p);
        return p;
    }

    private static FacturaVenta Venta(FacturacionDbContext db, string folio, PedidoFacturable pedido)
    {
        var f = FacturaVenta.CrearBorrador(Empresa, folio, 1, Sucursal, null, null, Receptor, Emisor,
            "PUE", "03", "MXN", null, 2026, 11, 1, ComportamientoFiscal.MostradorInmediato, pedido.Id, null, null, false);
        f.AgregarLinea(null, "01010101", "P", "H87", 1m, 1000m, 0m, "01", 0m, null, null);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.FacturasVenta.Add(f);
        return f;
    }

    private static CobroMostrador Cobro(FacturacionDbContext db, Guid comprobante, params (string Forma, decimal Importe)[] formas)
    {
        var c = CobroMostrador.Registrar(Empresa, Guid.NewGuid(), Sucursal, null, comprobante, OrigenCobroMostrador.Mostrador,
            Ahora, Guid.NewGuid(), formas.Select(f => (f.Forma, f.Importe, (string?)null, (string?)null, (string?)null, (string?)null)).ToList());
        db.CobrosMostrador.Add(c);
        return c;
    }

    private static ReciboPago Repp(FacturacionDbContext db, params (FacturaVenta Factura, string Forma, decimal Importe)[] lineas)
    {
        var r = ReciboPago.CrearBorrador(Empresa, $"REPP-{Guid.NewGuid():N}", 1, Sucursal, null, null, Receptor, Emisor,
            2026, 11, Ahora, "MXN", 1);
        foreach (var (f, forma, importe) in lineas)
            r.AgregarFacturaPagada(f.Id, f.Uuid!, 1, "MXN", importe, f.Total, forma, null, null, null, null, "REF", "01", f.Total, []);
        r.EstablecerImporteTotalPago(lineas.Sum(l => l.Importe));
        r.MarcarTimbradoEnProceso();
        r.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.RecibosPago.Add(r);
        return r;
    }

    private static FacturaAnticipo Anticipo(FacturacionDbContext db, string folio, Guid? pedidoId, string? pedidoRef, Guid? clienteId = null)
    {
        var id = Guid.NewGuid();
        var f = FacturaAnticipo.CrearBorrador(Empresa, folio, 1, Sucursal, null, null, Receptor, Emisor, "03", "MXN", null,
            2026, 11, TipoAnticipo.ClientesMxp, pedidoId, id, 1000m, 0.16m);
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.FacturasAnticipo.Add(f);
        db.Anticipos.Add(Millet.Facturacion.Domain.Anticipos.Anticipo.Crear(Empresa, clienteId ?? Guid.NewGuid(), "BBB010101BBB",
            TipoAnticipo.ClientesMxp, "MXN", 1160m, f.Id, pedidoRef));
        return f;
    }

    [Fact]
    public async Task Efectivo_y_tarjeta_en_un_cobro_dan_dos_grupos()
    {
        await using var db = NewDb();
        var f = Venta(db, "F-1", Pedido(db, "10500001"));
        Cobro(db, f.Id, ("01", 300m), ("04", 700m));
        await db.SaveChangesAsync();

        var r = (await Adapter(db).PorComprobanteAsync(f.Id, default)).Single();

        r.NumeroPedidoAw.Should().Be("10500001");
        r.Folio.Should().Be("F-1");
        r.EsFacturaFinal.Should().BeTrue();
        r.Grupos.Should().BeEquivalentTo([new PagoGrupoAw("01", "MXN", 300m), new PagoGrupoAw("04", "MXN", 700m)]);
    }

    [Fact]
    public async Task Dos_cobros_de_la_misma_forma_se_suman()
    {
        await using var db = NewDb();
        var f = Venta(db, "F-1", Pedido(db, "10500001"));
        Cobro(db, f.Id, ("01", 300m));
        Cobro(db, f.Id, ("01", 200m));
        await db.SaveChangesAsync();

        (await Adapter(db).PorComprobanteAsync(f.Id, default)).Single().Grupos
            .Should().BeEquivalentTo([new PagoGrupoAw("01", "MXN", 500m)]);
    }

    [Fact]
    public async Task Cobro_cancelado_no_cuenta()
    {
        await using var db = NewDb();
        var f = Venta(db, "F-1", Pedido(db, "10500001"));
        Cobro(db, f.Id, ("01", 300m)).Cancelar();
        Cobro(db, f.Id, ("04", 100m));
        await db.SaveChangesAsync();

        (await Adapter(db).PorComprobanteAsync(f.Id, default)).Single().Grupos
            .Should().BeEquivalentTo([new PagoGrupoAw("04", "MXN", 100m)]);
    }

    [Fact]
    public async Task Cobro_sobre_un_REPP_no_duplica_las_lineas_del_REPP()
    {
        await using var db = NewDb();
        var f = Venta(db, "F-1", Pedido(db, "10500001"));
        var repp = Repp(db, (f, "03", 400m));
        Cobro(db, repp.Id, ("03", 400m));
        await db.SaveChangesAsync();

        (await Adapter(db).PorComprobanteAsync(repp.Id, default)).Single().Grupos
            .Should().BeEquivalentTo([new PagoGrupoAw("03", "MXN", 400m)]);
        (await Adapter(db).PorComprobanteAsync(f.Id, default)).Single().Grupos
            .Should().BeEquivalentTo([new PagoGrupoAw("03", "MXN", 400m)]);
    }

    [Fact]
    public async Task REPP_que_paga_dos_facturas_da_dos_entradas()
    {
        await using var db = NewDb();
        var a = Venta(db, "F-1", Pedido(db, "10500001"));
        var b = Venta(db, "F-2", Pedido(db, "10500002"));
        var repp = Repp(db, (a, "03", 100m), (b, "03", 250m));
        await db.SaveChangesAsync();

        var r = await Adapter(db).PorComprobanteAsync(repp.Id, default);

        r.Select(x => (x.Folio, x.Grupos.Single().Importe)).Should().BeEquivalentTo([("F-1", 100m), ("F-2", 250m)]);
    }

    [Fact]
    public async Task Factura_de_anticipo_resuelve_el_pedido_por_PedidoOrigenRef()
    {
        await using var db = NewDb();
        var cliente = Guid.NewGuid();
        Pedido(db, "10500009", clienteId: cliente);
        var f = Anticipo(db, "FANT-1", null, "10500009", cliente);
        Cobro(db, f.Id, ("03", 1160m));
        await db.SaveChangesAsync();

        var r = (await Adapter(db).PorComprobanteAsync(f.Id, default)).Single();

        r.NumeroPedidoAw.Should().Be("10500009");
        r.EsFacturaFinal.Should().BeFalse();
        r.Grupos.Should().BeEquivalentTo([new PagoGrupoAw("03", "MXN", 1160m)]);
    }

    [Fact]
    public async Task El_PedidoOrigenRef_solo_cuenta_si_es_un_pedido_AW_del_mismo_cliente()
    {
        await using var db = NewDb();
        var cliente = Guid.NewGuid();
        Pedido(db, "10500010", clienteId: Guid.NewGuid());            // pedido A+W de otro cliente
        Pedido(db, "10500011", aw: false, clienteId: cliente);         // pedido manual del mismo cliente
        var otro = Anticipo(db, "FANT-2", null, "10500010", cliente);
        var manual = Anticipo(db, "FANT-3", null, "10500011", cliente);
        var inexistente = Anticipo(db, "FANT-4", null, "99999999", cliente);
        Cobro(db, otro.Id, ("03", 100m));
        Cobro(db, manual.Id, ("03", 100m));
        Cobro(db, inexistente.Id, ("03", 100m));
        await db.SaveChangesAsync();

        foreach (var f in new[] { otro, manual, inexistente })
            (await Adapter(db).PorComprobanteAsync(f.Id, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Factura_cancelada_no_devuelve_nada()
    {
        await using var db = NewDb();
        var f = Venta(db, "F-1", Pedido(db, "10500001"));
        Cobro(db, f.Id, ("01", 300m));
        f.MarcarCancelacionPendiente();
        f.MarcarCancelado();
        await db.SaveChangesAsync();

        (await Adapter(db).PorComprobanteAsync(f.Id, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Factura_de_pedido_no_AW_no_devuelve_nada()
    {
        await using var db = NewDb();
        var f = Venta(db, "F-1", Pedido(db, "P-MANUAL", aw: false));
        Cobro(db, f.Id, ("01", 300m));
        await db.SaveChangesAsync();

        (await Adapter(db).PorComprobanteAsync(f.Id, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task PorFolio_recalcula_la_factura_del_pedido()
    {
        await using var db = NewDb();
        var f = Venta(db, "F-1", Pedido(db, "10500001"));
        Cobro(db, f.Id, ("01", 300m));
        await db.SaveChangesAsync();

        (await Adapter(db).PorFolioAsync("10500001", "F-1", default))!.Grupos.Single().Importe.Should().Be(300m);
        (await Adapter(db).PorFolioAsync("99999999", "F-1", default)).Should().BeNull();
    }
}
