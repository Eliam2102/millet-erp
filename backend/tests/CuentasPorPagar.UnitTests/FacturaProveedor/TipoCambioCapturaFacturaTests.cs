using Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

/// <summary>M-080: el TC capturado manda dentro de la tolerancia de M-207; fuera pide confirmación con permiso.</summary>
public sealed class TipoCambioCapturaFacturaTests
{
    private static readonly DateOnly Dia = new(2026, 10, 16);

    [Theory]
    [InlineData(18.40, 18.25)] // 0.82 % con tolerancia 1 %
    [InlineData(18.25, 18.25)]
    [InlineData(18.07, 18.25)] // -0.99 %
    public void Dentro_de_tolerancia_usa_el_capturado_sin_confirmar(double capturado, double catalogo)
    {
        var d = TipoCambioCapturaFactura.Decidir((decimal)capturado, (decimal)catalogo, 1m, false, false, Dia);
        Assert.Equal((decimal)capturado, d.Valor);
        Assert.False(d.Confirmado);
    }

    [Fact]
    public void Fuera_de_tolerancia_sin_confirmar_es_422()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => TipoCambioCapturaFactura.Decidir(19.50m, 18.25m, 1m, false, true, Dia));
        Assert.Equal("TIPO_CAMBIO_REQUIERE_CONFIRMACION", ex.Code);
    }

    [Fact]
    public void Confirmar_sin_permiso_es_403()
    {
        var ex = Assert.Throws<ForbiddenException>(() => TipoCambioCapturaFactura.Decidir(19.50m, 18.25m, 1m, true, false, Dia));
        Assert.Equal("TIPO_CAMBIO_CONFIRMACION_SIN_PERMISO", ex.Code);
    }

    [Fact]
    public void Confirmar_con_permiso_usa_el_capturado_y_queda_marcado()
    {
        var d = TipoCambioCapturaFactura.Decidir(19.50m, 18.25m, 1m, true, true, Dia);
        Assert.Equal(19.50m, d.Valor);
        Assert.True(d.Confirmado);
    }

    [Fact]
    public void Sin_catalogo_o_sin_tolerancia_no_compara()
    {
        Assert.Equal(25m, TipoCambioCapturaFactura.Decidir(25m, null, 1m, false, false, Dia).Valor);
        Assert.Equal(25m, TipoCambioCapturaFactura.Decidir(25m, 18.25m, 0m, false, false, Dia).Valor);
    }
}
