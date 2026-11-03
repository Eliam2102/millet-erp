using MediatR;

namespace Millet.Compras.Domain.Oc.Events;

/// <summary>
/// Evento in-process (86e3tacbn): el área solicitante confirmó (<c>Confirmado = true</c>) o deshizo la
/// confirmación del servicio de la OC. <c>Texto</c> es el comentario al confirmar o el motivo al deshacer.
/// </summary>
public sealed record OrdenCompraServicioConfirmadoEvent(
    Guid OrdenCompraId,
    Guid EmpresaId,
    string Folio,
    bool Confirmado,
    Guid UsuarioId,
    string? Texto,
    DateTimeOffset OcurridoEn) : INotification;
