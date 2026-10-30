using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Blob;
using Millet.Compras.Application.Oc.Pdf;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Oc.Eventos;

/// <summary>
/// Listener in-proc (F6-PR1) que reacciona a
/// <see cref="OrdenCompraAutorizadaPersistida"/>: genera el PDF institucional
/// vía <see cref="ImpresionOrdenCompraService"/> (sin marca: foto del momento de la
/// autorización; descargas y envíos se renderizan al vuelo), sube los bytes al blob
/// storage y persiste la fila <see cref="OrdenCompraPdf"/>. Si ya existía
/// un PDF previo (re-autorización tras rechazo), se reemplaza (PUT
/// semantics).
///
/// <para>
/// Se publica después de persistir la autorización y su outbox. El guardado
/// del PDF es independiente y no participa en la persistencia del evento.
/// </para>
/// </summary>
public sealed class OrdenCompraAutorizadaPdfListener
    : INotificationHandler<OrdenCompraAutorizadaPersistida>
{
    private readonly ComprasDbContext _db;
    private readonly ImpresionOrdenCompraService _impresion;
    private readonly IAlmacenarBlobPort _blobPort;
    private readonly IClock _clock;
    private readonly ILogger<OrdenCompraAutorizadaPdfListener> _logger;

    public OrdenCompraAutorizadaPdfListener(
        ComprasDbContext db,
        ImpresionOrdenCompraService impresion,
        IAlmacenarBlobPort blobPort,
        IClock clock,
        ILogger<OrdenCompraAutorizadaPdfListener> logger)
    {
        _db = db;
        _impresion = impresion;
        _blobPort = blobPort;
        _clock = clock;
        _logger = logger;
    }

    public async Task Handle(OrdenCompraAutorizadaPersistida notification, CancellationToken cancellationToken)
    {
        var pdf = await _impresion.RenderizarAsync(notification.OrdenCompraId, marca: null, cancellationToken);

        using var stream = new MemoryStream(pdf.Contenido, writable: false);
        var pdfId = Guid.CreateVersion7();
        var blobUrl = await _blobPort.SubirAsync(
            blobId: pdfId,
            contenido: stream,
            contentType: pdf.ContentType,
            nombreArchivoOriginal: pdf.NombreSugerido,
            cancellationToken);

        // PUT semantics: si ya existe un PDF previo (re-autorización tras
        // rechazo), lo reemplazamos. El blob viejo queda huérfano y lo
        // limpia el job de blob cleanup (post-MVP).
        var existente = await _db.OrdenCompraPdfs
            .FirstOrDefaultAsync(p => p.OrdenCompraId == notification.OrdenCompraId, cancellationToken);
        if (existente is not null)
        {
            _db.OrdenCompraPdfs.Remove(existente);
        }

        var registro = new OrdenCompraPdf(
            id: pdfId,
            ordenCompraId: notification.OrdenCompraId,
            blobUrl: blobUrl,
            contentType: pdf.ContentType,
            tamañoBytes: pdf.Contenido.LongLength,
            generadoEn: _clock.UtcNow,
            generadoPor: LocalPdfGeneradorIdentificador.QuestPdf);
        _db.OrdenCompraPdfs.Add(registro);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "PDF de OC generado. OcId={OcId} Folio={Folio} BlobUrl={BlobUrl} Bytes={Bytes}",
            notification.OrdenCompraId, notification.Folio, blobUrl, pdf.Contenido.LongLength);
    }
}

/// <summary>
/// Identificadores estables del generador de PDF (F6-PR1 stub, F6-PR3
/// real con QuestPDF). Persisten en <c>OrdenCompraPdf.GeneradoPor</c>
/// para audit.
/// </summary>
public static class LocalPdfGeneradorIdentificador
{
    public const string Stub = "LocalPdfOrdenCompraStub";
    public const string QuestPdf = "QuestPdfOrdenCompraGenerator";
}
