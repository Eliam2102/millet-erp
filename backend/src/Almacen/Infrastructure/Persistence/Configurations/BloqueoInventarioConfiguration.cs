using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.Conteos;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF de <see cref="BloqueoInventario"/> (F7-PR3).
/// Tabla <c>almacen.bloqueos_inventario</c>. Índice parcial por
/// activos para que el lookup en hot path (validación de salidas)
/// sea O(1).
/// </summary>
public sealed class BloqueoInventarioConfiguration : IEntityTypeConfiguration<BloqueoInventario>
{
    public void Configure(EntityTypeBuilder<BloqueoInventario> builder)
    {
        builder.ToTable("bloqueos_inventario");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ConteoId).IsRequired();
        builder.Property(x => x.SubAlmacenId).IsRequired();
        builder.Property(x => x.BloqueaSalidas).IsRequired();
        builder.Property(x => x.BloqueaEntradas).IsRequired();
        builder.Property(x => x.Activo).IsRequired();
        builder.Property(x => x.Desde).IsRequired();
        builder.Property(x => x.Hasta);

        builder.HasIndex(x => x.SubAlmacenId)
            .HasDatabaseName("ix_bloqueos_inv_sub_almacen_activos")
            .HasFilter("activo = true");
        builder.HasIndex(x => x.ConteoId);
    }
}
