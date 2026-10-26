using FluentValidation;

namespace Millet.Compras.Application.Lineas.ActualizarLinea;

public sealed class ActualizarLineaValidator : AbstractValidator<ActualizarLineaCommand>
{
    public ActualizarLineaValidator()
    {
        RuleFor(c => c.RequisicionId).NotEmpty().WithErrorCode("REQUISICION_REQUERIDA");
        RuleFor(c => c.LineaId).NotEmpty().WithErrorCode("LINEA_REQUERIDA");
        RuleFor(c => c.ArticuloId).NotEmpty().WithErrorCode("ARTICULO_REQUERIDO");

        // ADM08: null solicita la herencia. La regla autoritativa resuelve el centro.
        RuleFor(c => c.CentroCostoId).NotEqual(Guid.Empty)
            .When(c => c.CentroCostoId.HasValue).WithErrorCode("CECO_INVALIDO");

        RuleFor(c => c.Cantidad)
            .GreaterThan(0m).WithErrorCode("CANTIDAD_INVALIDA");

        RuleFor(c => c.UnidadMedida)
            .NotEmpty().WithErrorCode("UNIDAD_MEDIDA_REQUERIDA")
            .MaximumLength(20).WithErrorCode("UNIDAD_MEDIDA_DEMASIADO_LARGA");

        // Precio estimado opcional (null = 0): solo se valida si viene.
        RuleFor(c => c.PrecioEstimadoMonto)
            .GreaterThanOrEqualTo(0m).WithErrorCode("PRECIO_NEGATIVO")
            .When(c => c.PrecioEstimadoMonto.HasValue);

        RuleFor(c => c.PrecioEstimadoMoneda)
            .NotEmpty().WithErrorCode("MONEDA_REQUERIDA")
            .Matches("^[A-Z]{3}$").WithErrorCode("MONEDA_FORMATO_INVALIDO")
            .When(c => c.PrecioEstimadoMoneda is not null);

        RuleFor(c => c.ClaseActivoId).NotEqual(Guid.Empty)
            .When(c => c.ClaseActivoId.HasValue).WithErrorCode("CLASE_ACTIVO_INVALIDA");

        RuleFor(c => c.Proyecto)
            .MaximumLength(200).WithErrorCode("PROYECTO_DEMASIADO_LARGO")
            .When(c => c.Proyecto is not null);
    }
}
