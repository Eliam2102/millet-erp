using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="IntegrationEventOutboxEntry"/>
/// en el schema del módulo: tabla
/// <c>integraciones_fiscal.integration_events_outbox</c>. Misma
/// estructura e índices que <c>integraciones_aw.integration_events_outbox</c>
/// y <c>compras.integration_events_outbox</c>.
/// </summary>
public sealed class IntegrationEventOutboxEntryConfiguration
    : IEntityTypeConfiguration<IntegrationEventOutboxEntry>
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
