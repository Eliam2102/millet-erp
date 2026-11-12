using System.Globalization;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc;

/// <summary>M-080: decisión pura del TC capturado de una factura en moneda extranjera frente al catálogo (tolerancia de M-207).</summary>
public static class TipoCambioCapturaFactura
{
    public readonly record struct Decision(decimal Valor, bool Confirmado);

    public static Decision Decidir(decimal capturado, decimal? catalogo, decimal toleranciaPct, bool confirma, bool tienePermiso, DateOnly fecha)
    {
        if (catalogo is not > 0 || toleranciaPct <= 0)
            return new(capturado, false);
        var pct = Math.Abs(capturado - catalogo.Value) / catalogo.Value * 100m;
        if (pct <= toleranciaPct)
            return new(capturado, false);
        if (!confirma)
            throw new BusinessRuleException("TIPO_CAMBIO_REQUIERE_CONFIRMACION",
                string.Create(CultureInfo.InvariantCulture,
                    $"El tipo de cambio capturado {capturado:0.0000} difiere {pct:0.00} % del catálogo ({catalogo.Value:0.0000}, {fecha:yyyy-MM-dd}); la tolerancia es {toleranciaPct:0.##} %. ")
                + "Confírmalo (requiere el permiso de autorizar tipo de cambio sustituto) o corrígelo.");
        if (!tienePermiso)
            throw new ForbiddenException("TIPO_CAMBIO_CONFIRMACION_SIN_PERMISO",
                "Confirmar un tipo de cambio fuera de tolerancia requiere el permiso de autorizar tipo de cambio sustituto.");
        return new(capturado, true);
    }
}
