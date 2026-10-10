using FluentValidation;

namespace Millet.Compras.Application.Lineas.AgregarLinea;

/// <summary>
/// Validación de input shape de <see cref="AgregarLineaCommand"/>. Errores
/// devuelven HTTP 400. Los invariantes del agregado (estado != Borrador,
/// cantidad <= 0, etc.) los maneja <see cref="Domain.Requisicion.AgregarLinea"/>
/// → 422.
/// </summary>
public sealed class AgregarLineaValidator : AbstractValidator<AgregarLineaCommand>
{
    public AgregarLineaValidator()
    {
        RuleFor(c => c.RequisicionId).NotEmpty().WithErrorCode("REQUISICION_REQUERIDA");
        RuleFor(c => c.ArticuloId).NotEmpty().WithErrorCode("ARTICULO_REQUERIDO");

        // ADM08: null solicita la herencia. La regla autoritativa resuelve el centro.
        RuleFor(c => c.CentroCostoId).NotEqual(Guid.Empty)
            .When(c => c.CentroCostoId.HasValue).WithErrorCode("CECO_INVALIDO");

        RuleFor(c => c.Cantidad)
            .GreaterThan(0m).WithErrorCode("CANTIDAD_INVALIDA");

        RuleFor(c => c.UnidadMedida)
            .NotEmpty().WithErrorCode("UNIDAD_MEDIDA_REQUERIDA")
            .MaximumLength(20).WithErrorCode("UNIDAD_MEDIDA_DEMASIADO_LARGA");

        RuleFor(c => c.PrecioEstimadoMonto)
            .GreaterThanOrEqualTo(0m).WithErrorCode("PRECIO_NEGATIVO");

        RuleFor(c => c.PrecioEstimadoMoneda)
            .NotEmpty().WithErrorCode("MONEDA_REQUERIDA")
            .Matches("^[A-Z]{3}$").WithErrorCode("MONEDA_FORMATO_INVALIDO");

        RuleFor(c => c.Proyecto)
            .MaximumLength(200).WithErrorCode("PROYECTO_DEMASIADO_LARGO")
            .When(c => c.Proyecto is not null);

        RuleFor(c => c.Notas)
            .MaximumLength(500).WithErrorCode("NOTAS_DEMASIADO_LARGAS")
            .When(c => c.Notas is not null);
    }
}
