using FluentValidation;

namespace Millet.Compras.Application.Oc.Rechazar;

public sealed class RechazarOrdenCompraValidator : AbstractValidator<RechazarOrdenCompraCommand>
{
    public RechazarOrdenCompraValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty().WithErrorCode("OC_ID_REQUERIDA");
        RuleFor(c => c.MotivoRechazoId).NotEmpty().WithErrorCode("MOTIVO_REQUERIDO");
        RuleFor(c => c.MotivoRechazoTexto)
            .MaximumLength(500).WithErrorCode("MOTIVO_TEXTO_DEMASIADO_LARGO")
            .When(c => c.MotivoRechazoTexto is not null);
        RuleFor(c => c.Notas)
            .MaximumLength(500).WithErrorCode("NOTAS_DEMASIADO_LARGAS")
            .When(c => c.Notas is not null);
    }
}
