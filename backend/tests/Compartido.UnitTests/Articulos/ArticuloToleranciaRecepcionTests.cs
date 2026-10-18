using System.Globalization;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.UnitTests.Articulos;

public sealed class ArticuloToleranciaRecepcionTests
{
    private static Articulo CrearArticulo() =>
        new(Guid.NewGuid(), "E2E-ART", "Artículo", "PZA");

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("5")]
    [InlineData("12.5")]
    [InlineData("100")]
    public void ActualizarToleranciaRecepcion_valores_validos_se_asignan(string? valor)
    {
        var porcentaje = valor is null ? (decimal?)null : decimal.Parse(valor, CultureInfo.InvariantCulture);
        var articulo = CrearArticulo();

        articulo.ActualizarToleranciaRecepcion(porcentaje);

        Assert.Equal(porcentaje, articulo.ToleranciaCantidadPorcentaje);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("100.01")]
    [InlineData("5.555")]
    public void ActualizarToleranciaRecepcion_valores_invalidos_lanzan_error(string valor)
    {
        var articulo = CrearArticulo();
        var porcentaje = decimal.Parse(valor, CultureInfo.InvariantCulture);

        var ex = Assert.Throws<BusinessRuleException>(
            () => articulo.ActualizarToleranciaRecepcion(porcentaje));

        Assert.Equal("ARTICULO_TOLERANCIA_INVALIDA", ex.Code);
        Assert.Null(articulo.ToleranciaCantidadPorcentaje);
    }
}
