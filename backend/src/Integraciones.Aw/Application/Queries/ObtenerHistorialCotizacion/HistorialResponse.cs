using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Application.Queries.ObtenerHistorialCotizacion;

public sealed record HistorialResponse(
    Guid CotizacionId,
    string ReferenciaExterna,
    string Sucursal,
    IReadOnlyList<HistorialItem> Envios);

public sealed record HistorialItem(
    Guid EnvioId,
    short AttemptNumber,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    EstadoEnvio Status,
    string DropServiceUrl,
    int? BytesSent,
    short? HttpStatusCode,
    string? ErrorMessage,
    string? ErrorKind,
    int? DurationMs,
    string? Filename);
