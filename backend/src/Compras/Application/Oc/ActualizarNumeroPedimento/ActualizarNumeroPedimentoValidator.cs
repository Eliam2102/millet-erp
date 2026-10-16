using FluentValidation;

namespace Millet.Compras.Application.Oc.ActualizarNumeroPedimento;

public sealed class ActualizarNumeroPedimentoValidator
    : AbstractValidator<ActualizarNumeroPedimentoCommand>
{
    public ActualizarNumeroPedimentoValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty().WithErrorCode("OC_ID_REQUERIDA");
        RuleFor(c => c.NumeroPedimento)
            .MaximumLength(60).WithErrorCode("PEDIMENTO_DEMASIADO_LARGO")
            .When(c => c.NumeroPedimento is not null);
        RuleFor(c => c.NumeroBl)
            .MaximumLength(50).WithErrorCode("BL_DEMASIADO_LARGO")
            .When(c => c.NumeroBl is not null);
        RuleFor(c => c.AgenteAduanalNaviera)
            .MaximumLength(120).WithErrorCode("AGENTE_DEMASIADO_LARGO")
            .When(c => c.AgenteAduanalNaviera is not null);
    }
}
