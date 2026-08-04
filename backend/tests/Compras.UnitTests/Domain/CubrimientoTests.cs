using Millet.Compras.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Domain;

public class CubrimientoTests
{
    [Fact]
    public void Should_Create_When_ValuesAreValid()
    {
        var c = new Cubrimiento(cantidadOriginal: 10m, cantidadDeAlmacen: 4m, cantidadDeCompra: 6m, cantidadRecibida: 3m);

        Assert.Equal(10m, c.CantidadOriginal);
        Assert.Equal(4m, c.CantidadDeAlmacen);
        Assert.Equal(6m, c.CantidadDeCompra);
        Assert.Equal(3m, c.CantidadRecibida);
    }

    [Fact]
    public void Should_ComputePendiente_AsOriginalMinusAlmacenMinusRecibida()
    {
        var c = new Cubrimiento(10m, 4m, 6m, 3m);

        // 10 - 4 - 3 = 3
        Assert.Equal(3m, c.CantidadPendiente);
    }

    [Fact]
    public void Inicial_Should_Return_AllZerosExceptOriginal()
    {
        var c = Cubrimiento.Inicial(10m);

        Assert.Equal(10m, c.CantidadOriginal);
        Assert.Equal(0m, c.CantidadDeAlmacen);
        Assert.Equal(0m, c.CantidadDeCompra);
        Assert.Equal(0m, c.CantidadRecibida);
        Assert.Equal(10m, c.CantidadPendiente);
        Assert.False(c.TieneCubrimiento);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Should_Throw_When_CantidadOriginalIsNotPositive(decimal valor)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => new Cubrimiento(valor, 0m, 0m, 0m));

        Assert.Equal("CUBRIMIENTO_CANTIDAD_ORIGINAL_INVALIDA", ex.Code);
    }

    [Fact]
    public void Should_Throw_When_CantidadDeAlmacenIsNegative()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => new Cubrimiento(10m, -1m, 0m, 0m));

        Assert.Equal("CUBRIMIENTO_CANTIDAD_NEGATIVA", ex.Code);
    }

    [Fact]
    public void Should_Throw_When_CantidadDeCompraIsNegative()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => new Cubrimiento(10m, 0m, -1m, 0m));

        Assert.Equal("CUBRIMIENTO_CANTIDAD_NEGATIVA", ex.Code);
    }

    [Fact]
    public void Should_Throw_When_CantidadRecibidaIsNegative()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => new Cubrimiento(10m, 0m, 5m, -1m));

        Assert.Equal("CUBRIMIENTO_CANTIDAD_NEGATIVA", ex.Code);
    }

    [Fact]
    public void Should_Throw_When_AlmacenPlusCompraExceedsOriginal()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => new Cubrimiento(10m, 6m, 5m, 0m));

        Assert.Equal("CUBRIMIENTO_EXCEDE_ORIGINAL", ex.Code);
    }

    [Fact]
    public void Should_Throw_When_RecibidaExceedsCompra()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => new Cubrimiento(10m, 0m, 5m, 6m));

        Assert.Equal("CUBRIMIENTO_RECIBIDA_EXCEDE_COMPRA", ex.Code);
    }

    [Fact]
    public void Should_AcceptBoundary_AlmacenPlusCompraEqualsOriginal()
    {
        // Caso: todo cubierto entre almacén y compra.
        var c = new Cubrimiento(10m, 4m, 6m, 0m);

        Assert.Equal(10m, c.CantidadOriginal);
        Assert.True(c.TieneCubrimiento);
    }

    [Fact]
    public void Should_AcceptBoundary_RecibidaEqualsCompra()
    {
        // Caso: todo lo de compra ya recibido.
        var c = new Cubrimiento(10m, 0m, 5m, 5m);

        Assert.Equal(5m, c.CantidadRecibida);
    }

    [Fact]
    public void TieneCubrimiento_Should_BeTrue_When_AlmacenOrCompraGreaterThanZero()
    {
        Assert.True(new Cubrimiento(10m, 1m, 0m, 0m).TieneCubrimiento);
        Assert.True(new Cubrimiento(10m, 0m, 1m, 0m).TieneCubrimiento);
        Assert.False(new Cubrimiento(10m, 0m, 0m, 0m).TieneCubrimiento);
    }
}
