using MediatR;

namespace Millet.Compras.Domain.Oc.Events;

/// <summary>
/// Evento in-process emitido cuando la OC transiciona automáticamente
/// a <see cref="EstadoOrdenCompra.Cerrada"/> (F5-PR1). El cierre dispara
/// cuando las 3 dimensiones cierran: <c>SubEstadoRecepcion.Completa</c> +
/// <c>SubEstadoFacturacion.Completa</c> + <c>SubEstadoPago.Pagada</c> y
/// el estado actual es <see cref="EstadoOrdenCompra.Autorizada"/>.
///
/// Lo consumen listeners de notificaciones (F8) y de archivado /
/// reportería. F5-PR2 también lo usa para sincronizar el sub-estado en
/// integraciones cross-BC.
/// </summary>
public sealed record OrdenCompraCerradaEvent(
    Guid OrdenCompraId,
    Guid EmpresaId,
    string Folio,
    Guid CompradorTitularId,
    DateTimeOffset OcurridoEn) : INotification;
