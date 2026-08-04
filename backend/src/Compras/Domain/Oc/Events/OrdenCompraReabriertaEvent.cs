using MediatR;

namespace Millet.Compras.Domain.Oc.Events;

/// <summary>
/// Evento in-process emitido cuando una OC <see cref="EstadoOrdenCompra.Cerrada"/>
/// regresa a <see cref="EstadoOrdenCompra.Autorizada"/> por una devolución
/// o nota de crédito que dejó las 3 dimensiones fuera del estado de
/// cierre (F5-PR2). Listeners de reportes y notificaciones lo usan para
/// reflejar el cambio.
/// </summary>
public sealed record OrdenCompraReabriertaEvent(
    Guid OrdenCompraId,
    Guid EmpresaId,
    string Folio,
    Guid CompradorTitularId,
    DateTimeOffset OcurridoEn) : INotification;
