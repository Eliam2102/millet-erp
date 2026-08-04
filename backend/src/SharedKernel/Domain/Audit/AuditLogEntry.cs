using System.Net;

namespace Millet.SharedKernel.Domain.Audit;

/// <summary>
/// Registro de auditoría escrito automáticamente por el AuditSaveChangesInterceptor
/// para cada entidad <see cref="IAuditable"/> que cambia. Append-only por
/// convención: la cuenta de aplicación de PostgreSQL solo tiene INSERT y
/// SELECT, no UPDATE ni DELETE. Vive en <c>core.audit_log</c> y está
/// particionada mensualmente por <see cref="Timestamp"/>.
/// Ver ADR-0008.
/// </summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public Guid? UsuarioId { get; init; }

    public Guid? EmpresaId { get; init; }

    public string Modulo { get; init; } = string.Empty;

    public string Entidad { get; init; } = string.Empty;

    public Guid? EntidadId { get; init; }

    /// <summary>
    /// Id del agregado raíz al que pertenece <see cref="EntidadId"/>.
    /// Igual a <see cref="EntidadId"/> cuando la entidad ES un aggregate
    /// root; cuando implementa <see cref="IBelongsToAggregate"/>, captura
    /// el id del root para que el histórico del root incluya hijos con
    /// un solo <c>WHERE aggregate_root_id = X</c>. Backfilled como NULL
    /// para filas pre-B.2. Permitido nulo en BD por compat con histórico.
    /// </summary>
    public Guid? AggregateRootId { get; init; }

    public string Operacion { get; init; } = string.Empty;

    /// <summary>Diff o snapshot serializado en JSON (jsonb en PG). Ver ADR-0008.</summary>
    public string Cambios { get; init; } = "{}";

    /// <summary>IP del cliente del request (mapea a <c>inet</c> en PG). Resuelto desde HttpContext en PR 4+.</summary>
    public IPAddress? Ip { get; init; }

    public Guid CorrelationId { get; init; }

    public bool EsBulk { get; init; }

    public string? Metadatos { get; init; }
}
