using FluentValidation;

namespace Millet.Compras.Application.Oc.ActualizarInformacionImportacion;

public sealed class ActualizarInformacionImportacionValidator
    : AbstractValidator<ActualizarInformacionImportacionCommand>
{
    public ActualizarInformacionImportacionValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty().WithErrorCode("OC_ID_REQUERIDA");

        RuleFor(c => c.IncotermId)
            .NotEqual(Guid.Empty).WithErrorCode("INCOTERM_INVALIDO")
            .When(c => c.IncotermId.HasValue);

        RuleFor(c => c.PaisOrigen)
            .Length(2).WithErrorCode("PAIS_FORMATO_INVALIDO")
            .WithMessage("PaisOrigen debe ser código ISO 3166-1 alpha-2 (2 caracteres).")
            .When(c => c.PaisOrigen is not null);

        RuleFor(c => c.NumeroContenedor)
            .MaximumLength(40).WithErrorCode("CONTENEDOR_DEMASIADO_LARGO")
            .When(c => c.NumeroContenedor is not null);

        RuleFor(c => c.CodigoRuta)
            .MaximumLength(40).WithErrorCode("RUTA_DEMASIADO_LARGA")
            .When(c => c.CodigoRuta is not null);

        RuleFor(c => c.SemanaEmbarque)
            .MaximumLength(40).WithErrorCode("SEMANA_DEMASIADO_LARGA")
            .When(c => c.SemanaEmbarque is not null);
    }
}
