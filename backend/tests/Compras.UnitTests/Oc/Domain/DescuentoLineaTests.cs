using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests de los VOs <see cref="DescuentoLinea"/> y
/// <see cref="DescuentoGlobal"/> (diseño §4.8).
/// </summary>
public class DescuentoLineaTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(50.5)]
    [InlineData(100)]
    public void DescuentoLinea_Porcentaje_RangoValido_OK(decimal valor)
    {
        var d = new DescuentoLinea(DescuentoTipo.Porcentaje, valor);
        Assert.Equal(DescuentoTipo.Porcentaje, d.Tipo);
        Assert.Equal(valor, d.Valor);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-100)]
    [InlineData(100.01)]
    [InlineData(150)]
    public void DescuentoLinea_Porcentaje_FueraDeRango_Lanza(decimal valor)
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            new DescuentoLinea(DescuentoTipo.Porcentaje, valor));
        Assert.Equal("DESCUENTO_PORCENTAJE_INVALIDO", ex.Code);
    }

    [Fact]
    public void DescuentoLinea_Monto_Negativo_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            new DescuentoLinea(DescuentoTipo.Monto, -1m));
        Assert.Equal("DESCUENTO_MONTO_INVALIDO", ex.Code);
    }

    [Fact]
    public void DescuentoLinea_Cero_ProduceMontoCero()
    {
        var d = DescuentoLinea.Cero;
        Assert.Equal(DescuentoTipo.Monto, d.Tipo);
        Assert.Equal(0m, d.Valor);
    }

    [Theory]
    [InlineData(100, 10, 10)]   // 10% de 100 = 10
    [InlineData(1000, 16, 160)] // 16% de 1000 = 160
    [InlineData(0, 50, 0)]
    [InlineData(-50, 10, 0)]    // subtotal negativo → sin descuento
    public void DescuentoLinea_Porcentaje_AplicaCorrectamente(decimal subtotal, decimal porcentaje, decimal esperado)
    {
        var d = new DescuentoLinea(DescuentoTipo.Porcentaje, porcentaje);
        Assert.Equal(esperado, d.Aplicar(subtotal));
    }

    [Theory]
    [InlineData(100, 25, 25)]      // monto fijo
    [InlineData(100, 150, 100)]    // monto > subtotal → capa al subtotal
    [InlineData(0, 50, 0)]         // subtotal 0 → 0
    public void DescuentoLinea_Monto_AplicaCorrectamente(decimal subtotal, decimal monto, decimal esperado)
    {
        var d = new DescuentoLinea(DescuentoTipo.Monto, monto);
        Assert.Equal(esperado, d.Aplicar(subtotal));
    }

    // --- DescuentoGlobal: mismas reglas ---

    [Fact]
    public void DescuentoGlobal_Porcentaje_FueraDeRango_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            new DescuentoGlobal(DescuentoTipo.Porcentaje, 101m));
        Assert.Equal("DESCUENTO_GLOBAL_PORCENTAJE_INVALIDO", ex.Code);
    }

    [Fact]
    public void DescuentoGlobal_Monto_Negativo_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            new DescuentoGlobal(DescuentoTipo.Monto, -10m));
        Assert.Equal("DESCUENTO_GLOBAL_MONTO_INVALIDO", ex.Code);
    }

    [Fact]
    public void DescuentoGlobal_Porcentaje_AplicaSobreSubtotalAntesDescuento()
    {
        var d = new DescuentoGlobal(DescuentoTipo.Porcentaje, 5m);
        Assert.Equal(50m, d.Aplicar(1000m));
    }
}
