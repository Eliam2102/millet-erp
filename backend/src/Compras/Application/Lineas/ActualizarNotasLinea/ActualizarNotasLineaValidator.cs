using FluentValidation;

namespace Millet.Compras.Application.Lineas.ActualizarNotasLinea;

public sealed class ActualizarNotasLineaValidator : AbstractValidator<ActualizarNotasLineaCommand>
{
    public ActualizarNotasLineaValidator()
    {
        RuleFor(c => c.RequisicionId).NotEmpty().WithErrorCode("REQUISICION_REQUERIDA");
        RuleFor(c => c.LineaId).NotEmpty().WithErrorCode("LINEA_REQUERIDA");

        RuleFor(c => c.Notas)
            .MaximumLength(500).WithErrorCode("NOTAS_DEMASIADO_LARGAS")
            .When(c => c.Notas is not null);
    }
}
