using Millet.CuentasPorPagar.Application.Reportes.Comun;

namespace Millet.CuentasPorPagar.UnitTests.Reportes;

/// <summary>
/// F8-PR1: tests del cálculo de buckets de antigüedad de saldos.
/// Convención del módulo: días = fechaCorte - fechaVencimiento;
/// negativo (no vencida) cae en 0-30.
/// </summary>
public sealed class BucketsAntiguedadTests
{
    [Theory]
    [InlineData(-30, "b0_30")]
    [InlineData(-1,  "b0_30")]
    [InlineData(0,   "b0_30")]
    [InlineData(1,   "b0_30")]
    [InlineData(30,  "b0_30")]
    [InlineData(31,  "b31_60")]
    [InlineData(60,  "b31_60")]
    [InlineData(61,  "b61_90")]
    [InlineData(90,  "b61_90")]
    [InlineData(91,  "bMas90")]
    [InlineData(365, "bMas90")]
    public void CalcularBucket_clasifica_por_dias_correctamente(int dias, string bucketEsperado)
    {
        var corte = new DateOnly(2026, 5, 31);
        var vencimiento = corte.AddDays(-dias);

        var bucket = BucketsAntiguedad.CalcularBucket(vencimiento, corte);
        bucket.Should().Be(bucketEsperado);
    }

    [Fact]
    public void CalcularBucket_factura_no_vencida_cae_en_0_30()
    {
        var corte = new DateOnly(2026, 5, 31);
        var vencimientoFuturo = new DateOnly(2026, 6, 15); // 15 días en el futuro

        var bucket = BucketsAntiguedad.CalcularBucket(vencimientoFuturo, corte);
        bucket.Should().Be(BucketsAntiguedad.Bucket0a30);
    }

    [Fact]
    public void CalcularBucket_codigos_son_los_constantes()
    {
        BucketsAntiguedad.Bucket0a30.Should().Be("b0_30");
        BucketsAntiguedad.Bucket31a60.Should().Be("b31_60");
        BucketsAntiguedad.Bucket61a90.Should().Be("b61_90");
        BucketsAntiguedad.BucketMas90.Should().Be("bMas90");
    }
}
