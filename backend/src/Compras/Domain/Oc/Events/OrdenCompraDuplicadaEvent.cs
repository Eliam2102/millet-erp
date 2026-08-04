using MediatR;

namespace Millet.Compras.Domain.Oc.Events;

/// <summary>
/// Evento in-process emitido al duplicar una OC (F6-PR2, C4 del diseño).
/// La OC nueva nace en <see cref="EstadoOrdenCompra.Borrador"/> con
/// líneas manuales (sin FK a RQ); el comprador la edita y la transmite.
/// Listeners interesados: notificaciones, reportería, audit.
/// </summary>
public sealed record OrdenCompraDuplicadaEvent(
    Guid OrdenCompraNuevaId,
    Guid OrdenCompraOrigenId,
    Guid EmpresaId,
    string FolioNuevo,
    string FolioOrigen,
    Guid CompradorTitularId,
    DateTimeOffset OcurridoEn) : INotification;
