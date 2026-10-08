using Microsoft.Extensions.Primitives;

namespace Millet.Identidad.Infrastructure.Adapters;

/// <summary>
/// Señal singleton que expira de golpe todas las resoluciones de service
/// principals cacheadas por <see cref="CachedServicePrincipalResolver"/>
/// (U1.0). Los permisos de un SP viajan dentro de esa resolución, así que
/// cualquier cambio de rol o permisos debe descartarlas; son pocas entradas
/// y la siguiente petición las recarga de BD.
/// </summary>
public sealed class ServicePrincipalCacheSignal : IDisposable
{
    private CancellationTokenSource _cts = new();

    /// <summary>Token que se adjunta a cada entrada del cache de SPs.</summary>
    public IChangeToken Token => new CancellationChangeToken(Volatile.Read(ref _cts).Token);

    /// <summary>Expira todas las entradas emitidas hasta ahora.</summary>
    public void Reset()
    {
        var previous = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }

    public void Dispose() => Volatile.Read(ref _cts).Dispose();
}
