using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.Compras.Infrastructure.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="IntegrationEventOutboxEntry"/>
/// en el schema de Compras: tabla <c>compras.integration_events_outbox</c>
/// (F6-PR1).
///
/// <para>
/// Índice parcial sobre <c>(published_at)</c> filtrado por
/// <c>WHERE published_at IS NULL</c>: el worker (F6-PR2) lee solo filas
/// no publicadas, y Postgres mantiene el índice pequeño aunque la
/// tabla crezca a millones de filas históricas.
/// </para>
/// <para>
/// La tabla NO implementa <c>IPerteneceAEmpresa</c>: el worker debe
/// poder leer eventos de cualquier empresa sin filtro tenant. La
/// columna <c>integration_empresa_id</c> está denormalizada para que
/// los consumers la enruten.
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

        // Índice parcial: solo las filas pendientes de publicación. Para
        // queries del worker tipo:
        //   SELECT ... WHERE published_at IS NULL ORDER BY occurred_at LIMIT N FOR UPDATE SKIP LOCKED
        builder.HasIndex(e => e.PublishedAt)
            .HasDatabaseName("ix_integration_events_outbox_pending")
            .HasFilter("published_at IS NULL");

        // Índice secundario por empresa para auditoría y filtros admin.
        builder.HasIndex(e => e.IntegrationEmpresaId);
    }
}
