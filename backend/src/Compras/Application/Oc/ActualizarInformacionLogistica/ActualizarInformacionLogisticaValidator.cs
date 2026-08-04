using FluentValidation;

namespace Millet.Compras.Application.Oc.ActualizarInformacionLogistica;

public sealed class ActualizarInformacionLogisticaValidator
    : AbstractValidator<ActualizarInformacionLogisticaCommand>
{
    public ActualizarInformacionLogisticaValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty().WithErrorCode("OC_ID_REQUERIDA");

        // El VO valida transportista exclusivo y longitudes.
        RuleFor(c => c.TransportistaId)
            .NotEqual(Guid.Empty).WithErrorCode("TRANSPORTISTA_INVALIDO")
            .When(c => c.TransportistaId.HasValue);

        RuleFor(c => c.TransportistaTexto)
            .MaximumLength(200).WithErrorCode("TRANSPORTISTA_TEXTO_DEMASIADO_LARGO")
            .When(c => c.TransportistaTexto is not null);

        RuleFor(c => c.NumeroGuia)
            .MaximumLength(80).WithErrorCode("NUMERO_GUIA_DEMASIADO_LARGO")
            .When(c => c.NumeroGuia is not null);
    }
}
