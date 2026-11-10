using System.Globalization;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Domain;

/// <summary>
/// M-207 (86e3zr7nf): diferencia máxima, en porcentaje, entre el tipo de cambio de un CFDI en moneda extranjera y el del
/// catálogo común (K10.6) para la misma fecha. Fuera de tolerancia, el motor contable manda el evento a Rechazos para que se
/// capture y autorice un valor sustituto. <c>0</c> apaga la comparación.
/// </summary>
public static class ToleranciaTipoCambioCfdiParametro
{
    public const string Clave = "contabilidad.tipo-cambio.tolerancia-pct";
    public const decimal ValorPorOmision = 1.00m;

    public static decimal LeerValor(string valor)
    {
        if (!decimal.TryParse(valor, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var pct)
            || pct < 0 || pct > 100)
            throw new BusinessRuleException("CONTAB_TOLERANCIA_TC_INVALIDA",
                "La tolerancia del tipo de cambio debe ser un porcentaje entre 0 y 100, con punto decimal (0 apaga la comparación).");
        if (decimal.Round(pct, 2) != pct)
            throw new BusinessRuleException("CONTAB_TOLERANCIA_TC_PRECISION",
                "La tolerancia del tipo de cambio admite hasta 2 decimales.");
        return pct;
    }
}
