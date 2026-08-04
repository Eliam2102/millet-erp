using MediatR;

namespace Millet.Integraciones.Aw.Application.Queries.ObtenerHistorialCotizacion;

/// <summary>
/// Query del endpoint <c>GET /cotizaciones/{id}/historial</c>. Retorna
/// el historial cronológico de envíos (tabla <c>envio</c>) para una
/// cotización dada.
///
/// <para>
/// El historial de transiciones de estado de la entidad raíz NO se
/// persiste hoy — solo el estado actual + flags <c>SubmittedAt</c>,
/// <c>DeliveredToAwAt</c>, <c>CorrelatedAt</c>. PR D expone esos
/// timestamps en <c>CotizacionDetalleResponse</c>; el "historial" es
/// la cronología de intentos de drop. Si en el futuro se requiere
/// historial de transiciones, abrir tabla <c>cotizacion_state_history</c>
/// (PLATFORM-TODO).
/// </para>
/// </summary>
public sealed record ObtenerHistorialCotizacionQuery(
    Guid CotizacionId
) : IRequest<HistorialResponse?>;
