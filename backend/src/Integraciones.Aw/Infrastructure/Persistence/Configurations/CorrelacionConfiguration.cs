using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="Correlacion"/>. Tabla
/// <c>integraciones_aw.correlacion</c>. Relación 1:1 con
/// <see cref="EntidadExterna"/> vía UNIQUE constraint en
/// <c>entidad_externa_id</c>.
/// </summary>
public sealed class CorrelacionConfiguration : IEntityTypeConfiguration<Correlacion>
{
    public void Configure(EntityTypeBuilder<Correlacion> builder)
    {
        builder.ToTable("correlacion");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.EntidadExternaId).IsRequired();
        builder.Property(c => c.AwDocId).IsRequired();
        builder.Property(c => c.AwDocIdSecondary).HasMaxLength(80);
        builder.Property(c => c.PollingCycleNumber).IsRequired();
        builder.Property(c => c.PollingQueryDurationMs);
        builder.Property(c => c.CorrelatedAt).IsRequired();
        builder.Property(c => c.AwRecordSnapshot).HasColumnType("jsonb");

        // 1:1 enforcement: UNIQUE en entidad_externa_id.
        builder.HasIndex(c => c.EntidadExternaId)
            .IsUnique()
            .HasDatabaseName("uq_correlacion_entidad_externa");

        builder.HasOne<EntidadExterna>()
            .WithMany()
            .HasForeignKey(c => c.EntidadExternaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
