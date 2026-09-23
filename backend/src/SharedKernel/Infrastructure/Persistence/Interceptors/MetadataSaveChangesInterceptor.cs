using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;

namespace Millet.SharedKernel.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Asigna y mantiene los campos de metadata de <see cref="BaseEntity"/>
/// (<c>Version</c>, <c>CreatedAt</c>, <c>UpdatedAt</c>, <c>CreatedBy</c>,
/// <c>UpdatedBy</c>) en cada SaveChanges. Se ejecuta antes de los demás
/// interceptors para que la auditoría capture los valores ya asignados.
/// Ver ADR-0005 y ADR-0012.
/// </summary>
public sealed class MetadataSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IClock _clock;
    private readonly ICurrentUserContext _userContext;
    private readonly IAuditOriginContext _originContext;

    public MetadataSaveChangesInterceptor(
        IClock clock,
        ICurrentUserContext userContext,
        IAuditOriginContext originContext)
    {
        _clock = clock;
        _userContext = userContext;
        _originContext = originContext;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null) ApplyMetadata(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) ApplyMetadata(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void ApplyMetadata(DbContext context)
    {
        var now = _clock.UtcNow;
        var userName = _userContext.UserName ?? _originContext.Origin ?? "system";

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
                entry.Entity.CreatedBy = userName;
                entry.Entity.UpdatedBy = userName;
                entry.Entity.Version = 1;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = userName;
                entry.Entity.Version++;
            }
        }

        // Entidades con PK natural no-GUID que no derivan de BaseEntity
        // (FAC-ING-PR2). BaseEntity NO implementa ITieneMetadata, así que
        // los dos loops son disjuntos — nada se procesa dos veces.
        foreach (var entry in context.ChangeTracker.Entries<ITieneMetadata>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
                entry.Entity.CreatedBy = userName;
                entry.Entity.UpdatedBy = userName;
                entry.Entity.Version = 1;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = userName;
                entry.Entity.Version++;
            }
        }
    }
}
