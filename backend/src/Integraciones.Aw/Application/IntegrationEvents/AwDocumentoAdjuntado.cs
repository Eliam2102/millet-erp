using Millet.SharedKernel.Application.Integration;

namespace Millet.Integraciones.Aw.Application.IntegrationEvents;

/// <summary>
/// Evento emitido cuando el <c>AwDocumentSyncWorker</c> descarga el PDF que
/// A+W exportó (oferta/pedido) y lo sube a Blob Storage, dejándolo
/// disponible para el Glass Agent vía el endpoint proxy
/// <c>GET /cotizaciones/{id}/pdf</c>.
///
/// <para>
/// Consumers esperados: notificación realtime al Glass Agent (Soketi +
/// SignalR). También útil para módulos futuros (Facturación / CxC) que
/// quieran adjuntar el PDF original de A+W.
/// </para>
/// </summary>
public sealed record AwDocumentoAdjuntado(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid AggregateId,
    string QuoteReference,
    long AwDocId,
    string DocType,
    string PdfFilename,
    DateTimeOffset PdfUploadedAt)
    : IntegrationEvent(EventTypeName, EmpresaId, OcurridoEn)
{
    public const string EventTypeName = "integraciones.aw.documento.adjuntado.v1";
}
