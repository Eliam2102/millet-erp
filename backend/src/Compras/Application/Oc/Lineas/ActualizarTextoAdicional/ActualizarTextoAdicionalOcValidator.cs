using FluentValidation;

namespace Millet.Compras.Application.Oc.Lineas.ActualizarTextoAdicional;

public sealed class ActualizarTextoAdicionalOcValidator : AbstractValidator<ActualizarTextoAdicionalOcCommand>
{
    public ActualizarTextoAdicionalOcValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty().WithErrorCode("OC_ID_REQUERIDA");
        RuleFor(c => c.LineaId).NotEmpty().WithErrorCode("LINEA_ID_REQUERIDA");
        RuleFor(c => c.TextoAdicional)
            .MaximumLength(500).WithErrorCode("TEXTO_ADICIONAL_DEMASIADO_LARGO")
            .When(c => c.TextoAdicional is not null);
    }
}
