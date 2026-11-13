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

/// <summary>N-14: el pedido manual no acepta un cliente inactivo, igual que la factura. Datos genéricos.</summary>
public sealed class PedidoManualClienteInactivoTests
{
    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(empresaId));

    private static ClienteFiscalLectura Cliente(Guid id, bool activo) =>
        new(id, "AAA010101AAA", "Cliente prueba", "601", "97000", "G03", "01", "PUE", "MXN", false, activo);

    private static CrearPedidoFacturableManualCommand CrearCmd(Guid clienteId) => new(
        NumeroPedido: "MAN-INACT-1", SucursalId: Guid.NewGuid(), ClienteId: clienteId, ClienteNombre: "Cliente prueba",
        CanalVenta: (short)10, ComportamientoFiscal: ComportamientoFiscal.Administrativa, Moneda: "MXN",
        ObraId: null, ObraNombre: null, Comentarios: null,
        Lineas: [new PedidoFacturableLineaInput(null, "Producto", "01010101", "H87", 2m, 100m, 0m, false)],
        ClaseManual: ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta: SubtipoVentaDirecta.Servicio);

    private static CrearPedidoFacturableManualHandler Crear(FacturacionDbContext db, Guid empresa, IClientesReadPort? clientes) =>
        new(db, new FakeEmpresaContext(empresa), new FakeUserContext(Guid.NewGuid()), new FakeProductosReadPort(),
            new FakeEmpresaFiscalReadPort(), new FakeCurrentUserPermissions(), clientes);

    private static EditarPedidoFacturableManualHandler Editar(FacturacionDbContext db, Guid empresa, IClientesReadPort? clientes) =>
        new(db, new FakeEmpresaContext(empresa), new FakeProductosReadPort(), new FakeEmpresaFiscalReadPort(),
            new FakeCurrentUserPermissions(), clientes);

    private static EditarPedidoFacturableManualCommand EditarCmd(Guid pedidoId, int version, Guid clienteId) => new(
        pedidoId, version, clienteId, "Cliente prueba", (short)10, ComportamientoFiscal.Administrativa, "MXN", null, null, null,
        [new PedidoFacturableLineaInput(null, "Producto", "01010101", "H87", 2m, 100m, 0m, false)],
        ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Servicio);

    [Fact]
    public async Task Un_pedido_manual_nuevo_con_cliente_inactivo_se_rechaza_como_en_la_factura_y_no_se_guarda()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var clienteId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Crear(db, empresa, new FakeClientesReadPort(Cliente(clienteId, activo: false))).Handle(CrearCmd(clienteId), CancellationToken.None));

        ex.Code.Should().Be("CLIENTE_INACTIVO");
        ex.Message.Should().Contain("inactivo").And.Contain("no admite ventas nuevas").And.Contain("se conservan");
        (await db.PedidosFacturables.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Un_cliente_activo_crea_el_pedido_manual_como_siempre()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var clienteId = Guid.NewGuid();

        var r = await Crear(db, empresa, new FakeClientesReadPort(Cliente(clienteId, activo: true))).Handle(CrearCmd(clienteId), CancellationToken.None);

        r.Estado.Should().Be("Importado");
        (await db.PedidosFacturables.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Sin_puerto_de_clientes_la_creacion_no_cambia()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);

        var r = await Crear(db, empresa, clientes: null).Handle(CrearCmd(Guid.NewGuid()), CancellationToken.None);

        r.Estado.Should().Be("Importado");
    }

    [Fact]
    public async Task Cambiar_un_pedido_manual_a_un_cliente_inactivo_se_rechaza()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var creado = await Crear(db, empresa, clientes: null).Handle(CrearCmd(Guid.NewGuid()), CancellationToken.None);
        var inactivo = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Editar(db, empresa, new FakeClientesReadPort(Cliente(inactivo, activo: false)))
                .Handle(EditarCmd(creado.Id, creado.Version, inactivo), CancellationToken.None));

        ex.Code.Should().Be("CLIENTE_INACTIVO");
        (await db.PedidosFacturables.SingleAsync()).ClienteId.Should().NotBe(inactivo);
    }

    [Fact]
    public async Task Editar_el_pedido_de_un_cliente_que_se_inactivo_despues_no_se_bloquea()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var clienteId = Guid.NewGuid();
        var creado = await Crear(db, empresa, new FakeClientesReadPort(Cliente(clienteId, activo: true))).Handle(CrearCmd(clienteId), CancellationToken.None);

        // El cliente se inactiva después; el mismo cliente del pedido ya capturado no se rechaza al editar otras cosas.
        var r = await Editar(db, empresa, new FakeClientesReadPort(Cliente(clienteId, activo: false)))
            .Handle(EditarCmd(creado.Id, creado.Version, clienteId), CancellationToken.None);

        r.Total.Should().Be(200m);
    }
}
