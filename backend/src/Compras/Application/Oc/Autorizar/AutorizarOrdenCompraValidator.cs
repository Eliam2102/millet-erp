using FluentValidation;

namespace Millet.Compras.Application.Oc.Autorizar;

public sealed class AutorizarOrdenCompraValidator : AbstractValidator<AutorizarOrdenCompraCommand>
{
    public AutorizarOrdenCompraValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty().WithErrorCode("OC_ID_REQUERIDA");
        RuleFor(c => c.Nivel).IsInEnum().WithErrorCode("NIVEL_INVALIDO");
        RuleFor(c => c.Notas)
            .MaximumLength(500).WithErrorCode("NOTAS_DEMASIADO_LARGAS")
            .When(c => c.Notas is not null);
    }
}
