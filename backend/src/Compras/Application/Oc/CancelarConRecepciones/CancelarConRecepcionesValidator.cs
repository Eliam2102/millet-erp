using FluentValidation;

namespace Millet.Compras.Application.Oc.CancelarConRecepciones;

public sealed class CancelarConRecepcionesValidator : AbstractValidator<CancelarConRecepcionesCommand>
{
    public CancelarConRecepcionesValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty().WithErrorCode("OC_ID_REQUERIDA");
        RuleFor(c => c.MotivoCancelacionId).NotEmpty().WithErrorCode("MOTIVO_CANCELACION_REQUERIDO");
        RuleFor(c => c.MotivoCancelacionTexto)
            .MaximumLength(500).When(c => c.MotivoCancelacionTexto is not null)
            .WithErrorCode("MOTIVO_CANCELACION_TEXTO_DEMASIADO_LARGO");
    }
}
