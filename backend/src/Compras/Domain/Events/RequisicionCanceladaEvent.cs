using MediatR;

namespace Millet.Compras.Domain.Events;

/// <summary>
/// Evento de dominio in-proc emitido cuando una requisición autorizada
/// (estado <see cref="EstadoRequisicion.Autorizada"/> o
/// <see cref="EstadoRequisicion.EnSurtido"/>) se cancela. El handler
/// (F4-PR3) ya liberó las reservas en Almacén y borró las OCs borrador
/// del submódulo OC <b>dentro de la misma TX</b>; el evento es
/// notificación informativa para handlers de logging / telemetría /
/// notificaciones (F4-PR4).
/// </summary>
public sealed record RequisicionCanceladaEvent(
    Guid RequisicionId,
    Guid EmpresaId,
    Guid MotivoId,
    string? MotivoTexto,
    Guid ActorId,
    DateTimeOffset OcurridoEn) : INotification;
