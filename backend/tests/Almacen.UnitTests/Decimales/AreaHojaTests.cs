using Millet.Almacen.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Decimales;

/// <summary>F1-MP-01 S1 (E07 D6): m² por hoja redondeado a 4 decimales, por hoja.</summary>
public sealed class AreaHojaTests
{
    [Theory]
    [InlineData(2440, 3660, 8.9304)]
    [InlineData(2140, 3300, 7.0620)]
    [InlineData(1830, 2440, 4.4652)]
    public void Calcula_M2_Por_Hoja(int ancho, int alto, double esperado) =>
        Assert.Equal((decimal)esperado, AreaHoja.M2(ancho, alto));

    [Fact]
    public void Redondea_Por_Hoja_A_4_Decimales() =>
        Assert.Equal(1.0000m, AreaHoja.M2(1000.04m, 999.98m)); // 1.00002...

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, -1)]
    public void Rechaza_Medidas_No_Positivas(int ancho, int alto) =>
        Assert.Equal("MEDIDA_HOJA_INVALIDA", Assert.Throws<BusinessRuleException>(() => AreaHoja.M2(ancho, alto)).Code);
}
