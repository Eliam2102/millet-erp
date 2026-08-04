using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Application.Queries.ListarCotizaciones;

/// <summary>
/// Item de la lista de cotizaciones (response de bandeja). Subset del
/// detalle — los campos que la UI muestra en una fila.
/// </summary>
public sealed record CotizacionResumenItem(
    Guid Id,
    string ReferenciaExterna,
    string Sucursal,
    EstadoEntidad Estado,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? DeliveredToAwAt,
    DateTimeOffset? CorrelatedAt,
    long? AwDocId,
    short RetryCount,
    string? LastError);
