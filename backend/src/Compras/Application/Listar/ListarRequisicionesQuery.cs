using MediatR;
using Millet.Compras.Domain;

namespace Millet.Compras.Application.Listar;

/// <summary>
/// Query de bandeja general. Aplica el global query filter de empresa
/// (ADR-0011) automáticamente. Filtros opcionales soportados por los
/// índices §10.1: estado, departamentoId, requisitanteId.
///
/// Paginación offset-based con tope de 200 para evitar cargas costosas
/// (default 50). Orden: <c>FechaSolicitud DESC</c> para coincidir con
/// el índice <c>(empresa_id, estado, fecha_solicitud DESC)</c>.
/// </summary>
public sealed record ListarRequisicionesQuery(
    EstadoRequisicion? Estado = null,
    Guid? DepartamentoId = null,
    Guid? RequisitanteId = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<RequisicionListItemResponse>>;
