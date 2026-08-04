using Millet.Compras.Application.Lineas.ActualizarNotasLinea;

namespace Millet.Compras.UnitTests.Application.Lineas;

public class ActualizarNotasLineaValidatorTests
{
    private readonly ActualizarNotasLineaValidator _validator = new();

    [Fact]
    public void Should_PassValidation_When_NotasIsNull()
    {
        var result = _validator.Validate(new ActualizarNotasLineaCommand(
            RequisicionId: Guid.CreateVersion7(),
            LineaId: Guid.CreateVersion7(),
            Notas: null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_PassValidation_When_NotasAtBoundary()
    {
        var result = _validator.Validate(new ActualizarNotasLineaCommand(
            RequisicionId: Guid.CreateVersion7(),
            LineaId: Guid.CreateVersion7(),
            Notas: new string('x', 500)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_FailValidation_When_NotasTooLong()
    {
        var result = _validator.Validate(new ActualizarNotasLineaCommand(
            RequisicionId: Guid.CreateVersion7(),
            LineaId: Guid.CreateVersion7(),
            Notas: new string('x', 501)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "NOTAS_DEMASIADO_LARGAS");
    }

    [Fact]
    public void Should_FailValidation_When_RequisicionIdIsEmpty()
    {
        var result = _validator.Validate(new ActualizarNotasLineaCommand(
            RequisicionId: Guid.Empty,
            LineaId: Guid.CreateVersion7(),
            Notas: null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "REQUISICION_REQUERIDA");
    }

    [Fact]
    public void Should_FailValidation_When_LineaIdIsEmpty()
    {
        var result = _validator.Validate(new ActualizarNotasLineaCommand(
            RequisicionId: Guid.CreateVersion7(),
            LineaId: Guid.Empty,
            Notas: null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "LINEA_REQUERIDA");
    }
}
