using FluentValidation;

namespace Millet.Compras.Application.Lineas.ActualizarLinea;

public sealed class ActualizarLineaValidator : AbstractValidator<ActualizarLineaCommand>
{
    public ActualizarLineaValidator()
    {
        RuleFor(c => c.RequisicionId).NotEmpty().WithErrorCode("REQUISICION_REQUERIDA");
        RuleFor(c => c.LineaId).NotEmpty().WithErrorCode("LINEA_REQUERIDA");
        RuleFor(c => c.ArticuloId).NotEmpty().WithErrorCode("ARTICULO_REQUERIDO");

        // Fase E PR2.1: el CC-Máquina es OBLIGATORIO también al editar. El PATCH
        // es replace completo (el FE prellenar con el CC actual de la línea), así
        // que editar una línea histórica sin CC obliga a elegir uno.
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
    }
}
