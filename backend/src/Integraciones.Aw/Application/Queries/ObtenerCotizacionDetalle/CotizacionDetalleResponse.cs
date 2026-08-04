using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Application.Queries.ObtenerCotizacionDetalle;

/// <summary>
/// Response del detalle. Schema fija para serialización JSON (camelCase).
/// </summary>
public sealed record CotizacionDetalleResponse(
    Guid Id,
    TipoEntidad TipoEntidad,
    string ReferenciaExterna,
    string Sucursal,
    Guid EmpresaId,
    EstadoEntidad Estado,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? DeliveredToAwAt,
    DateTimeOffset? CorrelatedAt,
    long? AwDocId,
    string? AwDocIdSecondary,
    Guid? SubmittedBySpnId,
    string? LastError,
    short RetryCount,
    string? ResolutionNote,
    // PDF de A+W (oferta/pedido). PdfUrl es la ruta del endpoint proxy
    // autenticado (no la URL interna del blob); null hasta que el
    // AwDocumentSyncWorker adjunte el archivo.
    string? PdfUrl,
    string? PdfFilename,
    DateTimeOffset? PdfUploadedAt,
    CorrelacionDetalle? Correlacion,
    IReadOnlyList<EnvioDetalleItem> EnviosRecientes);

public sealed record CorrelacionDetalle(
    Guid Id,
    long AwDocId,
    string? AwDocIdSecondary,
    int PollingCycleNumber,
    int? PollingQueryDurationMs,
    DateTimeOffset CorrelatedAt);

public sealed record EnvioDetalleItem(
    Guid Id,
    short AttemptNumber,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    EstadoEnvio Status,
    string DropServiceUrl,
    int? BytesSent,
    short? HttpStatusCode,
    string? ErrorMessage,
    string? ErrorKind,
    int? DurationMs);
