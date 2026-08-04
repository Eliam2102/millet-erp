using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence.Configurations;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Integraciones.Aw.Infrastructure.Persistence;

/// <summary>
/// DbContext del módulo Integraciones.Aw (foundation PR B). Schema:
/// <c>integraciones_aw</c>. Hereda de <see cref="BaseDbContext"/> que
/// aplica los 4 interceptors transversales (Metadata, EmpresaContext,
/// Audit, Outbox) automáticamente — no se registran aquí.
///
/// <para>
/// Cross-schema: las FKs lógicas a <c>compartido.empresa</c> y
/// <c>identidad.usuario_servicio</c> están en las configurations
/// individuales. Postgres soporta cross-schema FKs físicas; las
/// migraciones las generan.
/// </para>
///
/// <para>
/// Outbox table: <c>integraciones_aw.integration_events_outbox</c>
/// (NO "outbox" suelto como propone doc 02 — alineado a la convención
/// existente de compras.integration_events_outbox; ver RESUMEN.md
/// sección "Discrepancia doc vs código").
/// </para>
/// </summary>
public sealed class IntegracionesAwDbContext : BaseDbContext
{
    public IntegracionesAwDbContext(
        DbContextOptions<IntegracionesAwDbContext> options,
        ICurrentEmpresaContext empresaContext) : base(options, empresaContext) { }

    public DbSet<EntidadExterna> EntidadesExternas => Set<EntidadExterna>();
    public DbSet<Envio> Envios => Set<Envio>();
    public DbSet<Correlacion> Correlaciones => Set<Correlacion>();

    /// <summary>
    /// Outbox de eventos de integración del módulo. Lo escribe
    /// <c>OutboxSaveChangesInterceptor</c> drenando el buffer scoped
    /// antes de SaveChanges; lo lee <c>OutboxPublisherWorker&lt;IntegracionesAwDbContext&gt;</c>
    /// para publicar al topic <c>integraciones-aw-events</c>.
    /// </summary>
    public DbSet<IntegrationEventOutboxEntry> OutboxEntries => Set<IntegrationEventOutboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("integraciones_aw");
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new EntidadExternaConfiguration());
        modelBuilder.ApplyConfiguration(new EnvioConfiguration());
        modelBuilder.ApplyConfiguration(new CorrelacionConfiguration());
        modelBuilder.ApplyConfiguration(new IntegrationEventOutboxEntryConfiguration());
    }
}
