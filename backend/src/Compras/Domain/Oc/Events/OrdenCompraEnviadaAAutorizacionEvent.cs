using MediatR;

namespace Millet.Compras.Domain.Oc.Events;

/// <summary>
/// Evento in-process emitido tras la transición
/// <c>Borrador → EnAutorizacionJefeCompras</c>. Lo consume el listener
/// de notificaciones (cuando exista — F8) para enviar email al N1
/// designado, y handlers de logging.
/// </summary>
public sealed record OrdenCompraEnviadaAAutorizacionEvent(
    Guid OrdenCompraId,
    Guid EmpresaId,
    string Folio,
    Guid CompradorTitularId,
    DateTimeOffset OcurridoEn) : INotification;
