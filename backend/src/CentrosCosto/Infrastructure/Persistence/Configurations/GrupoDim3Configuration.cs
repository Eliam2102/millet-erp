using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CentrosCosto.Domain;

namespace Millet.CentrosCosto.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="GrupoDim3"/> (CECO-PR4). Tabla
/// <c>centros_costo.grupos_dim3</c>. UNIQUE en <c>nombre</c>.
/// </summary>
public sealed class GrupoDim3Configuration : IEntityTypeConfiguration<GrupoDim3>
{
    public void Configure(EntityTypeBuilder<GrupoDim3> builder)
    {
        builder.ToTable("grupos_dim3", t =>
        {
            t.HasCheckConstraint("ck_grupos_dim3_estatus", "estatus BETWEEN 0 AND 2");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        builder.HasIndex(x => x.Nombre)
            .IsUnique()
            .HasDatabaseName("ux_grupos_dim3_nombre");
        builder.HasIndex(x => x.Estatus);
    }
}
