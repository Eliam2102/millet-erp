namespace Millet.SharedKernel.Application;

/// <summary>
/// Información sobre el usuario que está realizando el request actual.
/// La implementación de producción la resuelve desde el JWT del API
/// (ver ADR-0007). En Phase 1 hay una implementación placeholder que
/// retorna null hasta que el flujo de auth real esté wired (PR 4+).
/// </summary>
public interface ICurrentUserContext
{
    /// <summary>Object ID de Entra del usuario; null si no hay request o no hay auth.</summary>
    Guid? UserId { get; }

    /// <summary>Nombre del usuario para campos de auditoría (CreatedBy/UpdatedBy).</summary>
    string? UserName { get; }
}
