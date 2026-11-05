using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.PublicAdapters;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.PublicAdapters;

/// <summary>
/// U-3 (86e3yxp2b): saldo real por pedido para liberar por pago en Producto Terminado. Pedido de $10,000 + IVA = $11,600.
/// La cartera de CxC (saldo por factura, ya con cobros y NC) se simula con <see cref="CarteraFalsa"/>.
/// </summary>
public sealed class SaldoPedidoReadAdapterTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cliente = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private const string Numero = "10600001";

    private sealed class CarteraFalsa : ISaldoFacturaReadPort
    {
        public Dictionary<Guid, decimal> Saldos { get; } = [];
        public Task<decimal?> ObtenerSaldoAsync(Guid facturaVentaId, CancellationToken cancellationToken) =>
            Task.FromResult(Saldos.TryGetValue(facturaVentaId, out var s) ? s : (decimal?)null);
    }

    private readonly CarteraFalsa _cartera = new();

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(Empresa));

    private static PedidoFacturable Pedido(FacturacionDbContext db, decimal importe = 10_000m, string numero = Numero)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(Empresa, numero, Guid.NewGuid(), Cliente, "Cliente", (short)1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "42", condicionPagoOrigen: "30D");
        pedido.AgregarLinea(null, "Vidrio", null, "H87", 1m, importe, 0m, false, tasaIva: 0.16m);
        pedido.RecalcularTotal();
        db.PedidosFacturables.Add(pedido);
        return pedido;
    }

    private static FacturaVenta Factura(FacturacionDbContext db, PedidoFacturable pedido, decimal importe = 10_000m, bool timbrar = true)
    {
        var fv = FacturaVenta.CrearBorrador(Empresa, "F-000001", 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("BBB010101BBB", "Cliente SA", "601", "64000", "G03", "MEX", false),
            new DatosFiscalesEmisor("AAA010101AAA", "Millet", "601", "76120"), "PPD", "99", "MXN", null, 2026, 11,
            (short)1, ComportamientoFiscal.MostradorInmediato, pedido.Id, null, null, false);
        fv.AgregarLinea(null, "30171500", "Vidrio", "H87", 1m, importe, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        if (timbrar)
        {
            fv.MarcarTimbradoEnProceso();
            fv.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        }
        db.FacturasVenta.Add(fv);
        return fv;
    }

    private static Anticipo AnticipoDe(FacturacionDbContext db, decimal monto, string? pedidoRef)
    {
        var a = Anticipo.Crear(Empresa, Cliente, "BBB010101BBB", TipoAnticipo.ClientesMxp, "MXN", monto, Guid.NewGuid(), pedidoOrigenRef: pedidoRef);
        db.Anticipos.Add(a);
        return a;
    }

    private async Task<decimal?> Saldo(FacturacionDbContext db, string numero = Numero)
    {
        await db.SaveChangesAsync();
        return await new SaldoPedidoReadAdapter(db, _cartera, new FakeEmpresaContext(null, isBypassed: true))
            .ObtenerAsync(Empresa, numero, CancellationToken.None);
    }

    [Fact]
    public async Task Criterio_1_factura_cubierta_por_anticipo_aplicado_saldo_cero()
    {
        await using var db = NewDb();
        var fv = Factura(db, Pedido(db));
        _cartera.Saldos[fv.Id] = 0m; // la NC de amortización de $11,600 ya se aplicó en cartera

        (await Saldo(db)).Should().Be(0m);
    }

    [Fact]
    public async Task Criterio_1_sin_factura_anticipo_ligado_al_pedido_lo_cubre()
    {
        await using var db = NewDb();
        Pedido(db);
        AnticipoDe(db, 11_600m, Numero);

        (await Saldo(db)).Should().Be(0m);
    }

    [Fact]
    public async Task Criterio_2_con_6000_cobrados_queda_saldo_5600()
    {
        await using var db = NewDb();
        var pedido = Pedido(db);
        var fv = Factura(db, pedido);
        pedido.MarcarFacturado(fv.Id);
        _cartera.Saldos[fv.Id] = 5_600m;

        (await Saldo(db)).Should().Be(5_600m);
    }

    [Fact]
    public async Task Criterio_3_al_aplicarse_el_cobro_de_5600_el_saldo_llega_a_cero()
    {
        await using var db = NewDb();
        var pedido = Pedido(db);
        var fv = Factura(db, pedido);
        pedido.MarcarFacturado(fv.Id);
        _cartera.Saldos[fv.Id] = 5_600m;
        (await Saldo(db)).Should().Be(5_600m);

        _cartera.Saldos[fv.Id] = 0m; // REPP o cobro de mostrador de $5,600 aplicado en cartera

        (await Saldo(db)).Should().Be(0m);
    }

    [Fact]
    public async Task Criterio_4_pedido_sin_factura_saldo_es_el_importe_con_IVA()
    {
        await using var db = NewDb();
        Pedido(db);

        (await Saldo(db)).Should().Be(11_600m);
    }

    [Fact]
    public async Task Factura_en_borrador_o_cancelada_no_cuenta_como_facturada()
    {
        await using var db = NewDb();
        var pedido = Pedido(db);
        var borrador = Factura(db, pedido, timbrar: false);
        _cartera.Saldos[borrador.Id] = 0m;

        (await Saldo(db)).Should().Be(11_600m);
    }

    [Fact]
    public async Task Factura_que_la_cartera_aun_no_proyecta_cuenta_por_su_total()
    {
        await using var db = NewDb();
        var pedido = Pedido(db);
        pedido.MarcarFacturado(Factura(db, pedido).Id);

        (await Saldo(db)).Should().Be(11_600m);
    }

    [Fact]
    public async Task Factura_parcial_suma_lo_que_falta_facturar()
    {
        await using var db = NewDb();
        var pedido = Pedido(db);
        var fv = Factura(db, pedido, importe: 4_000m); // $4,640 facturados, el pedido sigue sin estado Facturado
        _cartera.Saldos[fv.Id] = 0m;

        (await Saldo(db)).Should().Be(6_960m);
    }

    [Fact]
    public async Task Anticipo_vinculado_a_la_factura_sin_NC_todavia_reduce_el_saldo()
    {
        await using var db = NewDb();
        var pedido = Pedido(db);
        var fv = Factura(db, pedido);
        pedido.MarcarFacturado(fv.Id);
        _cartera.Saldos[fv.Id] = 11_600m;
        AnticipoDe(db, 5_000m, "OTRO").Vincular(fv.Id, 3_000m, Ahora);

        (await Saldo(db)).Should().Be(8_600m);
    }

    [Fact]
    public async Task Anticipo_de_otro_pedido_no_cubre_este()
    {
        await using var db = NewDb();
        Pedido(db);
        AnticipoDe(db, 11_600m, "99999999");

        (await Saldo(db)).Should().Be(11_600m);
    }

    [Theory]
    [InlineData(0.50, 0)]
    [InlineData(1.00, 0)]
    [InlineData(1.01, 1.01)]
    public async Task Tolerancia_de_un_peso_por_redondeo(decimal enCartera, decimal esperado)
    {
        await using var db = NewDb();
        var pedido = Pedido(db);
        var fv = Factura(db, pedido);
        pedido.MarcarFacturado(fv.Id);
        _cartera.Saldos[fv.Id] = enCartera;

        (await Saldo(db)).Should().Be(esperado);
    }

    [Fact]
    public async Task Pedido_no_ingerido_o_cancelado_es_desconocido()
    {
        await using var db = NewDb();
        Pedido(db, numero: "10600002").Cancelar();

        (await Saldo(db, "10600002")).Should().BeNull();
        (await Saldo(db, "10600999")).Should().BeNull();
    }
}
