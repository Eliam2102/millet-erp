using MediatR;

namespace Millet.Integraciones.Aw.Application.Commands.ReintentarCotizacion;

/// <summary>
/// Comando del endpoint <c>POST /cotizaciones/{id}/reintentar</c>. Reactiva
/// una cotización en estado <c>FailedDrop</c> o <c>ManuallyResolved</c>
/// devolviéndola a <c>Submitted</c> y emitiendo <c>AwCotizacionRecibida</c>
/// al Outbox — el <c>AwDropWorker</c> recoge el evento y reintenta el
/// drop al on-prem.
///
/// <para>
/// El handler valida el estado vía el método de dominio
/// <c>EntidadExterna.Reintentar()</c> (PR D — agregado al aggregate),
/// que throws <c>InvalidStateTransitionException</c> si el estado actual
/// no permite reintento. La excepción mapea automáticamente a HTTP 409
/// Conflict vía la jerarquía <c>ConflictException</c> (PR D — exception
/// hierarchy fix).
/// </para>
///
/// <para>
/// <b>Razón opcional:</b> el operador puede agregar una nota corta
/// explicando el motivo del reintento (audit trail). Hoy se loguea pero
/// no se persiste; <c>// PLATFORM-TODO(&lt;ReintentoAuditTrail&gt;)</c>
/// agregar tabla de historial de acciones admin si surge necesidad.
/// </para>
/// </summary>
public sealed record ReintentarCotizacionCommand(
    Guid CotizacionId,
    string? Razon
) : IRequest<ReintentarCotizacionResponse>;
