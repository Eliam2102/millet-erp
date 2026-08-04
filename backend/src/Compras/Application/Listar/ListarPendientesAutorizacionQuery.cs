using MediatR;
using Millet.Compras.Domain;

namespace Millet.Compras.Application.Listar;

/// <summary>
/// Bandeja específica de pendientes de autorización: filtra por
/// <c>Estado = EnAutorizacion</c>. Expone filtro opcional por
/// departamento (visibilidad típica de un autorizador) y por
/// <see cref="NivelPendiente"/> (N1/N2 que falta firmar — PR-A). Orden:
/// <c>FechaSolicitud ASC</c> (FIFO — más antiguos primero, para que las
/// RQs no se queden estancadas).
/// </summary>
public sealed record ListarPendientesAutorizacionQuery(
    Guid? DepartamentoId = null,
    NivelAutorizacion? NivelPendiente = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<RequisicionListItemResponse>>;
