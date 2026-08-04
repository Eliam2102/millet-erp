using MediatR;

namespace Millet.Compras.Application.Oc.Cancelar;

/// <summary>
/// Cancela una OC sin recepciones (F3-PR3). El caso con recepciones
/// parciales entra en F5-PR4 (requiere doble autorización y liberación
/// proporcional de RQs).
///
/// Permiso requerido: <c>compras.ordenes.cancelar</c>.
/// </summary>
public sealed record CancelarOrdenCompraCommand(
    Guid OrdenCompraId,
    Guid MotivoCancelacionId,
    string? MotivoCancelacionTexto) : IRequest;
