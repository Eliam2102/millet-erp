namespace Millet.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// 86e3tfdx8 — búsqueda parcial por palabras: separa el texto capturado en
/// palabras normalizadas (minúsculas y sin acentos con el mismo mapa que
/// <see cref="PostgresFunctions.Translate"/>, ADR-0045) para exigir que TODAS
/// aparezcan, en cualquier orden («carlos perez» encuentra «PÉREZ GARCÍA
/// CARLOS»). Cada consulta aplica un <c>Where</c> por palabra contra sus
/// columnas pasadas por <c>translate(lower(columna))</c>.
/// </summary>
public static class BusquedaPorPalabras
{
    public static IReadOnlyList<string> Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return [];

        var chars = texto.ToLowerInvariant().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var idx = PostgresFunctions.AcentosOrigen.IndexOf(chars[i], StringComparison.Ordinal);
            if (idx >= 0) chars[i] = PostgresFunctions.AcentosDestino[idx];
        }

        return new string(chars)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
