using System.Globalization;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Domain;

public static class ToleranciaFacturaContraOcParametro
{
    public const string Clave = "cxp.tolerancia-factura-contra-oc-mxn";
    public const decimal ValorPorOmision = 0.99m;

    public static decimal LeerValor(string valor)
    {
        if (!decimal.TryParse(valor, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var monto) || monto < 0)
            throw new BusinessRuleException("CXP_TOLERANCIA_GENERAL_INVALIDA",
                "La tolerancia general debe ser un monto en pesos mayor o igual a cero, con punto decimal y sin separadores de miles.");
        if (monto > 99999999999999.9999m || decimal.Round(monto, 4) != monto)
            throw new BusinessRuleException("CXP_TOLERANCIA_GENERAL_PRECISION",
                "La tolerancia admite hasta 14 enteros y 4 decimales, igual que la foto en la factura.");
        return monto;
    }
}
