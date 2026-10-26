using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.SharedKernel.UnitTests.Persistence;

/// <summary>86e3tfdx8 — normalización de la búsqueda parcial por palabras.</summary>
public class BusquedaPorPalabrasTests
{
    [Theory]
    [InlineData("Carlos Pérez", new[] { "carlos", "perez" })]
    [InlineData("  PÉREZ   GARCÍA\tCARLOS ", new[] { "perez", "garcia", "carlos" })]
    [InlineData("Muñoz Güemes", new[] { "munoz", "guemes" })]
    [InlineData("carlos CARLOS", new[] { "carlos" })]
    [InlineData("MID-001", new[] { "mid-001" })]
    public void Normalizar_separa_en_palabras_sin_acentos_ni_mayusculas(string texto, string[] esperado)
    {
        Assert.Equal(esperado, BusquedaPorPalabras.Normalizar(texto));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalizar_vacio_no_filtra(string? texto)
    {
        Assert.Empty(BusquedaPorPalabras.Normalizar(texto));
    }
}
