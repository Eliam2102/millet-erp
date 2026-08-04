using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Application.Ports;

/// <summary>
/// Puerto out-going que publica cambios de estado de una <see cref="EntidadExterna"/>
/// al canal realtime de Glass Agent (Soketi/Pusher), para que la UI del
/// vendedor en Agent reciba el update sin polling.
///
/// <para>
/// <b>Best-effort por diseño</b>: la implementación NO debe lanzar
/// excepciones — el outcome del flow del worker ya está persistido en
/// BD y no debe fallar por un problema de push. Loguear warning y seguir.
/// </para>
///
/// <para>
/// <b>Canal</b>: <c>private-user-{operator_user_id}</c>. El
/// <c>operator_user_id</c> viene en <see cref="EntidadExterna.PayloadOriginal"/>
/// (JSON serializado del payload original del Agent — Glass Agent v2.5.0+
/// siempre lo incluye). Si falta, la implementación skipea el push
/// silenciosamente con warning.
/// </para>
///
/// <para>
/// <b>Evento</b>: <c>aw-cotizacion-actualizada</c> (único para todos los
/// cambios de estado; el <c>estado</c> int del payload permite a la UI
/// discriminar). Schema en snake_case por contrato con Agent —
/// distinto del API REST que usa camelCase.
/// </para>
/// </summary>
public interface IAgentRealtimePublisher
{
    /// <summary>
    /// Publica el estado actual de la cotización al canal Soketi del
    /// vendedor. Se invoca después de cada <c>SaveChangesAsync</c> que
    /// transiciona el estado (Submitted → Correlated/FailedCorrelation/FailedDrop,
    /// o por reintentos/resoluciones manuales).
    /// </summary>
    /// <remarks>
    /// La entidad debe estar persistida (con <c>UpdatedAt</c> actualizado)
    /// ANTES de invocar este método — el payload incluye el <c>updated_at</c>
    /// para que la UI ordene/dedupe.
    /// </remarks>
    Task PublishCotizacionActualizadaAsync(
        EntidadExterna entidad,
        CancellationToken cancellationToken);
}
