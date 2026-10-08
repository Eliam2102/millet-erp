using Microsoft.Extensions.Caching.Memory;
using Millet.Identidad.Application.Ports;

namespace Millet.Identidad.Infrastructure.Adapters;

/// <summary>
/// Decorator de <see cref="IServicePrincipalResolver"/> que cachea el
/// resultado en <see cref="IMemoryCache"/> singleton del proceso. Evita
/// golpear BD en cada request del SP (típicamente uno por POST de Glass
/// Agent).
///
/// <para>
/// <b>TTL = 5 minutos.</b> Se aplica a todos los resultados (Found,
/// NotFound, Disabled). Cambios de rol, de permisos o de estado del
/// usuario expiran todo el cache al instante vía
/// <see cref="ServicePrincipalCacheSignal"/> (U1.0); el TTL solo cubre
/// cambios hechos fuera de esos comandos (p. ej. bootstrap del catálogo).
/// </para>
///
/// <para>
/// Cachear los NotFound/Disabled tiene un trade-off: bloquea ataques de
/// AppId-spraying (no gastamos BD) pero también significa que dar de
/// alta un SP nuevo en BD tarda hasta 5 min en empezar a funcionar.
/// Aceptable para fase 1; documentado en RESUMEN.md.
/// </para>
///
/// <para>
/// DI: este decorator es <b>scoped</b> (lo es porque depende de
/// <see cref="DefaultServicePrincipalResolver"/> scoped), pero el
/// <see cref="IMemoryCache"/> es singleton — el cache persiste entre
/// requests. Key prefix evita colisiones con otros usos de IMemoryCache.
/// </para>
/// </summary>
public sealed class CachedServicePrincipalResolver : IServicePrincipalResolver
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private const string CacheKeyPrefix = "sp:";

    private readonly IServicePrincipalResolver _inner;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;
    private readonly ServicePrincipalCacheSignal? _signal;

    public CachedServicePrincipalResolver(
        IServicePrincipalResolver inner,
        IMemoryCache cache,
        ServicePrincipalCacheSignal? signal = null)
        : this(inner, cache, DefaultTtl, signal)
    {
    }

    // Constructor con TTL custom (público para que tests externos puedan
    // usarlo con TTLs cortos para validar expiry; en runtime productivo
    // se usa el constructor de 2 args que usa DefaultTtl).
    public CachedServicePrincipalResolver(
        IServicePrincipalResolver inner,
        IMemoryCache cache,
        TimeSpan ttl,
        ServicePrincipalCacheSignal? signal = null)
    {
        _inner = inner;
        _cache = cache;
        _ttl = ttl;
        _signal = signal;
    }

    public async Task<ServicePrincipalResolutionResult> ResolveAsync(
        Guid entraAppId,
        Guid entraObjectId,
        CancellationToken cancellationToken = default)
    {
        var key = CacheKeyPrefix + entraAppId;
        if (_cache.TryGetValue<ServicePrincipalResolutionResult>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var fresh = await _inner.ResolveAsync(entraAppId, entraObjectId, cancellationToken);
        var options = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl };
        if (_signal is not null)
            options.AddExpirationToken(_signal.Token);
        _cache.Set(key, fresh, options);
        return fresh;
    }
}
