using MediatR;

namespace Millet.Compras.Domain.Events;

/// <summary>
/// Evento de dominio in-proc emitido cuando el jefe de almacén / almacenista
/// <b>cierra manualmente</b> una requisición autorizada o en surtido que el
/// requisitante ya no necesita (ADR-0043 R3). Distinto del
/// <see cref="RequisicionCerradaEvent"/> (cierre por surtido/entrega completa):
/// este lleva motivo + actor + el estado terminal elegido
/// (<see cref="EstadoRequisicion.CerradaSinSurtir"/> si no se entregó nada,
/// <see cref="EstadoRequisicion.CerradaSurtidaParcial"/> si se entregó una
/// parte). El material no entregado queda como stock libre.
///
/// <para>El handler libera las reservas del tramo de stock dentro de la misma
/// TX (como en cancelación); este evento es notificación informativa para
/// logging / integración / BI.</para>
/// </summary>
public sealed record RequisicionCerradaManualmenteEvent(
    Guid RequisicionId,
    Guid EmpresaId,
    EstadoRequisicion EstadoFinal,
    Guid MotivoId,
    string? MotivoTexto,
    Guid ActorId,
    DateTimeOffset OcurridoEn) : INotification;
