using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.Catalogo;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="SubAlmacen"/> (F1-PR1). Tabla
/// <c>almacen.sub_almacenes</c>. UNIQUE compuesto <c>(almacen_id, clave)</c>.
/// </summary>
public sealed class SubAlmacenConfiguration : IEntityTypeConfiguration<SubAlmacen>
{
    public void Configure(EntityTypeBuilder<SubAlmacen> builder)
    {
        builder.ToTable("sub_almacenes", t =>
        {
            t.HasCheckConstraint("ck_sub_almacenes_estatus", "estatus BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_sub_almacenes_tipo", "tipo BETWEEN 0 AND 3");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.AlmacenId).IsRequired();
        builder.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        builder.Property(x => x.Tipo).HasConversion<short>().IsRequired();
        builder.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        builder.HasIndex(x => new { x.AlmacenId, x.Clave })
            .IsUnique()
            .HasDatabaseName("ux_sub_almacenes_almacen_clave");
        builder.HasIndex(x => x.AlmacenId);
        builder.HasIndex(x => x.Tipo);
        builder.HasIndex(x => x.Estatus);
    }
}
