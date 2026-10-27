using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Millet.Documentos.Componentes;

public sealed class EncabezadoEmpresa(EmpresaEncabezado empresa, DocumentoTitulo documento) : IComponent
{
    public void Compose(IContainer container)
    {
        container.Column(columna =>
        {
            columna.Spacing(5);
            if (!string.IsNullOrWhiteSpace(documento.Marca))
                columna.Item().AlignRight().Border(1).BorderColor(PdfEstilos.RojoMillet)
                    .Padding(4).Text(documento.Marca).Bold().FontColor(PdfEstilos.RojoMillet);

            columna.Item().Row(fila =>
            {
                fila.Spacing(12);
                var izquierda = fila.ConstantItem(PdfEstilos.LogoAnchoCm, Unit.Centimetre);
                var logo = empresa.Logo;
                if (logo is { Length: > 0 })
                    izquierda.MaxHeight(PdfEstilos.LogoAnchoCm, Unit.Centimetre).Image(logo).FitArea();
                else
                    izquierda.Text(string.IsNullOrWhiteSpace(empresa.NombreComercial)
                        ? empresa.RazonSocial : empresa.NombreComercial).FontSize(PdfEstilos.Titulo).Bold();

                fila.RelativeItem().Column(derecha =>
                {
                    derecha.Item().AlignRight().Text(documento.TituloEs).FontSize(PdfEstilos.Titulo).Bold();
                    if (!string.IsNullOrWhiteSpace(documento.TituloEn))
                        derecha.Item().AlignRight().Text(documento.TituloEn).FontSize(PdfEstilos.Detalle);
                    derecha.Item().AlignRight().Text(documento.SerieFolio).Bold().FontColor(PdfEstilos.RojoMillet);
                    derecha.Item().AlignRight().Text(empresa.RazonSocial).Bold();
                    foreach (var linea in empresa.DomicilioLineas.Where(x => !string.IsNullOrWhiteSpace(x)))
                        derecha.Item().AlignRight().Text(linea);
                    derecha.Item().AlignRight().Text($"RFC: {empresa.Rfc}");
                    derecha.Item().AlignRight().Text(empresa.RegimenFiscal);
                    derecha.Item().AlignRight().Text(documento.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
                });
            });
        });
    }
}
