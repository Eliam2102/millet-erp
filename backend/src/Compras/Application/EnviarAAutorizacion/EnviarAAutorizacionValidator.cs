using FluentValidation;

namespace Millet.Compras.Application.EnviarAAutorizacion;

public sealed class EnviarAAutorizacionValidator : AbstractValidator<EnviarAAutorizacionCommand>
{
    public EnviarAAutorizacionValidator()
    {
        RuleFor(c => c.RequisicionId).NotEmpty().WithErrorCode("REQUISICION_REQUERIDA");
    }
}
