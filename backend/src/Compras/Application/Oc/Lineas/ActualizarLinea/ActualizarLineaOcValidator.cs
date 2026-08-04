using FluentValidation;

namespace Millet.Compras.Application.Oc.Lineas.ActualizarLinea;

public sealed class ActualizarLineaOcValidator : AbstractValidator<ActualizarLineaOcCommand>
{
    public ActualizarLineaOcValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty().WithErrorCode("OC_ID_REQUERIDA");
        RuleFor(c => c.LineaId).NotEmpty().WithErrorCode("LINEA_ID_REQUERIDA");

        RuleFor(c => c.ArticuloId)
            .NotEqual(Guid.Empty).WithErrorCode("ARTICULO_INVALIDO")
            .When(c => c.ArticuloId.HasValue);

        RuleFor(c => c.DepartamentoSolicitanteId)
            .NotEqual(Guid.Empty).WithErrorCode("DEPARTAMENTO_INVALIDO")
            .When(c => c.DepartamentoSolicitanteId.HasValue);

        RuleFor(c => c.Cantidad)
            .GreaterThan(0m).WithErrorCode("CANTIDAD_INVALIDA")
            .When(c => c.Cantidad.HasValue);

        RuleFor(c => c.PrecioUnitario)
            .GreaterThanOrEqualTo(0m).WithErrorCode("PRECIO_NEGATIVO")
            .When(c => c.PrecioUnitario.HasValue);

        RuleFor(c => c.UnidadMedida)
            .NotEmpty().WithErrorCode("UNIDAD_MEDIDA_REQUERIDA")
            .MaximumLength(20).WithErrorCode("UNIDAD_MEDIDA_DEMASIADO_LARGA")
            .When(c => c.UnidadMedida is not null);

        RuleFor(c => c.IndicadorImpuestos)
            .MaximumLength(40).WithErrorCode("INDICADOR_DEMASIADO_LARGO")
            .When(c => c.IndicadorImpuestos is not null);

        RuleFor(c => c.DescripcionExtendida)
            .MaximumLength(1000).WithErrorCode("DESCRIPCION_DEMASIADO_LARGA")
            .When(c => c.DescripcionExtendida is not null);

        // Descuento: ambos o ninguno.
        RuleFor(c => c.DescuentoValor)
            .NotNull().WithErrorCode("DESCUENTO_VALOR_REQUERIDO")
            .When(c => c.DescuentoTipo.HasValue);
        RuleFor(c => c.DescuentoTipo)
            .NotNull().WithErrorCode("DESCUENTO_TIPO_REQUERIDO")
            .When(c => c.DescuentoValor.HasValue);
    }
}
