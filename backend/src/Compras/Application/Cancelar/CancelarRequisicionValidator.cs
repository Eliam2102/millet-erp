using FluentValidation;

namespace Millet.Compras.Application.Cancelar;

public sealed class CancelarRequisicionValidator : AbstractValidator<CancelarRequisicionCommand>
{
    public CancelarRequisicionValidator()
    {
        RuleFor(c => c.RequisicionId).NotEmpty().WithErrorCode("REQUISICION_REQUERIDA");
        RuleFor(c => c.MotivoId).NotEmpty().WithErrorCode("MOTIVO_REQUERIDO");

        RuleFor(c => c.MotivoTexto)
            .MaximumLength(500).WithErrorCode("MOTIVO_TEXTO_DEMASIADO_LARGO")
            .When(c => c.MotivoTexto is not null);
    }
}
