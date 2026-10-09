using MediatR;

namespace Millet.Compras.Application.Cancelar;

/// <summary>
/// Comando para cancelar una requisición autorizada (Autorizada o
/// EnSurtido) → Cancelada. El handler aborta las OC borrador asociadas
/// en una sola transacción. (P7/ADR-0061: se liberan los apartados pendientes.)
///
/// <para>
/// El permiso <c>compras.requisiciones.cancelar</c> se valida en el
/// endpoint Api; el actor sale del JWT vía <c>ICurrentUserContext</c>.
/// </para>
/// </summary>
public sealed record CancelarRequisicionCommand(
    Guid RequisicionId,
    Guid MotivoId,
    string? MotivoTexto = null) : IRequest<Unit>;
