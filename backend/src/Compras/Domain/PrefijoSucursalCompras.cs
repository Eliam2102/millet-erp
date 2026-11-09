using System.Text.RegularExpressions;

namespace Millet.Compras.Domain;

internal static partial class PrefijoSucursalCompras
{
    // Claves operativas de Millet. El folio usa un prefijo distinto de la clave de integración.
    public static string Resolver(string clave) => clave switch
    {
        "101" => "CON",
        "102" => "CHI",
        "103" => "CIR",
        "104" => "CAN",
        "105" => "PIN",
        _ => clave
    };

    public static bool EsValido(string? clave) =>
        clave is not null && Formato().IsMatch(Resolver(clave));

    [GeneratedRegex(@"\A[A-Z]{2,4}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Formato();
}
