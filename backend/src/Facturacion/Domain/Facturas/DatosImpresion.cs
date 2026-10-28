using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Domain.Facturas;

internal static class DatosImpresion
{
    internal static string? Texto(string? valor, int maximo, string campo)
    {
        var texto = string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
        if (texto?.Length > maximo)
            throw new BusinessRuleException("DATO_IMPRESION_LARGO", $"{campo} admite hasta {maximo} caracteres; acorta el texto.");
        return texto;
    }

    internal static decimal? Medida(decimal? valor)
    {
        if (valor is < 0 or > 999999999999999.999m)
            throw new BusinessRuleException("MEDIDA_IMPRESION_INVALIDA", "El peso y el área deben ser positivos o cero y caber en 15 enteros y 3 decimales; corrige la partida.");
        return valor is null ? null : Math.Round(valor.Value, 3, MidpointRounding.AwayFromZero);
    }
}
