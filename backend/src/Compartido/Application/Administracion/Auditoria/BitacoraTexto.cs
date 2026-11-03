using System.Text.RegularExpressions;

namespace Millet.Administracion.Application.Auditoria;

/// <summary>
/// Vuelve legibles los nombres técnicos que el interceptor de auditoría guarda en
/// la etiqueta y el resumen («CuentaBancariaProveedor», «EnRevision → Activo»).
/// Se aplica al leer, así cubre también las filas ya escritas.
/// </summary>
public static partial class BitacoraTexto
{
    private static readonly Dictionary<string, string> Acentos = new(StringComparer.Ordinal)
    {
        ["revision"] = "revisión", ["razon"] = "razón", ["codigo"] = "código", ["numero"] = "número",
        ["telefono"] = "teléfono", ["direccion"] = "dirección", ["clasificacion"] = "clasificación",
        ["condicion"] = "condición", ["credito"] = "crédito", ["regimen"] = "régimen", ["aw"] = "A+W",
    };

    // Identificadores PascalCase de al menos dos palabras: «EnRevision», «ProductoAw».
    [GeneratedRegex(@"\b[A-Z][a-z]+(?:[A-Z][a-z0-9]*)+\b")]
    private static partial Regex PascalCase();

    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex Corte();

    public static string Legible(string texto, string? entidad = null)
    {
        if (string.IsNullOrEmpty(texto)) return texto;
        // «Creó Entidad Entidad 01a18f7b» (etiqueta de respaldo repetida) → una sola vez.
        if (!string.IsNullOrEmpty(entidad)) texto = texto.Replace($"{entidad} {entidad} ", $"{entidad} ", StringComparison.Ordinal);
        return PascalCase().Replace(texto, m =>
        {
            var partes = Corte().Split(m.Value).Select((p, i) =>
            {
                var bajo = p.ToLowerInvariant();
                var r = Acentos.GetValueOrDefault(bajo, bajo);
                return i == 0 && r != "A+W" ? char.ToUpperInvariant(r[0]) + r[1..] : r;
            });
            return string.Join(' ', partes);
        });
    }
}
