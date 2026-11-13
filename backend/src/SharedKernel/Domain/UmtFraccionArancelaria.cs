using System.Reflection;

namespace Millet.SharedKernel.Domain;

/// <summary>
/// H-U22 (86e40p363): la unidad aduanera de una partida de Comercio Exterior debe coincidir con la UMT de su fracción arancelaria
/// (catálogo público SAT c_FraccionArancelaria vigente (v2.0, 2024), ver README junto al recurso). Si no, el PAC rechaza el timbre con CCE161 y el folio
/// ya se gastó; aquí se detiene antes. Fracción desconocida o unidad 99 (servicio): no se bloquea, lo decide el PAC.
/// </summary>
public static class UmtFraccionArancelaria
{
    public const string Codigo = "CCE_UNIDAD_ADUANA_UMT";
    private const string Recurso = "Millet.SharedKernel.Catalogos.c_FraccionArancelaria_umt.csv";
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Tabla = new(Cargar);

    /// <summary>
    /// UMT de la fracción de 10 dígitos (fracción + NICO, como la publica el catálogo vigente del SAT), o null si el catálogo no la trae
    /// (fracción de 8 dígitos, inexistente o derogada): sin base para rechazar.
    /// </summary>
    public static string? Umt(string? fraccion)
    {
        var f = fraccion?.Trim();
        if (f is null || f.Length != 10 || !f.All(char.IsDigit)) return null;
        return Tabla.Value.TryGetValue(f, out var umt) ? umt : null;
    }

    /// <summary>La UMT esperada cuando la unidad capturada no coincide; null si es válida o no hay base para rechazarla.</summary>
    public static string? UmtEsperadaSiNoCoincide(string? fraccion, string? unidadAduana)
    {
        var unidad = unidadAduana?.Trim();
        if (string.IsNullOrEmpty(unidad) || unidad == "99") return null;
        var umt = Umt(fraccion);
        return umt is not null && !string.Equals(umt, unidad, StringComparison.Ordinal) ? umt : null;
    }

    public static string Mensaje(string fraccion, string unidad, string umt) =>
        $"La unidad aduanera {unidad.Trim()} no corresponde a la UMT {umt} de la fracción {fraccion.Trim()} (catálogo SAT c_FraccionArancelaria); " +
        "el PAC la rechazaría (CCE161).";

    private static Dictionary<string, string> Cargar()
    {
        using var s = typeof(UmtFraccionArancelaria).Assembly.GetManifestResourceStream(Recurso)
            ?? throw new InvalidOperationException($"Falta el recurso {Recurso}.");
        using var r = new StreamReader(s);
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        _ = r.ReadLine(); // encabezado
        while (r.ReadLine() is { Length: > 0 } linea)
        {
            var i = linea.IndexOf(',');
            d[linea[..i]] = linea[(i + 1)..].Trim();
        }
        return d;
    }
}
