using MediatR;

namespace Millet.Compras.Application.Oc.Rechazar;

/// <summary>
/// Rechaza una OC en cualquier nivel del flujo de autorización. El
/// permiso requerido (<c>compras.ordenes.autorizar-nivel1</c> o
/// <c>:nivel2</c>) se valida en el endpoint Api según el estado actual
/// de la OC.
/// </summary>
public sealed record RechazarOrdenCompraCommand(
    Guid OrdenCompraId,
    Guid MotivoRechazoId,
    string? MotivoRechazoTexto,
    string? Notas) : IRequest;
