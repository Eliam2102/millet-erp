using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.PublicAdapters;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.PublicAdapters;

public sealed class PedidosPtReadAdapterTests
{
    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static async Task<(FacturacionDbContext Db, Guid Empresa)> ConPedidoAsync()
    {
        var empresa = Guid.NewGuid();
        var db = NewDb(empresa);
        var pedido = PedidoFacturable.ImportarDesdeAw(empresa, "10425068", Guid.NewGuid(), Guid.NewGuid(), "Cliente", (short)1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 7, "15", condicionPagoOrigen: "30D");
        pedido.AgregarLinea(null, "Vidrio B", null, "H87", 3m, 10m, 0m, false);
        pedido.AgregarLinea(null, "Vidrio A", null, "E48", 1m, 10m, 0m, false);
        db.PedidosFacturables.Add(pedido);
        await db.SaveChangesAsync();
        return (db, empresa);
    }

    [Fact]
    public async Task Devuelve_el_pedido_con_lineas_ordenadas_y_version()
    {
        var (db, empresa) = await ConPedidoAsync();
        var r = await new PedidosPtReadAdapter(db).ObtenerPorNumeroAsync(empresa, " 10425068 ", CancellationToken.None);

        r.Should().NotBeNull();
        r!.VersionOrigen.Should().Be(7);
        r.CondicionPagoOrigen.Should().Be("30D");
        r.Estado.Should().Be(nameof(EstadoPedidoFacturable.Importado));
        r.Lineas.Should().HaveCount(2);
        r.Lineas.Select(l => l.Posicion).Should().BeInAscendingOrder();
        r.Lineas.Select(l => l.ClaveUnidadSat).Should().BeEquivalentTo("H87", "E48");
    }

    [Fact]
    public async Task Numero_desconocido_devuelve_null()
    {
        var (db, empresa) = await ConPedidoAsync();
        (await new PedidosPtReadAdapter(db).ObtenerPorNumeroAsync(empresa, "999", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Otra_empresa_devuelve_null()
    {
        var (db, _) = await ConPedidoAsync();
        (await new PedidosPtReadAdapter(db).ObtenerPorNumeroAsync(Guid.NewGuid(), "10425068", CancellationToken.None)).Should().BeNull();
    }
}
