namespace Millet.SharedKernel.Application;

/// <summary>
/// Permite escribir registros de auditoría explícitos (como eventos de acceso o denegación)
/// que no se originan en el ChangeTracker de SaveChanges.
/// Ver ADR-0008 y F1-ADM-03.
/// </summary>
public interface IAuditLogWriter
{
    Task RegistrarAsync(
        string operacion,
        string modulo,
        string entidad,
        Guid? entidadId,
        Guid? aggregateRootId,
        string actorNombre,
        string actorTipo,
        string? actorEmail,
        string entidadEtiqueta,
        string resumen,
        Guid? usuarioId = null,
        Guid? empresaId = null,
        string cambios = "{}",
        string? metadatos = null,
        CancellationToken cancellationToken = default);
}
