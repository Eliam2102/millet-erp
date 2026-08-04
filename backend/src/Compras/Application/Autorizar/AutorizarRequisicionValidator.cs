using FluentValidation;
using Millet.Compras.Domain;

namespace Millet.Compras.Application.Autorizar;

public sealed class AutorizarRequisicionValidator : AbstractValidator<AutorizarRequisicionCommand>
{
    public AutorizarRequisicionValidator()
    {
        RuleFor(c => c.RequisicionId).NotEmpty().WithErrorCode("REQUISICION_REQUERIDA");

        RuleFor(c => c.Nivel)
            .Must(n => n == NivelAutorizacion.Nivel1 || n == NivelAutorizacion.Nivel2)
            .WithErrorCode("NIVEL_INVALIDO");

        RuleFor(c => c.Notas)
            .MaximumLength(500).WithErrorCode("NOTAS_DEMASIADO_LARGAS")
            .When(c => c.Notas is not null);
    }
}
