using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Infrastructure.Persistence.Configurations;

/// <summary>Tabla <c>integraciones_aw.costo_promedio_publicado</c> (M-447): marca de agua del publicador de costo promedio.</summary>
public sealed class AwCostoPromedioPublicadoConfiguration : IEntityTypeConfiguration<AwCostoPromedioPublicado>
{
    public void Configure(EntityTypeBuilder<AwCostoPromedioPublicado> builder)
    {
        builder.ToTable("costo_promedio_publicado");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Sucursal).HasMaxLength(40).IsRequired();
        builder.Property(e => e.AlmacenTipo).HasMaxLength(60).IsRequired();
        builder.Property(e => e.Linea).HasMaxLength(60).IsRequired();
        builder.Property(e => e.Costo).HasPrecision(18, 6);
        builder.HasIndex(e => new { e.ArticuloAw, e.Sucursal, e.AlmacenTipo, e.Linea, e.AnchoMm, e.AltoMm })
            .IsUnique().HasDatabaseName("ux_costo_promedio_publicado_llave");
    }
}
