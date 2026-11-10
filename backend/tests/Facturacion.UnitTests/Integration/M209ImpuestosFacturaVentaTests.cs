using Millet.Facturacion.Application.Integration;

namespace Millet.Facturacion.UnitTests.Integration;

/// <summary>
/// M-209 (86e3zr7mp): la factura de venta manda a Contabilidad la base y el IVA por tratamiento de sus líneas (ObjetoImp y tasa de
/// traslado), para separar el IVA trasladado por tasa (p. ej. 8 % en zona fronteriza) y dejar las bases para la DIOT.
/// </summary>
public sealed class M209ImpuestosFacturaVentaTests
{
    [Theory]
    [InlineData("01", 0.16, "NOOBJETO")]
    [InlineData("03", null, "NOOBJETO")]
    [InlineData("02", null, "EXENTO")]
    [InlineData("02", 0.16, "TASA16")]
    [InlineData("02", 0.08, "TASA8")]
    [InlineData("02", 0.0, "TASA0")]
    [InlineData("02", 0.11, null)]
    public void Clasifica_cada_linea_por_objeto_de_impuesto_y_tasa(string objetoImp, double? tasa, string? esperado) =>
        EventosContablesFacturacion.Tratamiento(objetoImp, (decimal?)tasa).Should().Be(esperado);
}
