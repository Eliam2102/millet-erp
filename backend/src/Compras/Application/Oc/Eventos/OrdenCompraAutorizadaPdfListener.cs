using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Domain.Ports.Blob;
using Millet.Compras.Domain.Ports.Pdf;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Eventos;

/// <summary>
/// Listener in-proc (F6-PR1) que reacciona a
/// <see cref="OrdenCompraAutorizadaEvent"/>: genera el PDF institucional
/// vía <see cref="IGenerarPdfOrdenCompraPort"/>, sube los bytes al blob
/// storage y persiste la fila <see cref="OrdenCompraPdf"/>. Si ya existía
/// un PDF previo (re-autorización tras rechazo), se reemplaza (PUT
/// semantics).
///
/// <para>
/// Corre síncrono dentro del flujo MediatR: el handler de
/// <c>AutorizarOrdenCompra</c> ya hizo <c>SaveChanges</c> y publicó el
/// evento; este listener corre en la misma TX request si la
/// implementación de <c>IPublisher</c> es síncrona (default MediatR).
/// </para>
/// </summary>
public sealed class OrdenCompraAutorizadaPdfListener
    : INotificationHandler<OrdenCompraAutorizadaEvent>
{
    private readonly ComprasDbContext _db;
    private readonly IGenerarPdfOrdenCompraPort _pdfPort;
    private readonly IAlmacenarBlobPort _blobPort;
    private readonly IClock _clock;
    private readonly ILogger<OrdenCompraAutorizadaPdfListener> _logger;

    public OrdenCompraAutorizadaPdfListener(
        ComprasDbContext db,
        IGenerarPdfOrdenCompraPort pdfPort,
        IAlmacenarBlobPort blobPort,
        IClock clock,
        ILogger<OrdenCompraAutorizadaPdfListener> logger)
    {
        _db = db;
        _pdfPort = pdfPort;
        _blobPort = blobPort;
        _clock = clock;
        _logger = logger;
    }

    public async Task Handle(OrdenCompraAutorizadaEvent notification, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == notification.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró OC '{notification.OrdenCompraId}' al generar PDF.");

        var pdf = await _pdfPort.GenerarAsync(oc, cancellationToken);

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
            .FirstOrDefaultAsync(p => p.OrdenCompraId == oc.Id, cancellationToken);
        if (existente is not null)
        {
            _db.OrdenCompraPdfs.Remove(existente);
        }

        var registro = new OrdenCompraPdf(
            id: pdfId,
            ordenCompraId: oc.Id,
            blobUrl: blobUrl,
            contentType: pdf.ContentType,
            tamañoBytes: pdf.Contenido.LongLength,
            generadoEn: _clock.UtcNow,
            generadoPor: LocalPdfGeneradorIdentificador.QuestPdf);
        _db.OrdenCompraPdfs.Add(registro);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "PDF de OC generado. OcId={OcId} Folio={Folio} BlobUrl={BlobUrl} Bytes={Bytes}",
            oc.Id, notification.Folio, blobUrl, pdf.Contenido.LongLength);
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
