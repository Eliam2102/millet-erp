using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Idempotency;
using Millet.SharedKernel.Domain.Audit;
using Millet.SharedKernel.Infrastructure.Idempotency;

namespace Millet.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// DbContext del esquema <c>core</c> en PostgreSQL: tablas transversales
/// del propio sistema. Contiene <c>audit_log</c> (ADR-0008) e
/// <c>idempotency_keys</c> (ADR-0020, F8-PR1). La tabla <c>locks_duros</c>
/// de ADR-0012 se agregará junto con su feature de hard locks.
///
/// Sólo este DbContext owna la migración de <c>core.audit_log</c>; los demás
/// DbContexts mapean la entidad para que el AuditSaveChangesInterceptor
/// pueda escribir vía <c>Set&lt;AuditLogEntry&gt;()</c>, pero excluyen la tabla
/// de sus propias migraciones.
/// </summary>
public sealed class CoreDbContext : BaseDbContext
{
    public CoreDbContext(
        DbContextOptions<CoreDbContext> options,
        ICurrentEmpresaContext empresaContext) : base(options, empresaContext) { }

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    protected override bool ManagesAuditLogMigration => true;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("core");
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new IdempotencyKeyConfiguration());
    }
}
