using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.ProductoTerminado.Infrastructure.Persistence;
public sealed class IntegrationEventOutboxEntryConfiguration : IEntityTypeConfiguration<IntegrationEventOutboxEntry>
{
    public void Configure(EntityTypeBuilder<IntegrationEventOutboxEntry> builder)
    {
        builder.ToTable("outbox");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EventType).HasMaxLength(150).IsRequired();
        builder.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.IntegrationEmpresaId).IsRequired();
        builder.Property(e => e.PublishedAt);
        builder.Property(e => e.Attempts).IsRequired();
        builder.Property(e => e.LastError);
        builder.HasIndex(e => e.PublishedAt).HasDatabaseName("ix_outbox_pending").HasFilter("published_at IS NULL");
        builder.HasIndex(e => e.IntegrationEmpresaId);
    }
}
