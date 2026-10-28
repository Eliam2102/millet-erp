using Millet.CuentasPorCobrar.Domain.Liberacion;

namespace Millet.CuentasPorCobrar.UnitTests.Liberacion;

/// <summary>
/// C7.3 (ClickUp 86e3jvv6n) — cálculo puro de la cobertura de crédito:
/// crédito DISPONIBLE ÷ monto del pedido. Bordes de los supuestos C7.3
/// (a) 50 % exacto = pendiente y (b) truncado a 2 decimales.
/// </summary>
public sealed class CoberturaCreditoTests
{
    [Theory]
    [InlineData(40_000, 60_000, 66.66)]   // CA7.2: 66.666… → 66.66 (trunca, no redondea)
    [InlineData(30_000, 100_000, 30.00)]  // CA7.3
    [InlineData(50_000, 100_000, 50.00)]
    [InlineData(99_990, 100_000, 99.99)]
    [InlineData(100_000, 100_000, 100.00)]
    [InlineData(2, 3, 66.66)]
    public void Porcentaje_trunca_a_dos_decimales(decimal disponible, decimal monto, decimal esperado)
    {
        CoberturaCredito.Porcentaje(disponible, monto).Should().Be(esperado);
    }

    [Fact]
    public void Porcentaje_se_acota_entre_0_y_100()
    {
        CoberturaCredito.Porcentaje(-5_000m, 10_000m).Should().Be(0m);
        CoberturaCredito.Porcentaje(1_000_000m, 10m).Should().Be(100m);
    }

    [Fact]
    public void Porcentaje_de_99_999_no_se_redondea_a_100()
    {
        CoberturaCredito.Porcentaje(99_999.99m, 100_000m).Should().Be(99.99m);
    }

    [Theory]
    [InlineData(100_000, 100_000, TramoCobertura.Plena)]
    [InlineData(150_000, 100_000, TramoCobertura.Plena)]
    [InlineData(99_990, 100_000, TramoCobertura.Parcial)]      // 99.99 % → pendiente
    [InlineData(99_999.99, 100_000, TramoCobertura.Parcial)]
    [InlineData(40_000, 60_000, TramoCobertura.Parcial)]       // 66 %
    [InlineData(50_000, 100_000, TramoCobertura.Parcial)]      // supuesto (a): 50 % exacto
    [InlineData(49_999.99, 100_000, TramoCobertura.Insuficiente)]
    [InlineData(30_000, 100_000, TramoCobertura.Insuficiente)] // CA7.3
    [InlineData(-1_000, 100_000, TramoCobertura.Insuficiente)]
    public void Evaluar_clasifica_en_tramos(decimal disponible, decimal monto, TramoCobertura esperado)
    {
        CoberturaCredito.Evaluar(disponible, monto).Should().Be(esperado);
    }

    [Fact]
    public void PorcentajeParaMostrar_trunca_a_entero()
    {
        // Supuesto (b): 66.66 se muestra «66 %».
        CoberturaCredito.PorcentajeEntero(66.66m).Should().Be(66);
        CoberturaCredito.PorcentajeEntero(99.99m).Should().Be(99);
    }
}
