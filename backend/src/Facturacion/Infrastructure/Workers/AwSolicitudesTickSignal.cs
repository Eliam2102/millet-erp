namespace Millet.Facturacion.Infrastructure.Workers;

/// <summary>
/// Señal singleton que despierta al <see cref="AwSolicitudesWorker"/> antes de
/// que venza su intervalo de polling (nudge de baja latencia, ADR-0048 §push).
/// La dispara el endpoint <c>POST /api/v1/integraciones/aw/pedidos/nudge</c>
/// cuando la customización de A+W avisa que insertó una solicitud en la
/// tabla-puente. <b>Best-effort</b>: si el nudge nunca llega, el polling
/// normal (IntervalSeconds) sigue siendo la red de seguridad — la tabla es
/// la fuente de verdad, no la señal.
///
/// <para>Semántica "a lo sumo un pendiente": múltiples nudges antes del tick
/// colapsan en uno (el tick barre TODO lo pendiente de una vez, así que no
/// se pierde nada).</para>
/// </summary>
public sealed class AwSolicitudesTickSignal : IDisposable
{
    private readonly SemaphoreSlim _senal = new(0, 1);

    public void Dispose() => _senal.Dispose();

    /// <summary>Pide un tick inmediato (idempotente si ya hay uno pendiente).</summary>
    public void Solicitar()
    {
        try
        {
            _senal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Ya había un nudge pendiente — el próximo tick cubre ambos.
        }
    }

    /// <summary>
    /// Espera un nudge o el timeout del polling, lo que llegue primero.
    /// <c>true</c> = despertado por nudge; <c>false</c> = timeout normal.
    /// </summary>
    public Task<bool> EsperarAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _senal.WaitAsync(timeout, cancellationToken);
}
