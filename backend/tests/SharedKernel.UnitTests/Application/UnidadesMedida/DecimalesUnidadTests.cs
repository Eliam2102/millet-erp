using System.Globalization;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.SharedKernel.UnitTests.Application.UnidadesMedida;

/// <summary>
/// Cubre el validador puro <see cref="DecimalesUnidad"/> (ADR-0046 Etapa 2):
/// una cantidad cabe en N decimales sii <c>Round(cantidad, N) == cantidad</c>.
/// </summary>
public class DecimalesUnidadTests
{
    [Theory]
    // pieza (0 decimales): enteros válidos, fracciones inválidas.
    [InlineData("2", 0, true)]
    [InlineData("2.0", 0, true)]      // 2.0 == 2 cabe en 0 decimales
    [InlineData("1.5", 0, false)]
    [InlineData("1", 0, true)]
    // kg (3 decimales)
    [InlineData("1.5", 3, true)]
    [InlineData("1.250", 3, true)]
    [InlineData("1.2505", 3, false)]
    [InlineData("0.001", 3, true)]
    [InlineData("0.0001", 3, false)]
    // ceros a la derecha: 1.50 cabe en >=1 decimal (no se cuenta el string).
    [InlineData("1.50", 1, true)]
    [InlineData("1.50", 2, true)]
    [InlineData("1.500000", 1, true)]
    public void EsValida_RespetaDecimalesDeLaUnidad(string cantidad, int decimales, bool esperado)
    {
        var valor = decimal.Parse(cantidad, CultureInfo.InvariantCulture);

        DecimalesUnidad.EsValida(valor, decimales).Should().Be(esperado);
    }

    [Fact]
    public void CodigoError_EsElCanonico()
    {
        DecimalesUnidad.CodigoError.Should().Be("CANTIDAD_DECIMALES_EXCEDE_UNIDAD");
    }
}
