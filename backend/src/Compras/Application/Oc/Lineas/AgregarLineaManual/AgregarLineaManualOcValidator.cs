using FluentValidation;

namespace Millet.Compras.Application.Oc.Lineas.AgregarLineaManual;

/// <summary>
/// Validador de <see cref="AgregarLineaManualOcCommand"/>. Las invariantes
/// del VO <c>DescuentoLinea</c> (porcentaje 0–100, monto ≥ 0) y del ctor
/// de <c>LineaOrdenCompra</c> (cantidad > 0, precio ≥ 0, etc.) actúan
/// como red de seguridad y devuelven 422.
/// </summary>
public sealed class AgregarLineaManualOcValidator : AbstractValidator<AgregarLineaManualOcCommand>
{
    public AgregarLineaManualOcValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty().WithErrorCode("OC_ID_REQUERIDA");
        RuleFor(c => c.ArticuloId).NotEmpty().WithErrorCode("ARTICULO_REQUERIDO");
        RuleFor(c => c.DepartamentoSolicitanteId).NotEmpty().WithErrorCode("DEPARTAMENTO_REQUERIDO");

        RuleFor(c => c.Cantidad).GreaterThan(0m).WithErrorCode("CANTIDAD_INVALIDA");
        RuleFor(c => c.PrecioUnitario).GreaterThanOrEqualTo(0m).WithErrorCode("PRECIO_NEGATIVO");

        // Fase E PR3.1: el CC-Máquina pasa de opcional a REQUERIDO en la línea
        // manual de OC. La línea heredada no pasa por aquí — su CC viene 1:1 de
        // la RQ, que ya lo exige desde PR2.1 (ADR-0050).
        RuleFor(c => c.CentroCostoId)
            .NotNull().WithErrorCode("LINEA_OC_MANUAL_CENTRO_COSTO_REQUERIDO");

        RuleFor(c => c.UnidadMedida)
            .NotEmpty().WithErrorCode("UNIDAD_MEDIDA_REQUERIDA")
            .MaximumLength(20).WithErrorCode("UNIDAD_MEDIDA_DEMASIADO_LARGA");

        RuleFor(c => c.IndicadorImpuestos)
            .MaximumLength(40).WithErrorCode("INDICADOR_DEMASIADO_LARGO")
            .When(c => c.IndicadorImpuestos is not null);

        RuleFor(c => c.DescripcionExtendida)
            .MaximumLength(1000).WithErrorCode("DESCRIPCION_DEMASIADO_LARGA")
            .When(c => c.DescripcionExtendida is not null);

        RuleFor(c => c.TextoAdicional)
            .MaximumLength(500).WithErrorCode("TEXTO_ADICIONAL_DEMASIADO_LARGO")
            .When(c => c.TextoAdicional is not null);

        // Descuento: si llega cualquiera de los dos, ambos requeridos.
        RuleFor(c => c.DescuentoValor)
            .NotNull().WithErrorCode("DESCUENTO_VALOR_REQUERIDO")
            .When(c => c.DescuentoTipo.HasValue);
        RuleFor(c => c.DescuentoTipo)
            .NotNull().WithErrorCode("DESCUENTO_TIPO_REQUERIDO")
            .When(c => c.DescuentoValor.HasValue);
    }
}
