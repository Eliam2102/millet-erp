using Millet.SharedKernel.Application.Integration;

namespace Millet.SharedKernel.UnitTests.Outbox;

/// <summary>
/// El ruteo de <see cref="IntegrationEventSchemaRouting"/> debe mandar cada
/// familia de eventos al schema del módulo dueño de su outbox (P9-H7). El
/// match es por prefijo más largo para desambiguar integraciones.aw / .fiscal.
/// </summary>
public sealed class IntegrationEventSchemaRoutingTests
{
    [Theory]
    [InlineData("facturacion.nota-credito.timbrada.v1", "facturacion")]
    [InlineData("facturacion.factura-venta.timbrada.v1", "facturacion")]
    [InlineData("facturacion.recibo-pago.timbrado.v1", "facturacion")]
    [InlineData("compras.orden-compra.autorizada.v1", "compras")]
    [InlineData("almacen.oc_recepcion.registrada.v1", "almacen")]
    [InlineData("cuentas_por_pagar.factura.pago-aplicado.v1", "cuentas_por_pagar")]
    [InlineData("cuentas_por_cobrar.propuesta-aplicacion.creada.v1", "cuentas_por_cobrar")]
    [InlineData("tesoreria.pago-factura-proveedor.aplicado.v1", "tesoreria")]
    [InlineData("integraciones.aw.cotizacion.recibida.v1", "integraciones_aw")]
    [InlineData("integraciones.fiscal.configuracion-actualizada.v1", "integraciones_fiscal")]
    public void Resuelve_ElSchemaDelModuloDueño(string eventType, string schemaEsperado)
    {
        var ok = IntegrationEventSchemaRouting.TryResolveSchema(eventType, out var schema);

        ok.Should().BeTrue();
        schema.Should().Be(schemaEsperado);
    }

    [Theory]
    [InlineData("admin.empresa.creada.v1")]
    [InlineData("identidad.usuario.rol.asignado.v1")]
    [InlineData("test.compras.requisicion.autorizada.v1")]
    public void NoResuelve_LosModulosSinOutboxPropio(string eventType)
    {
        var ok = IntegrationEventSchemaRouting.TryResolveSchema(eventType, out var schema);

        ok.Should().BeFalse();
        schema.Should().BeEmpty();
    }

    [Fact]
    public void Integraciones_Aw_Y_Fiscal_NoColisionan_PorPrefijoMasLargo()
    {
        IntegrationEventSchemaRouting.TryResolveSchema("integraciones.aw.pedido.correlacionado.v1", out var aw);
        IntegrationEventSchemaRouting.TryResolveSchema("integraciones.fiscal.configuracion-actualizada.v1", out var fiscal);

        aw.Should().Be("integraciones_aw");
        fiscal.Should().Be("integraciones_fiscal");
    }

    [Fact]
    public void EventTypeVacio_Lanza()
    {
        var act = () => IntegrationEventSchemaRouting.TryResolveSchema("  ", out _);

        act.Should().Throw<ArgumentException>();
    }
}
