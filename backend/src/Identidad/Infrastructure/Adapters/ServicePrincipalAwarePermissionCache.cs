using Millet.SharedKernel.Application;

namespace Millet.Identidad.Infrastructure.Adapters;

/// <summary>
/// Decorator de <see cref="IPermissionCache"/> (U1.0): toda invalidación de
/// permisos de un usuario también expira el cache de service principals,
/// cuyos permisos no pasan por <see cref="IPermissionCache"/> sino por la
/// resolución cacheada en <see cref="CachedServicePrincipalResolver"/>.
/// Así los handlers que ya invalidan (asignar/revocar rol, permisos del rol,
/// overrides, desactivar) cubren a humanos y SPs con una sola llamada.
/// </summary>
public sealed class ServicePrincipalAwarePermissionCache : IPermissionCache
{
    private readonly IPermissionCache _inner;
    private readonly ServicePrincipalCacheSignal _signal;

    public ServicePrincipalAwarePermissionCache(IPermissionCache inner, ServicePrincipalCacheSignal signal)
    {
        _inner = inner;
        _signal = signal;
    }

    public Task<IReadOnlyCollection<string>?> GetAsync(
        Guid userId, Guid empresaId, CancellationToken cancellationToken = default) =>
        _inner.GetAsync(userId, empresaId, cancellationToken);

    public Task SetAsync(
        Guid userId, Guid empresaId, IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken = default) =>
        _inner.SetAsync(userId, empresaId, permissions, cancellationToken);

    public async Task InvalidateAsync(
        Guid userId, Guid empresaId, CancellationToken cancellationToken = default)
    {
        await _inner.InvalidateAsync(userId, empresaId, cancellationToken);
        _signal.Reset();
    }

    public async Task InvalidateAllForUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        await _inner.InvalidateAllForUserAsync(userId, cancellationToken);
        _signal.Reset();
    }
}
