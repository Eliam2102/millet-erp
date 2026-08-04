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

        // Fase E PR2.1: el CC-Máquina pasa a OBLIGATORIO en la línea de RQ. El
        // capturista elige del picker filtrado por su alcance; si le sale vacío,
        // el admin de CentrosCosto le asigna CCs en /centros-costo/asignaciones.
        RuleFor(c => c.CentroCostoId)
            .NotNull().WithErrorCode("LINEA_RQ_CENTRO_COSTO_REQUERIDO");

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
