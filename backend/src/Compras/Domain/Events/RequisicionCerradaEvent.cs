using MediatR;

namespace Millet.Compras.Domain.Events;

/// <summary>
/// Evento de dominio in-proc emitido cuando una requisición transiciona
/// a <see cref="EstadoRequisicion.Cerrada"/> tras la última recepción
/// que cubre <c>CantidadPendiente == 0</c> en todas las líneas
/// (diseño §4.1, §5.2). Lo emite <see cref="Requisicion.RegistrarRecepcion"/>.
///
/// <para>
/// F5-PR1 emite el evento; el handler de logging vive en
/// <c>Application/Eventos/</c>. Eventualmente: notificaciones al
/// solicitante, cierre de OCs colgantes, etc.
/// </para>
/// </summary>
public sealed record RequisicionCerradaEvent(
    Guid RequisicionId,
    Guid EmpresaId,
    DateTimeOffset OcurridoEn) : INotification;
