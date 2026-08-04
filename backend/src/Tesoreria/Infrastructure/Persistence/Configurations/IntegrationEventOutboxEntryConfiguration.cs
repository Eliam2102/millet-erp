using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="IntegrationEventOutboxEntry"/>
/// en el schema de Tesorería: tabla
/// <c>tesoreria.integration_events_outbox</c> (TES-PR1, ADR-0009). El
/// <c>OutboxPublisherWorker&lt;TesoreriaDbContext&gt;</c> que la drena
/// hacia el topic <c>tesoreria-events</c> se registra en PR-4.
///
/// <para>
/// Índice parcial sobre <c>(published_at)</c> filtrado por
/// <c>WHERE published_at IS NULL</c>: el worker lee solo filas no
/// publicadas y Postgres mantiene el índice pequeño.
/// </para>
/// </summary>
public sealed class IntegrationEventOutboxEntryConfiguration : IEntityTypeConfiguration<IntegrationEventOutboxEntry>
{
    public void Configure(EntityTypeBuilder<IntegrationEventOutboxEntry> builder)
    {
        builder.ToTable("integration_events_outbox");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EventType).HasMaxLength(150).IsRequired();
        builder.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.IntegrationEmpresaId).IsRequired();

        builder.Property(e => e.PublishedAt);
        builder.Property(e => e.Attempts).IsRequired();
        builder.Property(e => e.LastError);

        builder.HasIndex(e => e.PublishedAt)
            .HasDatabaseName("ix_integration_events_outbox_pending")
            .HasFilter("published_at IS NULL");

        builder.HasIndex(e => e.IntegrationEmpresaId);
    }
}
