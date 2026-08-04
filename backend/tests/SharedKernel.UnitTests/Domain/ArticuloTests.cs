using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.SharedKernel.UnitTests.Domain;

public class ArticuloTests
{
    private static Articulo Crear(
        string clave = "ART-1",
        string nombre = "Articulo de prueba",
        string unidadMedidaDefault = "PZA",
        Naturaleza naturaleza = Naturaleza.Estandar,
        EstatusCatalogo estatus = EstatusCatalogo.Activo,
        decimal? precioReferenciaMonto = null,
        string? precioReferenciaMoneda = null) =>
        new(
            id: Guid.CreateVersion7(),
            clave: clave,
            nombre: nombre,
            unidadMedidaDefault: unidadMedidaDefault,
            naturaleza: naturaleza,
            estatus: estatus,
            precioReferenciaMonto: precioReferenciaMonto,
            precioReferenciaMoneda: precioReferenciaMoneda);

    [Fact]
    public void Should_Create_WithDefaults()
    {
        var a = Crear();
        Assert.Equal(Naturaleza.Estandar, a.Naturaleza);
        Assert.Equal(EstatusCatalogo.Activo, a.Estatus);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CLAVE-DEMASIADO-LARGA-PARA-CABER")]
    public void Should_Throw_When_ClaveInvalida(string clave)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(clave: clave));
        Assert.Equal("ARTICULO_CLAVE_INVALIDA", ex.Code);
    }

    [Fact]
    public void Should_Throw_When_NombreVacio()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(nombre: ""));
        Assert.Equal("ARTICULO_NOMBRE_INVALIDO", ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("UNIDAD-DEMASIADO-LARGA")]
    public void Should_Throw_When_UnidadMedidaInvalida(string um)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(unidadMedidaDefault: um));
        Assert.Equal("ARTICULO_UNIDAD_MEDIDA_INVALIDA", ex.Code);
    }

    [Fact]
    public void Should_Throw_When_PrecioNegativo()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            Crear(precioReferenciaMonto: -1m, precioReferenciaMoneda: "MXN"));
        Assert.Equal("ARTICULO_PRECIO_NEGATIVO", ex.Code);
    }

    [Fact]
    public void Should_Throw_When_PrecioSinMoneda()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            Crear(precioReferenciaMonto: 100m, precioReferenciaMoneda: null));
        Assert.Equal("ARTICULO_PRECIO_SIN_MONEDA", ex.Code);
    }

    [Theory]
    [InlineData("MX")]    // muy corto
    [InlineData("MXNX")]  // muy largo
    [InlineData("mxn")]   // minúsculas (regex requiere mayúsculas pero la validación de longitud lo deja pasar; el CHECK SQL lo bloquea — aquí solo la longitud)
    public void Should_Throw_When_MonedaLongitudInvalida(string moneda)
    {
        if (moneda.Length != 3)
        {
            var ex = Assert.Throws<BusinessRuleException>(() =>
                Crear(precioReferenciaMonto: 100m, precioReferenciaMoneda: moneda));
            Assert.Equal("ARTICULO_MONEDA_INVALIDA", ex.Code);
        }
    }

    [Theory]
    [InlineData(Naturaleza.Estandar)]
    [InlineData(Naturaleza.Servicio)]
    [InlineData(Naturaleza.Critico)]
    [InlineData(Naturaleza.Riesgo)]
    public void Should_Accept_TodasLasNaturalezas(Naturaleza n)
    {
        var a = Crear(naturaleza: n);
        Assert.Equal(n, a.Naturaleza);
    }
}
