using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Millet.Documentos.Componentes;

public sealed class QrCfdi : IComponent
{
    private readonly byte[] _png;
    public QrCfdi(string url, int pixelesPorModulo = 5) => _png = CfdiQr.Png(url, pixelesPorModulo);

    public void Compose(IContainer container) => container
        .Width(PdfEstilos.QrCm, Unit.Centimetre).Height(PdfEstilos.QrCm, Unit.Centimetre).Image(_png).FitArea();
}
