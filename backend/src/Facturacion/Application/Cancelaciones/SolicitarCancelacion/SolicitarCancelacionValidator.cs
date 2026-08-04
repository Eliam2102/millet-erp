using FluentValidation;

namespace Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;

/// <summary>Validación estructural (FluentValidation). El motivo 01 exige sustituto — lo valida el dominio.</summary>
public sealed class SolicitarCancelacionValidator : AbstractValidator<SolicitarCancelacionCommand>
{
    private static readonly string[] MotivosValidos = ["01", "02", "03", "04"];

    public SolicitarCancelacionValidator()
    {
        RuleFor(c => c.ComprobanteId).NotEmpty();
        RuleFor(c => c.MotivoSat)
            .NotEmpty()
            .Must(m => MotivosValidos.Contains(m))
            .WithMessage("El motivo SAT debe ser 01, 02, 03 o 04.");
        RuleFor(c => c.UuidSustituto).MaximumLength(36);
    }
}
