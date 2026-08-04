using FluentValidation;

namespace Millet.Compras.Application.Oc.Cancelar;

public sealed class CancelarOrdenCompraValidator : AbstractValidator<CancelarOrdenCompraCommand>
{
    public CancelarOrdenCompraValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty().WithErrorCode("OC_ID_REQUERIDA");
        RuleFor(c => c.MotivoCancelacionId).NotEmpty().WithErrorCode("MOTIVO_REQUERIDO");
        RuleFor(c => c.MotivoCancelacionTexto)
            .MaximumLength(500).WithErrorCode("MOTIVO_TEXTO_DEMASIADO_LARGO")
            .When(c => c.MotivoCancelacionTexto is not null);
    }
}
