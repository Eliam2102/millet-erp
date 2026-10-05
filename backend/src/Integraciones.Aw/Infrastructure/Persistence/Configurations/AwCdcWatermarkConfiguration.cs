using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Infrastructure.Persistence.Configurations;

/// <summary>Tabla <c>integraciones_aw.aw_cdc_watermark</c> (una fila por entidad).</summary>
public sealed class AwCdcWatermarkConfiguration : IEntityTypeConfiguration<AwCdcWatermark>
{
    public void Configure(EntityTypeBuilder<AwCdcWatermark> builder)
    {
        builder.ToTable("aw_cdc_watermark");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Entidad).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Lsn).HasMaxLength(20).IsRequired(); // 10 bytes en hex
        builder.HasIndex(e => e.Entidad).IsUnique().HasDatabaseName("uq_aw_cdc_watermark_entidad");
    }
}
