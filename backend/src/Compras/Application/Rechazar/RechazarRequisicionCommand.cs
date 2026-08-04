using MediatR;

namespace Millet.Compras.Application.Rechazar;

/// <summary>
/// Comando para rechazar una requisición desde
/// <c>EnAutorizacion</c> → <c>Rechazada</c> (terminal). El permiso
/// <c>compras.requisiciones.rechazar</c> se valida en el endpoint Api;
/// el actor (quien rechaza) viene del JWT vía <c>ICurrentUserContext</c>.
/// </summary>
public sealed record RechazarRequisicionCommand(
    Guid RequisicionId,
    Guid MotivoId,
    string? MotivoTexto = null) : IRequest<Unit>;
