using Millet.Almacen.Application.Reportes;

namespace Millet.Almacen.UnitTests.Reportes;

public sealed class AlfakValuacionAlCorteTests
{
    private static (bool, decimal, decimal) L(bool entrada, decimal q, decimal c) => (entrada, q, c);

    [Fact]
    public void SinMovimientosPosteriores_IgualAlCalculoActual()
    {
        var (valor, costo) = AlfakHistorialAlmacenHandler.ValorizarAlCorte(20m, 19000m, 950m, []);
        Assert.Equal(19000m, valor);
        Assert.Equal(950m, costo);
    }

    [Fact]
    public void EntradaPosteriorMasCara_NoCambiaElValorPasado()
    {
        var (valor, costo) = AlfakHistorialAlmacenHandler.ValorizarAlCorte(10m, 19000m, 950m, [L(true, 10m, 1000m)]);
        Assert.Equal(9000m, valor);
        Assert.Equal(900m, costo);
    }

    [Fact]
    public void SalidaPosterior_SeSumaDeVuelta()
    {
        // Hoy quedan 6 @ 100 = 600; después del corte salieron 4 @ 100 → al corte había 10 = 1,000.
        var (valor, costo) = AlfakHistorialAlmacenHandler.ValorizarAlCorte(10m, 600m, 100m, [L(false, 4m, 100m)]);
        Assert.Equal(1000m, valor);
        Assert.Equal(100m, costo);
    }

    [Fact]
    public void SaldoFinalCero_UsaCostoActualYValorCero()
    {
        var (valor, costo) = AlfakHistorialAlmacenHandler.ValorizarAlCorte(0m, 1000m, 100m, [L(true, 10m, 100m)]);
        Assert.Equal(0m, valor);
        Assert.Equal(100m, costo);
    }

    [Fact]
    public void Redondea_ValorADosDecimales_CostoACuatro()
    {
        var (valor, costo) = AlfakHistorialAlmacenHandler.ValorizarAlCorte(3m, 10.00m, 3m, [L(true, 1m, 0.003m)]);
        Assert.Equal(10m, valor); // 9.997 → 10.00
        Assert.Equal(3.3323m, costo); // 9.997 / 3
    }
}
