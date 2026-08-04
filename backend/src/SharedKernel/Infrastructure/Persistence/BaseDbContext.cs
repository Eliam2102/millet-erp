using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// Base abstracto para todos los DbContext del sistema. Aplica las
/// convenciones transversales que define la arquitectura:
///
/// - Concurrency token sobre <c>BaseEntity.Version</c> (ADR-0012)
/// - Global query filter de soft delete para entidades <see cref="IFiscalmenteRelevante"/> (ADR-0008)
/// - Global query filter de multi-tenancy para entidades <see cref="IPerteneceAEmpresa"/> (ADR-0011):
///   <c>bypass || EmpresaId == current</c>. Combinado con AND si la entidad
///   también es <see cref="IFiscalmenteRelevante"/>.
/// - Mapeo de la tabla <c>core.audit_log</c> en todos los contextos para que el
///   AuditSaveChangesInterceptor pueda escribir vía <c>Set&lt;AuditLogEntry&gt;()</c>
///   sin importar qué DbContext originó la operación. Solo CoreDbContext
///   owna la migración de la tabla (override <see cref="ManagesAuditLogMigration"/> => true).
///
/// Naming snake_case se aplica vía <c>UseSnakeCaseNamingConvention()</c> en
/// la configuración del DbContext (en Program.cs), no aquí.
/// </summary>
public abstract class BaseDbContext : DbContext
{
    private readonly ICurrentEmpresaContext _empresaContext;

    protected BaseDbContext(DbContextOptions options, ICurrentEmpresaContext empresaContext)
        : base(options)
    {
        _empresaContext = empresaContext;
    }

    /// <summary>
    /// Override en CoreDbContext con => true. Las demás derivadas heredan
    /// false y excluyen <c>core.audit_log</c> de sus migraciones.
    /// </summary>
    protected virtual bool ManagesAuditLogMigration => false;

    /// <summary>
    /// Property pública usada por el global query filter por empresa. EF Core
    /// parametriza el acceso a properties del DbContext instance — el getter
    /// se evalúa per-query con la instancia que ejecuta la query, así que el
    /// valor refleja el estado actual del request.
    /// </summary>
    public bool QueryFilterIsBypassed => _empresaContext.IsBypassed;

    /// <summary>
    /// Property pública usada por el global query filter por empresa.
    /// Comparación con <c>EmpresaId</c> via cast a Guid? — null sin bypass
    /// devuelve false (default seguro: sin empresa context, no se ven datos).
    /// </summary>
    public Guid? QueryFilterCurrentEmpresaId => _empresaContext.Current;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureAuditLogEntry(modelBuilder);
        ApplyConcurrencyToken(modelBuilder);
        ApplyQueryFilters(modelBuilder);
    }

    private void ConfigureAuditLogEntry(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AuditLogEntry>();
        entity.ToTable("audit_log", "core");
        // PK compuesta (id, timestamp) por requisito de PostgreSQL: la columna
        // de partición debe ser parte de la PK en tablas particionadas.
        // Ver ADR-0008 (particionado mensual).
        entity.HasKey(x => new { x.Id, x.Timestamp });
        entity.Property(x => x.Cambios).HasColumnType("jsonb");
        entity.Property(x => x.Metadatos).HasColumnType("jsonb");
        entity.Property(x => x.Ip).HasColumnType("inet");
        entity.Property(x => x.Modulo).HasMaxLength(64).IsRequired();
        entity.Property(x => x.Entidad).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Operacion).HasMaxLength(32).IsRequired();
        entity.HasIndex(x => new { x.EmpresaId, x.Timestamp });
        entity.HasIndex(x => new { x.UsuarioId, x.Timestamp });
        entity.HasIndex(x => new { x.Modulo, x.Entidad, x.EntidadId });
        // B.2: histórico por agregado (root + hijos) en single index lookup.
        entity.HasIndex(x => new { x.Modulo, x.AggregateRootId, x.Timestamp });

        if (!ManagesAuditLogMigration)
        {
            entity.ToTable(t => t.ExcludeFromMigrations());
        }
    }

    private static void ApplyConcurrencyToken(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // En jerarquías de herencia (TPT/TPH) las propiedades heredadas
            // —como Version— se configuran en la raíz y aplican a toda la
            // jerarquía; configurarlas sobre un tipo derivado lanza en EF Core.
            // Los tipos sin herencia son su propia raíz (BaseType == null).
            if (entityType.BaseType is not null)
            {
                continue;
            }

            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(BaseEntity.Version))
                    .IsConcurrencyToken();
            }
        }
    }

    /// <summary>
    /// Aplica los global query filters de soft delete y multi-tenancy a las
    /// entidades correspondientes. Cuando una entidad implementa AMBAS
    /// interfaces, los filters se combinan con AND. EF Core solo permite UN
    /// query filter por tipo, así que un loop único garantiza que ningún
    /// filter sobreescriba al otro.
    /// </summary>
    private void ApplyQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            // EF Core solo permite definir un query filter en la RAÍZ de una
            // jerarquía de herencia (TPT/TPH); ese filtro aplica a todos los
            // subtipos automáticamente. Aplicarlo a un tipo derivado lanza.
            // Los tipos sin herencia son su propia raíz (BaseType == null).
            if (entityType.BaseType is not null)
            {
                continue;
            }

            var clrType = entityType.ClrType;
            var isFiscalRelevante = typeof(IFiscalmenteRelevante).IsAssignableFrom(clrType);
            var isPerteneceAEmpresa = typeof(IPerteneceAEmpresa).IsAssignableFrom(clrType);

            if (!isFiscalRelevante && !isPerteneceAEmpresa)
            {
                continue;
            }

            var parameter = Expression.Parameter(clrType, "e");
            Expression? combined = null;

            if (isFiscalRelevante)
            {
                // e.DeletedAt == null
                var deletedAt = Expression.Property(parameter, nameof(BaseEntity.DeletedAt));
                var nullCheck = Expression.Equal(deletedAt, Expression.Constant(null, typeof(DateTimeOffset?)));
                combined = nullCheck;
            }

            if (isPerteneceAEmpresa)
            {
                // this.QueryFilterIsBypassed || ((IPerteneceAEmpresa)e).EmpresaId == this.QueryFilterCurrentEmpresaId
                var thisConst = Expression.Constant(this);
                var bypassedProp = typeof(BaseDbContext).GetProperty(nameof(QueryFilterIsBypassed))!;
                var currentProp = typeof(BaseDbContext).GetProperty(nameof(QueryFilterCurrentEmpresaId))!;
                var bypassedAccess = Expression.Property(thisConst, bypassedProp);
                var currentAccess = Expression.Property(thisConst, currentProp);

                var casted = Expression.Convert(parameter, typeof(IPerteneceAEmpresa));
                var empresaIdAccess = Expression.Property(casted, nameof(IPerteneceAEmpresa.EmpresaId));
                var liftedId = Expression.Convert(empresaIdAccess, typeof(Guid?));
                var equality = Expression.Equal(liftedId, currentAccess);

                var orEmpresa = Expression.OrElse(bypassedAccess, equality);
                combined = combined is null
                    ? orEmpresa
                    : Expression.AndAlso(combined, orEmpresa);
            }

            var lambda = Expression.Lambda(combined!, parameter);
            modelBuilder.Entity(clrType).HasQueryFilter(lambda);
        }
    }
}
