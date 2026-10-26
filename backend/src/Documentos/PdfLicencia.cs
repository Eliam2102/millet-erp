using QuestPDF.Infrastructure;

namespace Millet.Documentos;

public static class PdfLicencia
{
    private static readonly Lock Sync = new();
    private static LicenseType? _licencia;

    /// <summary>El host pasa Pdf:LicenciaQuestPdf. Sin valor se usa Community.
    /// La primera llamada fija la licencia del proceso; una configuración contradictoria falla.</summary>
    public static void Inicializar(string? licenciaQuestPdf = null)
    {
        var licencia = licenciaQuestPdf?.Trim() switch
        {
            null or "" or "Community" => LicenseType.Community,
            "Professional" => LicenseType.Professional,
            _ => throw new ArgumentException(
                "La licencia PDF no es válida. Configure Pdf:LicenciaQuestPdf como Community o Professional.",
                nameof(licenciaQuestPdf))
        };

        lock (Sync)
        {
            if (_licencia.HasValue && _licencia != licencia)
                throw new InvalidOperationException("La licencia PDF ya fue inicializada con otro valor. Revise Pdf:LicenciaQuestPdf y reinicie el proceso.");
            if (_licencia.HasValue)
                return;
            QuestPDF.Settings.License = licencia;
            _licencia = licencia;
        }
    }
}
