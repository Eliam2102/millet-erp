using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CentrosCosto.Domain;

namespace Millet.CentrosCosto.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="Dim1"/> (Nivel 1, CECO-PR4). Tabla
/// <c>centros_costo.dim1</c>. Clave ÚNICA GLOBAL. Sin relaciones fuera del
/// esquema (separación total — la sucursal del ERP no se cruza).
/// </summary>
public sealed class Dim1Configuration : IEntityTypeConfiguration<Dim1>
{
    public void Configure(EntityTypeBuilder<Dim1> builder)
    {
        builder.ToTable("dim1", t =>
        {
            t.HasCheckConstraint("ck_dim1_estatus", "estatus BETWEEN 0 AND 2");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Clave).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        builder.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        builder.HasIndex(x => x.Clave)
            .IsUnique()
            .HasDatabaseName("ux_dim1_clave");
        builder.HasIndex(x => x.Estatus);
    }
}
