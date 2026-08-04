using System.Collections.Concurrent;
using Millet.SharedKernel.Application;

namespace Millet.SharedKernel.Infrastructure;

/// <summary>
/// Implementación in-memory de <see cref="IPermissionCache"/>. TTL fijo de
/// 5 minutos e invalidación explícita vía las APIs de la interfaz. Aceptable
/// para una sola instancia de App Service; al escalar horizontalmente se
/// migra a Redis sin tocar handlers (sustituir el registro DI). Ver ADR-0007.
/// </summary>
public sealed class InMemoryPermissionCache : IPermissionCache
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly IClock _clock;
    private readonly ConcurrentDictionary<(Guid UserId, Guid EmpresaId), CacheEntry> _entries = new();

    public InMemoryPermissionCache(IClock clock)
    {
        _clock = clock;
    }

    public Task<IReadOnlyCollection<string>?> GetAsync(
        Guid userId,
        Guid empresaId,
        CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue((userId, empresaId), out var entry) && !entry.IsExpired(_clock.UtcNow))
        {
            return Task.FromResult<IReadOnlyCollection<string>?>(entry.Permissions);
        }

        // Limpieza perezosa: si está expirada, sácala del cache.
        if (entry is not null)
        {
            _entries.TryRemove((userId, empresaId), out _);
        }

        return Task.FromResult<IReadOnlyCollection<string>?>(null);
    }

    public Task SetAsync(
        Guid userId,
        Guid empresaId,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var expiresAt = _clock.UtcNow.Add(DefaultTtl);
        _entries[(userId, empresaId)] = new CacheEntry(permissions, expiresAt);
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(
        Guid userId,
        Guid empresaId,
        CancellationToken cancellationToken = default)
    {
        _entries.TryRemove((userId, empresaId), out _);
        return Task.CompletedTask;
    }

    public Task InvalidateAllForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var keys = _entries.Keys.Where(k => k.UserId == userId).ToList();
        foreach (var key in keys)
        {
            _entries.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }

    private sealed record CacheEntry(IReadOnlyCollection<string> Permissions, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
    }
}
