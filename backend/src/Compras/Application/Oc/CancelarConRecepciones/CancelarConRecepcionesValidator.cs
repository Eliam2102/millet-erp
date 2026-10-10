using FluentValidation;

namespace Millet.Compras.Application.Oc.CancelarConRecepciones;

public sealed class CancelarConRecepcionesValidator : AbstractValidator<CancelarConRecepcionesCommand>
{
    public CancelarConRecepcionesValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty().WithErrorCode("OC_ID_REQUERIDA");
        RuleFor(c => c.MotivoCancelacionId).NotEmpty().WithErrorCode("MOTIVO_CANCELACION_REQUERIDO");
        RuleFor(c => c.MotivoCancelacionTexto)
            .NotEmpty().WithMessage("Escribe el motivo de la solicitud de cancelación.")
            .MaximumLength(500)
            .WithErrorCode("MOTIVO_CANCELACION_TEXTO_DEMASIADO_LARGO");
    }
}
