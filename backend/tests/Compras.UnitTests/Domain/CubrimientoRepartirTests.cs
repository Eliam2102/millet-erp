using Millet.Compras.Domain;

namespace Millet.Compras.UnitTests.Domain;

/// <summary>
/// Unit de la función pura <see cref="Cubrimiento.Repartir"/> — fuente única
/// del reparto disponible→(almacén, compra), compartida por la bifurcación
/// real y por el preview read-only (PR-C). Algoritmo conservador: usa todo el
/// disponible hasta el tope de la cantidad; el resto es saldo para compra.
/// (InlineData usa <c>int</c> porque <c>decimal</c> no es constante de
/// atributo; los valores se convierten a decimal en la llamada/aserción.)
/// </summary>
public class CubrimientoRepartirTests
{
    [Theory]
    // (disponible, cantidad) → (esperadoAlmacen, esperadoCompra)
    [InlineData(100, 5, 5, 0)]   // disponible >= cantidad → todo de almacén
    [InlineData(5, 5, 5, 0)]     // disponible == cantidad → exacto
    [InlineData(0, 5, 0, 5)]     // sin disponible → todo a compra
    [InlineData(3, 5, 3, 2)]     // parcial → split
    [InlineData(-10, 5, 0, 5)]   // disponible negativo (defensivo) → 0 de almacén
    public void Repartir_DistribuyeDisponibleHastaElTope(
        int disponible, int cantidad, int esperadoAlmacen, int esperadoCompra)
    {
        var (deAlmacen, deCompra) = Cubrimiento.Repartir(disponible, cantidad);

        Assert.Equal((decimal)esperadoAlmacen, deAlmacen);
        Assert.Equal((decimal)esperadoCompra, deCompra);
        // Invariante: el reparto reconstruye exactamente la cantidad.
        Assert.Equal((decimal)cantidad, deAlmacen + deCompra);
    }
}
