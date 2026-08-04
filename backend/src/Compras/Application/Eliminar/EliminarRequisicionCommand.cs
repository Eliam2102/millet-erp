using MediatR;

namespace Millet.Compras.Application.Eliminar;

/// <summary>
/// Comando para eliminar (soft-terminate por estado) una requisición desde
/// <c>Borrador</c> o <c>EnAutorizacion</c> → <c>Eliminada</c> (terminal).
/// El permiso <c>compras.requisiciones.eliminar</c> se valida en el
/// endpoint Api; el actor viene del JWT.
///
/// NOTA: Este flujo NO toca <c>DeletedAt</c>; el estado <c>Eliminada</c>
/// es la marca de "eliminada por flujo de negocio". El soft delete del
/// framework queda para purgas administrativas, fuera del scope.
/// </summary>
public sealed record EliminarRequisicionCommand(
    Guid RequisicionId,
    Guid MotivoId,
    string? MotivoTexto = null) : IRequest<Unit>;
