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
/// NotFound, Disabled). Cambios al SP en BD (Activo, permisos) propagan
/// hasta 5 min después; cambios urgentes requieren restart del App
/// Service. Endpoint admin para invalidación granular queda como
/// <c>PLATFORM-TODO(&lt;SpCacheInvalidation&gt;)</c>.
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

    public CachedServicePrincipalResolver(
        IServicePrincipalResolver inner,
        IMemoryCache cache)
        : this(inner, cache, DefaultTtl)
    {
    }

    // Constructor con TTL custom (público para que tests externos puedan
    // usarlo con TTLs cortos para validar expiry; en runtime productivo
    // se usa el constructor de 2 args que usa DefaultTtl).
    public CachedServicePrincipalResolver(
        IServicePrincipalResolver inner,
        IMemoryCache cache,
        TimeSpan ttl)
    {
        _inner = inner;
        _cache = cache;
        _ttl = ttl;
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
        _cache.Set(key, fresh, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl });
        return fresh;
    }
}
