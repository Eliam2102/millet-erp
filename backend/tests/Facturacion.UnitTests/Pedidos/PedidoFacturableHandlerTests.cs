using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Pedidos.CrearPedidoFacturableManual;
using Millet.Facturacion.Application.Pedidos.EditarPedidoFacturableManual;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Pedidos;

public sealed class PedidoFacturableHandlerTests
{
    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static CrearPedidoFacturableManualCommand CrearCmd() => new(
        NumeroPedido: "MAN-1",
        SucursalId: Guid.NewGuid(),
        ClienteId: Guid.NewGuid(),
        ClienteNombre: "Cliente",
        CanalVenta: (short)10,
        ComportamientoFiscal: ComportamientoFiscal.Administrativa,
        Moneda: "MXN",
        ObraId: null,
        ObraNombre: null,
        Comentarios: null,
        Lineas: [new PedidoFacturableLineaInput(null, "Producto", "01010101", "H87", 2m, 100m, 0m, false)]);

    [Fact]
    public async Task Crear_persiste_pedido_Importado_con_total()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var handler = new CrearPedidoFacturableManualHandler(db, new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeProductosReadPort(), new FakeEmpresaFiscalReadPort());

        var resp = await handler.Handle(CrearCmd(), CancellationToken.None);

        resp.Estado.Should().Be("Importado");
        resp.Total.Should().Be(200m);

        var pedido = await db.PedidosFacturables.Include(p => p.Lineas).SingleAsync();
        pedido.Origen.Should().Be(OrigenPedido.Manual);
        pedido.Lineas.Should().ContainSingle();
    }

    [Fact]
    public async Task Editar_con_version_correcta_reemplaza_lineas()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var crear = new CrearPedidoFacturableManualHandler(db, new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeProductosReadPort(), new FakeEmpresaFiscalReadPort());
        var creado = await crear.Handle(CrearCmd(), CancellationToken.None);

        var editar = new EditarPedidoFacturableManualHandler(db, new FakeEmpresaContext(empresaId), new FakeProductosReadPort(), new FakeEmpresaFiscalReadPort());
        var resp = await editar.Handle(new EditarPedidoFacturableManualCommand(
            creado.Id, creado.Version, Guid.NewGuid(), "Cliente editado", (short)1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null,
            [new PedidoFacturableLineaInput(null, "Otro", "01010101", "H87", 1m, 30m, 0m, false)]),
            CancellationToken.None);

        resp.Total.Should().Be(30m);
        var pedido = await db.PedidosFacturables.Include(p => p.Lineas).SingleAsync();
        pedido.ClienteNombre.Should().Be("Cliente editado");
        pedido.Lineas.Should().ContainSingle();
        pedido.Lineas.Single().Importe.Should().Be(30m);
    }

    [Fact]
    public async Task Editar_con_version_stale_lanza_ConcurrencyException()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var crear = new CrearPedidoFacturableManualHandler(db, new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeProductosReadPort(), new FakeEmpresaFiscalReadPort());
        var creado = await crear.Handle(CrearCmd(), CancellationToken.None);

        var editar = new EditarPedidoFacturableManualHandler(db, new FakeEmpresaContext(empresaId), new FakeProductosReadPort(), new FakeEmpresaFiscalReadPort());
        var act = () => editar.Handle(new EditarPedidoFacturableManualCommand(
            creado.Id, creado.Version + 99, Guid.NewGuid(), "X", (short)1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null,
            [new PedidoFacturableLineaInput(null, "Otro", "01010101", "H87", 1m, 30m, 0m, false)]),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    [Fact]
    public async Task Editar_pedido_inexistente_lanza_NotFound()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var editar = new EditarPedidoFacturableManualHandler(db, new FakeEmpresaContext(empresaId), new FakeProductosReadPort(), new FakeEmpresaFiscalReadPort());

        var act = () => editar.Handle(new EditarPedidoFacturableManualCommand(
            Guid.NewGuid(), 0, Guid.NewGuid(), "X", (short)1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null,
            [new PedidoFacturableLineaInput(null, "Otro", "01010101", "H87", 1m, 30m, 0m, false)]),
            CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>();
    }

    [Fact]
    public async Task Crear_resuelve_tasa_iva_con_precedencia_articulo_sobre_empresa()
    {
        var empresaId = Guid.NewGuid();
        var productoId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var emisor = new EmpresaFiscalLectura(empresaId, "MIL010101ABC", "Millet", "601", 0.08m, "76120");
        var master = new ProductoFiscalLectura(
            productoId, "Vidrio", "43211701", "MTK", "02", 0.16m, null, null, "Aw");
        var handler = new CrearPedidoFacturableManualHandler(
            db, new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()),
            new FakeProductosReadPort(master), new FakeEmpresaFiscalReadPort(emisor));

        var cmd = CrearCmd() with
        {
            Lineas =
            [
                new PedidoFacturableLineaInput(productoId, "Con master", "43211701", "MTK", 1m, 100m, 0m, false),
                new PedidoFacturableLineaInput(null, "Sin master", "01010101", "H87", 1m, 50m, 0m, false),
            ],
        };
        await handler.Handle(cmd, CancellationToken.None);

        var lineas = (await db.PedidosFacturables.Include(p => p.Lineas).SingleAsync())
            .Lineas.OrderBy(l => l.Posicion).ToList();
        lineas[0].TasaIva.Should().Be(0.16m, "el IVA del artículo tiene prioridad sobre el default de empresa");
        lineas[1].TasaIva.Should().Be(0.08m, "sin artículo, cae al IVA default de la empresa");
    }

    [Fact]
    public async Task Crear_sin_master_ni_default_de_empresa_deja_tasa_null()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var handler = new CrearPedidoFacturableManualHandler(
            db, new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()),
            new FakeProductosReadPort(), new FakeEmpresaFiscalReadPort());

        await handler.Handle(CrearCmd(), CancellationToken.None);

        (await db.PedidosFacturables.Include(p => p.Lineas).SingleAsync())
            .Lineas.Single().TasaIva.Should().BeNull();
    }

    [Fact]
    public async Task Bandeja_lista_los_pedidos_creados()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var crear = new CrearPedidoFacturableManualHandler(db, new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeProductosReadPort(), new FakeEmpresaFiscalReadPort());
        await crear.Handle(CrearCmd(), CancellationToken.None);
        await crear.Handle(CrearCmd(), CancellationToken.None);

        var bandeja = new BandejaPedidosFacturablesHandler(db, new FakeAlcanceCajaEvaluator());
        var response = await bandeja.Handle(new BandejaPedidosFacturablesQuery(EstadoPedidoFacturable.Importado, OrigenPedido.Manual, 0, 50), CancellationToken.None);

        response.Items.Should().HaveCount(2);
        response.Items.Should().OnlyContain(i => i.Estado == "Importado" && i.Origen == "Manual");
    }
}
