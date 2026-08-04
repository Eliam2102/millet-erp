namespace Millet.Integraciones.Aw.Application.Workers;

/// <summary>
/// Señal ligera para despertar al <see cref="AwDocumentSyncWorker"/>
/// inmediatamente cuando una cotización correlaciona (el pedido ya existe en
/// A+W → su PDF es inminente), en vez de esperar el intervalo idle de ~120s.
///
/// <para>
/// Singleton. La produce el flujo de correlación (<c>AwDropWorker</c> en el
/// camino síncrono, <c>AwLateReconciliationWorker</c> en el tardío) y la
/// consume el loop del worker de PDF. Varios kicks entre ciclos se
/// <b>coalescen</b> en uno (SemaphoreSlim con maxCount=1): despertar una vez
/// basta, el worker ya re-escanea a todos los pendientes.
/// </para>
/// </summary>
public sealed class DocumentSyncKick : IDisposable
{
    private readonly SemaphoreSlim _signal = new(initialCount: 0, maxCount: 1);

    /// <summary>
    /// Solicita un ciclo de sincronización inmediato. Idempotente entre
    /// ciclos: si ya había un kick pendiente, este se descarta silenciosamente.
    /// </summary>
    public void Kick()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Ya había un kick pendiente sin consumir — coalesce, no-op.
        }
    }

    /// <summary>
    /// Espera un kick hasta <paramref name="timeout"/>. <c>true</c> si hubo
    /// kick (despertar temprano), <c>false</c> si venció el timeout (tick
    /// idle). El worker re-sincroniza en ambos casos, así que el caller puede
    /// ignorar el resultado.
    /// </summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => _signal.WaitAsync(timeout, cancellationToken);

    public void Dispose() => _signal.Dispose();
}
