using System.Collections.Immutable;

namespace Millet.Documentos;

public static class PdfDomicilio
{
    /// <summary>Líneas de domicilio al estilo del formato de Millet; omite lo que no se capturó.</summary>
    public static ImmutableArray<string> Lineas(string? calle, string? exterior, string? interior, string? colonia,
        string? ciudad, string? municipio, string? estado, string? cp, string? pais)
    {
        string? Unir(string sep, params string?[] partes) => string.Join(sep, partes.Where(p => !string.IsNullOrWhiteSpace(p)));
        var interiorTxt = string.IsNullOrWhiteSpace(interior) ? null : "Int. " + interior;
        return new[]
        {
            Unir(" ", calle, exterior, interiorTxt), colonia, Unir(", ", ciudad, municipio),
            Unir(", ", cp, estado), pais,
        }.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!).ToImmutableArray();
    }
}
