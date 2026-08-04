using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Catalogos;
using Millet.Facturacion.Application.Pedidos.CrearPedidoFacturableManual;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.Pedidos;

/// <summary>
/// FAC-ING-PR2 — el canal de venta pasó de enum hardcodeado a catálogo
/// administrable (<c>compartido.canales_venta</c>): el validator verifica
/// existencia+activo vía <see cref="ICanalesVentaReadPort"/>, el detalle
/// del pedido resuelve el nombre del catálogo y el lookup lista activos.
/// </summary>
public sealed class CanalVentaCatalogoTests
{
    private static CrearPedidoFacturableManualCommand ComandoValido(short canalVenta = 1) => new(
        NumeroPedido: null,
        SucursalId: Guid.NewGuid(),
        ClienteId: Guid.NewGuid(),
        ClienteNombre: "Cliente SA",
        CanalVenta: canalVenta,
        ComportamientoFiscal: ComportamientoFiscal.MostradorInmediato,
        Moneda: "MXN",
        ObraId: null,
        ObraNombre: null,
        Comentarios: null,
        Lineas: [new PedidoFacturableLineaInput(null, "Producto", null, null, 1m, 100m, 0m, false)]);

    [Fact]
    public async Task Validator_CanalActivoEnCatalogo_EsValido()
    {
        var validator = new CrearPedidoFacturableManualValidator(new FakeCanalesVentaReadPort(todoActivo: true));
        var resultado = await validator.ValidateAsync(ComandoValido());
        resultado.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validator_CanalInexistenteOInactivo_Falla()
    {
        // Antes: IsInEnum(). Ahora la validez la decide el catálogo — un id
        // sin fila activa (p.ej. canal desactivado en el admin) se rechaza.
        var validator = new CrearPedidoFacturableManualValidator(new FakeCanalesVentaReadPort(todoActivo: false));
        var resultado = await validator.ValidateAsync(ComandoValido(canalVenta: 99));

        resultado.IsValid.Should().BeFalse();
        resultado.Errors.Should().ContainSingle(e => e.PropertyName == "CanalVenta");
    }

    [Fact]
    public async Task Detalle_ResuelveNombreDelCanal_DesdeElCatalogo()
    {
        var empresaId = Guid.NewGuid();
        using var db = new FacturacionDbContext(
            new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

        var pedido = PedidoFacturable.ImportarDesdeAw(
            empresaId, "AW-1", Guid.NewGuid(), Guid.NewGuid(), "Cliente",
            canalVentaId: 4, comportamientoFiscal: ComportamientoFiscal.MostradorInmediato,
            moneda: "MXN", obraId: null, obraNombre: null, comentarios: null,
            versionOrigen: 1, estadoOrigen: "15");
        pedido.AgregarLinea(null, "Producto", null, null, 1m, 100m, 0m, false);
        pedido.RecalcularTotal();
        db.PedidosFacturables.Add(pedido);
        await db.SaveChangesAsync();

        var detalle = await new PedidoFacturableDetalleHandler(
                db, new FakeClientesReadPort(), new FakeProductosReadPort(),
                new FakeCanalesVentaReadPort(nombres: new Dictionary<short, string> { [4] = "CC Mérida" }),
                new FakeAlcanceCajaEvaluator())
            .Handle(new PedidoFacturableDetalleQuery(pedido.Id), CancellationToken.None);

        detalle.CanalVentaId.Should().Be((short)4);
        detalle.CanalVenta.Should().Be("CC Mérida");
    }

    [Fact]
    public async Task Lookup_ListaActivosOrdenadosPorId()
    {
        var handler = new CanalesVentaLookupHandler(new FakeCanalesVentaReadPort(
            nombres: new Dictionary<short, string>
            {
                [9] = "Planta Pintura",
                [1] = "Tienda Cancún",
            }));

        var items = await handler.Handle(new CanalesVentaLookupQuery(), CancellationToken.None);

        items.Should().HaveCount(2);
        items[0].Should().Be(new CanalVentaLookupItem(1, "Tienda Cancún"));
        items[1].Should().Be(new CanalVentaLookupItem(CanalesVentaConocidos.PlantaPintura, "Planta Pintura"));
    }
}
