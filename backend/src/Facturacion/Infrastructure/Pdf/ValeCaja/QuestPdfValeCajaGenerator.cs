using Millet.Facturacion.Domain.Ports;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Millet.Facturacion.Infrastructure.Pdf.ValeCaja;

/// <summary>Adapter QuestPDF de <see cref="IGenerarValeCajaPdfPort"/> (86e3ww6tw).</summary>
public sealed class QuestPdfValeCajaGenerator : IGenerarValeCajaPdfPort
{
    static QuestPdfValeCajaGenerator()
    {
        // QuestPDF Community license: uso comercial bajo $1M USD anuales (igual que QuestPdfFacturaGenerator).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public PdfFacturaGenerado Generar(ValeCajaPdfModel modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        return new PdfFacturaGenerado(new ValeCajaDocument(modelo).GeneratePdf(), "application/pdf", modelo.NombreArchivo);
    }
}
