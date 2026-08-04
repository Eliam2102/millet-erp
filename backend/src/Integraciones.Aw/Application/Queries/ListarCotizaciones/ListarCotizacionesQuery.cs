using MediatR;
using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Application.Queries.ListarCotizaciones;

/// <summary>
/// Query del endpoint <c>GET /cotizaciones</c>. Lista paginada con
/// filtros opcionales. Empresa se aplica automáticamente vía global
/// query filter (ADR-0011) — el caller no puede consultar otra empresa.
///
/// <para>
/// Paginación offset-based, mismo patrón que
/// <c>ListarRequisicionesQuery</c> de Compras. Tope max 200 (defensivo)
/// con default 50.
/// </para>
///
/// <para>
/// Sort: por defecto <c>SubmittedAt DESC</c> (más recientes primero —
/// alinea al uso operativo de bandeja).
/// </para>
/// </summary>
public sealed record ListarCotizacionesQuery(
    EstadoEntidad? Estado = null,
    DateTimeOffset? Desde = null,
    DateTimeOffset? Hasta = null,
    string? QuoteReference = null,
    string? QuoteReferenceSearch = null,
    long? AwDocId = null,
    int Offset = 0,
    int Limit = 50
) : IRequest<PagedResponse<CotizacionResumenItem>>;
