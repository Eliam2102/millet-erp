using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.PublicAdapters;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.PublicAdapters;

/// <summary>C7.1 (86e3kdxx4): pedidos a crédito en firme sin factura, con su anticipo ligado.</summary>
public sealed class PedidosSinFacturarReadAdapterTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cliente = Guid.NewGuid();
    private static readonly DatosFiscalesEmisor Emisor = new("AAA010101AAA", "Millet", "601", "76120");
    private static readonly DatosFiscalesReceptor Receptor =
        new("BBB010101BBB", "Cliente SA", "601", "64000", "G03", "MEX", false);

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(Empresa));

    private static PedidoFacturable Pedido(
        FacturacionDbContext db, string numero, decimal importe, string condicion = "30D",
        string moneda = "MXN", Guid? cliente = null, string estatus = "42")
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(Empresa, numero, Guid.NewGuid(), cliente ?? Cliente, "Cliente", (short)1,
            ComportamientoFiscal.MostradorInmediato, moneda, null, null, null, 1, estatus, condicionPagoOrigen: condicion);
        pedido.AgregarLinea(null, "Vidrio", null, "H87", 1m, importe, 0m, false);
        db.PedidosFacturables.Add(pedido);
        return pedido;
    }

    private static async Task<IReadOnlyList<Millet.CuentasPorCobrar.Domain.Ports.Facturacion.PedidoSinFacturarDto>> Listar(
        FacturacionDbContext db)
    {
        await db.SaveChangesAsync();
        return await new PedidosSinFacturarReadAdapter(db).ListarPorClienteAsync(Cliente, CancellationToken.None);
    }

    [Fact]
    public async Task Pedido_a_credito_en_firme_sin_factura_se_lista_con_su_total()
    {
        await using var db = NewDb();
        var pedido = Pedido(db, "10500001", 20_000m);

        var r = await Listar(db);

        r.Should().ContainSingle();
        r[0].NumeroPedido.Should().Be("10500001");
        r[0].Moneda.Should().Be("MXN");
        r[0].Total.Should().Be(pedido.Total);
        r[0].AnticipoLigado.Should().Be(0m);
    }

    [Fact]
    public async Task Total_va_con_IVA_como_la_factura_cuando_las_lineas_tienen_tasa()
    {
        await using var db = NewDb();
        var pedido = PedidoFacturable.ImportarDesdeAw(Empresa, "10500010", Guid.NewGuid(), Cliente, "Cliente", (short)1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "42", condicionPagoOrigen: "30D");
        pedido.AgregarLinea(null, "Vidrio", null, "H87", 1m, 20_000m, 0m, false, tasaIva: 0.16m);
        db.PedidosFacturables.Add(pedido);

        var r = await Listar(db);

        r.Single().Total.Should().Be(23_200m);
    }

    [Theory]
    [InlineData("15", true)]
    [InlineData("40", true)]
    [InlineData("42", true)]
    [InlineData("70", true)]
    [InlineData("20", false)]
    [InlineData("900", false)]
    [InlineData("999", false)]
    [InlineData(null, true)]
    public void V22_estatus_que_compromete_credito(string? estatus, bool compromete) =>
        PedidosSinFacturarReadAdapter.ComprometeCredito(estatus).Should().Be(compromete);

    [Fact]
    public async Task V22_pedido_en_estatus_previo_a_15_y_40_no_se_lista()
    {
        await using var db = NewDb();
        Pedido(db, "10500011", 20_000m, estatus: "20");
        Pedido(db, "10500012", 20_000m, estatus: "15");

        (await Listar(db)).Select(p => p.NumeroPedido).Should().Equal("10500012");
    }

    [Fact]
    public async Task Pedido_de_contado_no_se_lista()
    {
        await using var db = NewDb();
        Pedido(db, "10500002", 20_000m, condicion: "CONTADO");

        (await Listar(db)).Should().BeEmpty();
    }

    [Fact]
    public async Task C7_1_a_pedido_facturado_o_cancelado_no_se_lista()
    {
        await using var db = NewDb();
        Pedido(db, "10500003", 20_000m).MarcarFacturado(Guid.NewGuid());
        Pedido(db, "10500004", 20_000m).Cancelar();
        var bloqueado = Pedido(db, "10500005", 5_000m);
        bloqueado.Bloquear();

        var r = await Listar(db);

        r.Select(p => p.NumeroPedido).Should().Equal("10500005");
    }

    [Fact]
    public async Task No_mezcla_pedidos_de_otro_cliente()
    {
        await using var db = NewDb();
        Pedido(db, "10500006", 20_000m, cliente: Guid.NewGuid());

        (await Listar(db)).Should().BeEmpty();
    }

    [Fact]
    public async Task Anticipo_ligado_por_folio_del_pedido_suma_su_saldo()
    {
        await using var db = NewDb();
        Pedido(db, "10500007", 20_000m);
        db.Anticipos.Add(Anticipo.Crear(Empresa, Cliente, "BBB010101BBB", TipoAnticipo.ClientesMxp, "MXN",
            12_000m, Guid.NewGuid(), pedidoOrigenRef: "10500007"));
        // Anticipo de otro pedido y en otra moneda: no cubren este.
        db.Anticipos.Add(Anticipo.Crear(Empresa, Cliente, "BBB010101BBB", TipoAnticipo.ClientesMxp, "MXN",
            5_000m, Guid.NewGuid(), pedidoOrigenRef: "99999999"));
        db.Anticipos.Add(Anticipo.Crear(Empresa, Cliente, "BBB010101BBB", TipoAnticipo.ClientesUsd, "USD",
            5_000m, Guid.NewGuid(), pedidoOrigenRef: "10500007"));

        var r = await Listar(db);

        r.Single().AnticipoLigado.Should().Be(12_000m);
    }

    [Fact]
    public async Task Anticipo_ligado_por_el_CFDI_de_anticipo_del_pedido()
    {
        await using var db = NewDb();
        var pedido = Pedido(db, "10500008", 20_000m);
        var anticipoId = Guid.NewGuid();
        var factura = FacturaAnticipo.CrearBorrador(
            empresaId: Empresa, folio: "FANT-000010", folioNumero: 10,
            sucursalId: Guid.NewGuid(), cajaId: null, usuarioEmisorId: null,
            receptor: Receptor, emisor: Emisor,
            formaPago: "03", moneda: "MXN", tipoCambio: null,
            periodoAnio: 2026, periodoMes: 10,
            tipoAnticipo: TipoAnticipo.ClientesMxp, pedidoFacturableId: pedido.Id,
            anticipoId: anticipoId, montoBase: 1000m, tasaIvaTraslado: 0.16m);
        db.FacturasAnticipo.Add(factura);
        db.Anticipos.Add(Anticipo.Crear(Empresa, Cliente, "BBB010101BBB", TipoAnticipo.ClientesMxp, "MXN",
            20_000m, factura.Id, id: anticipoId));

        var r = await Listar(db);

        r.Single().AnticipoLigado.Should().Be(20_000m);
    }
}
