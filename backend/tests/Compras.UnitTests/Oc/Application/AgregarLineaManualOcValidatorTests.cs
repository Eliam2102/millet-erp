using Millet.Compras.Application.Oc.Lineas.AgregarLineaManual;

namespace Millet.Compras.UnitTests.Oc.Application;

/// <summary>
/// Tests del FluentValidation de <see cref="AgregarLineaManualOcCommand"/>.
/// Foco de Fase E PR3.1: el CC-Máquina pasa de opcional a REQUERIDO en la
/// línea manual de OC (el comprador lo elige por proxy con el selector
/// abierto). La línea heredada de RQ no pasa por este comando — su CC viene
/// 1:1 de la requisición, que ya lo exige desde PR2.1 (ADR-0050).
/// </summary>
public class AgregarLineaManualOcValidatorTests
{
    private readonly AgregarLineaManualOcValidator _validator = new();

    private static AgregarLineaManualOcCommand Valid() =>
        new(
            OrdenCompraId: Guid.CreateVersion7(),
            ArticuloId: Guid.CreateVersion7(),
            Cantidad: 10m,
            UnidadMedida: "PZA",
            PrecioUnitario: 100m,
            DepartamentoSolicitanteId: Guid.CreateVersion7(),
            CentroCostoId: Guid.CreateVersion7());

    [Fact]
    public void Should_PassValidation_When_CommandIsValid()
    {
        var result = _validator.Validate(Valid());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_FailValidation_When_CentroCostoIsNull()
    {
        var result = _validator.Validate(Valid() with { CentroCostoId = null });

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            e => e.ErrorCode == "LINEA_OC_MANUAL_CENTRO_COSTO_REQUERIDO");
    }

    [Fact]
    public void Should_PassValidation_When_CentroCostoIsPresent()
    {
        var cc = Guid.CreateVersion7();
        var result = _validator.Validate(Valid() with { CentroCostoId = cc });

        Assert.True(result.IsValid);
        Assert.DoesNotContain(
            result.Errors,
            e => e.ErrorCode == "LINEA_OC_MANUAL_CENTRO_COSTO_REQUERIDO");
    }
}
