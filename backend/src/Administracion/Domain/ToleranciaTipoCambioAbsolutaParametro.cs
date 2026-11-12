using System.Globalization;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Domain;

/// <summary>
/// M-080 (86e3zr5ue): diferencia máxima, en pesos por unidad de moneda extranjera, entre el tipo de cambio capturado en una
/// factura de proveedor y el del catálogo. Fuera de ella se exige confirmación con el permiso de autorizar sustituto.
/// <c>0</c> la apaga y se usa la tolerancia porcentual de M-207. No afecta el comportamiento del motor contable (M-207).
/// </summary>
public static class ToleranciaTipoCambioAbsolutaParametro
{
    public const string Clave = "contabilidad.tipo-cambio.tolerancia-abs-mxn";
    public const decimal ValorPorOmision = 0.05m;

    public static decimal LeerValor(string valor)
    {
        if (!decimal.TryParse(valor, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var mxn) || mxn < 0 || mxn > 100)
            throw new BusinessRuleException("CONTAB_TOLERANCIA_TC_ABS_INVALIDA",
                "La tolerancia absoluta del tipo de cambio debe ser un monto en pesos entre 0 y 100, con punto decimal (0 la apaga).");
        if (decimal.Round(mxn, 4) != mxn)
            throw new BusinessRuleException("CONTAB_TOLERANCIA_TC_ABS_PRECISION", "La tolerancia absoluta admite hasta 4 decimales.");
        return mxn;
    }
}
