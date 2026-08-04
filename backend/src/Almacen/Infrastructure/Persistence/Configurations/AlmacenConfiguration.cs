using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.Catalogo;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="Almacen"/> (F1-PR1). Tabla
/// <c>almacen.almacenes</c>. Coexiste temporalmente con la tabla
/// placeholder <c>compartido.almacenes</c> hasta F1-PR2.
/// </summary>
public sealed class AlmacenConfiguration : IEntityTypeConfiguration<Almacen.Domain.Catalogo.Almacen>
{
    public void Configure(EntityTypeBuilder<Almacen.Domain.Catalogo.Almacen> builder)
    {
        builder.ToTable("almacenes", t =>
        {
            t.HasCheckConstraint("ck_almacenes_estatus", "estatus BETWEEN 0 AND 2");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        builder.Property(x => x.SucursalId).IsRequired();
        builder.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        builder.HasIndex(x => x.Clave).IsUnique();
        builder.HasIndex(x => x.SucursalId);
        builder.HasIndex(x => x.Estatus);

        // FK lógica a compartido.sucursales — NO físico (cross-schema, evita
        // acoplar el ciclo de migraciones de Almacén con el de Administración).
        // Validación en handler vía ISucursalReadPort.

        // SubAlmacenes como navegación: hijos colección.
        builder.HasMany(x => x.SubAlmacenes)
            .WithOne()
            .HasForeignKey(s => s.AlmacenId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
