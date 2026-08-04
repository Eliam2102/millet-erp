using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Cfdi.DescargarCfdiBlob;

/// <summary>
/// Descarga el XML o el PDF de un <see cref="CfdiRecibido"/> desde el
/// blob storage. Cierra el PLATFORM-TODO(&lt;CfdiBlobDownload&gt;): sirve
/// al <c>CfdiXmlViewer</c>/<c>CfdiPdfPreview</c> del FE y a la descarga
/// directa del archivo.
///
/// <para>El caller (endpoint) es dueño del stream: lo entrega a
/// <c>Results.Stream</c>, que lo dispone al terminar la response.</para>
/// </summary>
public sealed record DescargarCfdiBlobQuery(Guid Id, bool Pdf)
    : IRequest<CfdiBlobDescarga>;

public sealed record CfdiBlobDescarga(
    Stream Contenido,
    string ContentType,
    string FileName);

public sealed class DescargarCfdiBlobHandler
    : IRequestHandler<DescargarCfdiBlobQuery, CfdiBlobDescarga>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICfdiBlobStorage _blobStorage;

    public DescargarCfdiBlobHandler(
        CuentasPorPagarDbContext db,
        ICfdiBlobStorage blobStorage)
    {
        _db = db;
        _blobStorage = blobStorage;
    }

    public async Task<CfdiBlobDescarga> Handle(
        DescargarCfdiBlobQuery query,
        CancellationToken cancellationToken)
    {
        var cfdi = await _db.CfdisRecibidos
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CFDI_NO_ENCONTRADO",
                $"No se encontró el CFDI con id '{query.Id}'.");

        var blobRef = query.Pdf ? cfdi.PdfBlobRef : cfdi.XmlBlobRef;
        if (string.IsNullOrEmpty(blobRef))
        {
            throw new BusinessRuleException(
                query.Pdf ? "CFDI_SIN_PDF" : "CFDI_SIN_XML",
                query.Pdf
                    ? "El CFDI no tiene PDF almacenado."
                    : "El CFDI no tiene XML almacenado.");
        }

        var stream = query.Pdf
            ? await _blobStorage.LeerPdfAsync(blobRef, cancellationToken)
            : await _blobStorage.LeerXmlAsync(blobRef, cancellationToken);

        if (stream is null)
        {
            throw new BusinessRuleException(
                query.Pdf ? "CFDI_PDF_NO_DISPONIBLE" : "CFDI_XML_NO_DISPONIBLE",
                "El archivo del CFDI no está disponible en el almacenamiento.");
        }

        return new CfdiBlobDescarga(
            Contenido: stream,
            ContentType: query.Pdf ? "application/pdf" : "application/xml",
            FileName: $"{cfdi.UuidCfdi.Valor}.{(query.Pdf ? "pdf" : "xml")}");
    }
}
