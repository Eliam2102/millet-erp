using QuestPDF.Drawing;

namespace Millet.Documentos;

/// <summary>Fuente incrustada del kit: Carlito (SIL OFL 1.1, mismas medidas que Calibri).
/// El servidor de Azure corre en Linux y no tiene Calibri ni Arial; con la fuente dentro del
/// ensamblado el PDF sale igual en la Mac, en el CI y en producción.</summary>
public static class PdfFuentes
{
    public const string Familia = "Carlito";

    private static readonly Lock Sync = new();
    private static bool _registradas;

    /// <summary>Registra en QuestPDF las variantes de Carlito incrustadas. Es idempotente y segura
    /// entre hilos; la llama <see cref="PdfEstilos.ConfigurarPagina"/> antes de componer cada página.</summary>
    public static void Registrar()
    {
        lock (Sync)
        {
            if (_registradas)
                return;
            foreach (var recurso in Recursos())
            {
                using var fuente = typeof(PdfFuentes).Assembly.GetManifestResourceStream(recurso)
                    ?? throw new InvalidOperationException($"No se encontró la fuente incrustada {recurso}.");
                FontManager.RegisterFont(fuente);
            }
            _registradas = true;
        }
    }

    /// <summary>Nombres de los recursos .ttf incrustados en el ensamblado.</summary>
    public static IReadOnlyList<string> Recursos() => typeof(PdfFuentes).Assembly
        .GetManifestResourceNames()
        .Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.Ordinal)
        .ToArray();
}
