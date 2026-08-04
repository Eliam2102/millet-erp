using MediatR;

namespace Millet.Compras.Application.Oc.ObtenerHistorico;

/// <summary>
/// Historial cronológico de una OC (F7-PR3, brecha §14.1 del 05-frontend).
/// Lee <c>core.audit_log</c> filtrado por <c>entity_type='OrdenCompra'</c>
/// + <c>aggregate_root_id = ocId</c> y devuelve un timeline ordenado
/// ASC. Cada entrada incluye operación, usuario, fecha, y un resumen
/// derivado de la columna <c>cambios</c>.
/// </summary>
public sealed record ObtenerHistoricoOrdenCompraQuery(
    Guid OrdenCompraId) : IRequest<ObtenerHistoricoOrdenCompraResponse>;

public sealed record ObtenerHistoricoOrdenCompraResponse(
    IReadOnlyList<EventoHistorico> Eventos);

public sealed record EventoHistorico(
    Guid Id,
    DateTimeOffset Timestamp,
    string Operacion,
    string Entidad,
    Guid? UsuarioId,
    string? Resumen);
