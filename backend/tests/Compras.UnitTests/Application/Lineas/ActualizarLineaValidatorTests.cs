using Millet.Compras.Application.Lineas.ActualizarLinea;

namespace Millet.Compras.UnitTests.Application.Lineas;

/// <summary>
/// Fase E PR2.1: el CC-Máquina es obligatorio también al EDITAR una línea de RQ.
/// El comando es replace completo (el FE prellenar con el CC actual), así que
/// editar una línea histórica sin CC obliga a elegir uno.
/// </summary>
public class ActualizarLineaValidatorTests
{
    private readonly ActualizarLineaValidator _validator = new();

    private static readonly Guid CcValido = Guid.Parse("0c000000-0000-0000-0000-000000000001");

    private static ActualizarLineaCommand Valid() =>
        new(
            RequisicionId: Guid.CreateVersion7(),
            LineaId: Guid.CreateVersion7(),
            ArticuloId: Guid.CreateVersion7(),
            Cantidad: 10m,
            UnidadMedida: "PZA",
            PrecioEstimadoMonto: 15.50m,
            PrecioEstimadoMoneda: "MXN",
            CentroCostoId: CcValido);

    [Fact]
    public void Should_PassValidation_When_CommandIsValid()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Should_FailValidation_When_CentroCostoIdIsNull()
    {
        var result = _validator.Validate(Valid() with { CentroCostoId = null });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "LINEA_RQ_CENTRO_COSTO_REQUERIDO");
    }
}
