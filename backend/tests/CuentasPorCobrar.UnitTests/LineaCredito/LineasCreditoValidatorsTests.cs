using Millet.CuentasPorCobrar.Application.LineasCredito;
using Millet.CuentasPorCobrar.Domain.LineaCredito;

namespace Millet.CuentasPorCobrar.UnitTests.LineaCredito;

public class LineasCreditoValidatorsTests
{
    [Fact]
    public void CrearLineaCreditoValidator_acepta_comando_valido()
    {
        var validator = new CrearLineaCreditoValidator();
        var result = validator.Validate(new CrearLineaCreditoCommand(
            Guid.NewGuid(), "MXN", 500_000m, OrigenLineaCredito.Solunion, 45));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CrearLineaCreditoValidator_rechaza_limite_cero_y_plazo_fuera_de_rango()
    {
        var validator = new CrearLineaCreditoValidator();
        var result = validator.Validate(new CrearLineaCreditoCommand(
            Guid.Empty, "MX", 0m, (OrigenLineaCredito)99, 400));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.PropertyName).Should().Contain(
            ["ClienteId", "Moneda", "Limite", "Origen", "PlazoDias"]);
    }

    [Fact]
    public void BloquearLineaCreditoValidator_rechaza_motivo_vacio()
    {
        var validator = new BloquearLineaCreditoValidator();
        var result = validator.Validate(new BloquearLineaCreditoCommand(Guid.NewGuid(), 1, ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Motivo");
    }

    [Fact]
    public void ActualizarLineaCreditoValidator_rechaza_plazo_mayor_a_365()
    {
        var validator = new ActualizarLineaCreditoValidator();
        var result = validator.Validate(new ActualizarLineaCreditoCommand(Guid.NewGuid(), 1, 100m, 366));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "PlazoDias");
    }
}
