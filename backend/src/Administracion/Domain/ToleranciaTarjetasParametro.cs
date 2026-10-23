using System.Globalization;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Domain;

/// <summary>
/// Parámetro global de la tolerancia de conciliación de tarjetas corporativas (MXN).
/// Independiente de la tolerancia factura contra OC.
/// </summary>
public static class ToleranciaTarjetasParametro
{
    public const string Clave = "cxp.tolerancia-tarjetas-mxn";
    public const decimal ValorPorOmision = 1.00m;

    public static decimal LeerValor(string valor)
    {
        if (!decimal.TryParse(valor, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var monto) || monto < 0)
            throw new BusinessRuleException("CXP_TOLERANCIA_TARJETAS_INVALIDA",
                "La tolerancia de tarjetas debe ser un monto en pesos mayor o igual a cero, con punto decimal y sin separadores de miles.");
        if (monto > 99999999999999.9999m || decimal.Round(monto, 4) != monto)
            throw new BusinessRuleException("CXP_TOLERANCIA_TARJETAS_PRECISION",
                "La tolerancia de tarjetas admite hasta 14 enteros y 4 decimales.");
        return monto;
    }
}
