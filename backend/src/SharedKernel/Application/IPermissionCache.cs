namespace Millet.SharedKernel.Application;

/// <summary>
/// Cache de permisos efectivos por (usuario, empresa). Implementación
/// por defecto in-memory con TTL de 5 min e invalidación explícita.
/// Diseñada para migrar a Redis cuando se escale a múltiples instancias
/// sin tocar handlers (la abstracción oculta la implementación).
/// Ver ADR-0007.
/// </summary>
public interface IPermissionCache
{
    /// <summary>Retorna los permisos cacheados o null si no hay entrada (o expiró).</summary>
    Task<IReadOnlyCollection<string>?> GetAsync(
        Guid userId,
        Guid empresaId,
        CancellationToken cancellationToken = default);

    /// <summary>Almacena los permisos para el par (userId, empresaId) con TTL default.</summary>
    Task SetAsync(
        Guid userId,
        Guid empresaId,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken = default);

    /// <summary>Invalida la entrada exacta (userId, empresaId).</summary>
    Task InvalidateAsync(
        Guid userId,
        Guid empresaId,
        CancellationToken cancellationToken = default);

    /// <summary>Invalida todas las entradas del usuario en cualquier empresa.</summary>
    Task InvalidateAllForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
