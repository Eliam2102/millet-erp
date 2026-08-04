using MediatR;

namespace Millet.Compras.Domain.Oc.Events;

/// <summary>
/// Evento in-process emitido tras agregar una línea (manual o desde
/// RQ) cuando la línea trae <c>RequisicionId</c>. El handler que
/// produce este evento ya llama
/// <see cref="Domain.Requisicion.ComprometerEnOc"/> en la misma TX —
/// el evento es para auditoría y para futuros listeners cross-BC
/// (Notificaciones, métricas).
/// </summary>
public sealed record RqComprometidaEnOcEvent(
    Guid RequisicionId,
    Guid OrdenCompraId,
    Guid EmpresaId,
    DateTimeOffset OcurridoEn) : INotification;
