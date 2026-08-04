using MediatR;

namespace Millet.Compras.Domain.Oc.Events;

/// <summary>
/// Evento in-process emitido tras una cancelación
/// (transición <c>* (no terminal) → Cancelada</c>) cuando la OC no tenía
/// recepciones registradas (<c>SubEstadoRecepcion = SinRecepcion</c>).
/// El caso con recepciones parciales requiere doble autorización y
/// libera RQs proporcionalmente — entra en F5-PR4.
///
/// Consumido por:
/// <list type="bullet">
///   <item>Notificaciones (F8) — email al comprador.</item>
///   <item>Listener cross-aggregate de Requisiciones (F4-PR3) —
///         libera RQs comprometidas.</item>
/// </list>
/// </summary>
public sealed record OrdenCompraCanceladaEvent(
    Guid OrdenCompraId,
    Guid EmpresaId,
    string Folio,
    EstadoOrdenCompra EstadoPrevio,
    Guid MotivoCancelacionId,
    string? MotivoCancelacionTexto,
    Guid CompradorTitularId,
    Guid UsuarioCanceladorId,
    DateTimeOffset OcurridoEn) : INotification;
