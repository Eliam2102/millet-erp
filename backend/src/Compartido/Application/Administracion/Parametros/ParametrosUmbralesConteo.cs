using System.Globalization;
using Millet.Almacen.Domain.Conteos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Parametros;

public static class ParametrosUmbralesConteo
{
    public const string VariacionPct = "almacen.conteo-variacion-pct-recuento";
    public const string VariacionValor = "almacen.conteo-variacion-valor-recuento";
    public const string Nivel1 = "almacen.conteo-nivel1-maximo";
    public const string Nivel2 = "almacen.conteo-nivel2-maximo";
    public static IReadOnlyList<string> Claves { get; } = Array.AsReadOnly(
        new[] { VariacionPct, VariacionValor, Nivel1, Nivel2 });

    public static ConteoUmbrales Leer(IReadOnlyDictionary<string, string> valores) => new(
        LeerNumero(valores, VariacionPct), LeerNumero(valores, VariacionValor),
        LeerNumero(valores, Nivel1), LeerNumero(valores, Nivel2));

    private static decimal LeerNumero(IReadOnlyDictionary<string, string> valores, string clave)
    {
        if (!valores.TryGetValue(clave, out var texto))
            throw new BusinessRuleException("CONTEO_UMBRALES_INCOMPLETOS",
                "Faltan umbrales de inventario. Revise los parámetros de Almacén en Administración.");
        if (!decimal.TryParse(texto, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var valor))
            throw new BusinessRuleException("CONTEO_UMBRAL_INVALIDO",
                "El umbral debe ser un número válido; use punto decimal y no use separadores de miles.");
        return valor;
    }
}
