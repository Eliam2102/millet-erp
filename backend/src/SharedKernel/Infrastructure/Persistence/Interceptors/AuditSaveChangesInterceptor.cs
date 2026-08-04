using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.SharedKernel.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Captura los cambios de las entidades <see cref="IAuditable"/> y los
/// inserta como filas en <c>core.audit_log</c> dentro de la misma
/// transacción de SaveChanges. Garantiza atomicidad: entidades modificadas
/// y registros de auditoría son INSERT en la misma transacción de PostgreSQL.
///
/// Formato de <c>Cambios</c> (jsonb):
/// - Crear: snapshot completo de la entidad — <c>{"snapshot": {...}}</c>
/// - Actualizar: diff campo a campo — <c>{"diff": {"campo": {"antes": ..., "despues": ...}}}</c>
/// - Borrar: snapshot pre-borrado — <c>{"snapshot_pre_borrado": {...}}</c>
///
/// Ver ADR-0008.
/// </summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IClock _clock;
    private readonly ICurrentUserContext _userContext;
    private readonly ICurrentEmpresaContext _empresaContext;

    public AuditSaveChangesInterceptor(
        IClock clock,
        ICurrentUserContext userContext,
        ICurrentEmpresaContext empresaContext)
    {
        _clock = clock;
        _userContext = userContext;
        _empresaContext = empresaContext;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null) AddAuditEntries(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) AddAuditEntries(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void AddAuditEntries(DbContext context)
    {
        var auditEntries = new List<AuditLogEntry>();
        var now = _clock.UtcNow;
        var correlationId = Guid.CreateVersion7();

        // Snapshot la lista ahora porque la voy a modificar (agrego AuditLogEntry).
        var trackedEntries = context.ChangeTracker.Entries().ToList();

        foreach (var entry in trackedEntries)
        {
            if (entry.Entity is not IAuditable) continue;
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            var entityId = (entry.Entity as BaseEntity)?.Id;
            // B.2: AggregateRootId = RootId si la entidad implementa
            // IBelongsToAggregate; sino el propio Id (la entidad ES root
            // por convención). Permite consultar el histórico de un
            // agregado completo (root + hijos) con un solo WHERE.
            var aggregateRootId = entry.Entity is IBelongsToAggregate aggregate
                ? aggregate.AggregateRootId
                : entityId;

            var auditEntry = new AuditLogEntry
            {
                Id = Guid.CreateVersion7(),
                Timestamp = now,
                UsuarioId = _userContext.UserId,
                EmpresaId = _empresaContext.IsBypassed ? null : _empresaContext.Current,
                Modulo = ResolveModule(entry.Entity.GetType()),
                Entidad = entry.Entity.GetType().Name,
                EntidadId = entityId,
                AggregateRootId = aggregateRootId,
                Operacion = entry.State switch
                {
                    EntityState.Added => "crear",
                    EntityState.Modified => "actualizar",
                    EntityState.Deleted => "borrar",
                    _ => "unknown"
                },
                Cambios = SerializeChanges(entry),
                CorrelationId = correlationId,
                EsBulk = false
            };

            auditEntries.Add(auditEntry);
        }

        if (auditEntries.Count > 0)
        {
            context.Set<AuditLogEntry>().AddRange(auditEntries);
        }
    }

    private static string SerializeChanges(EntityEntry entry)
    {
        return entry.State switch
        {
            EntityState.Added => JsonSerializer.Serialize(new
            {
                snapshot = entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue)
            }),
            EntityState.Modified => JsonSerializer.Serialize(new
            {
                diff = entry.Properties
                    .Where(p => p.IsModified)
                    .ToDictionary(
                        p => p.Metadata.Name,
                        p => (object)new { antes = p.OriginalValue, despues = p.CurrentValue })
            }),
            EntityState.Deleted => JsonSerializer.Serialize(new
            {
                snapshot_pre_borrado = entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue)
            }),
            _ => "{}"
        };
    }

    private static string ResolveModule(Type entityType)
    {
        // Convención: el namespace tipo Millet.{Modulo}.Domain.* identifica el módulo.
        // Ej. Millet.SharedKernel.Domain.Empresa → módulo "SharedKernel"
        //     Millet.Identidad.Domain.Usuario  → módulo "Identidad"
        var ns = entityType.Namespace ?? string.Empty;
        var parts = ns.Split('.');
        return parts.Length >= 2 && parts[0] == "Millet" ? parts[1] : "Unknown";
    }
}
