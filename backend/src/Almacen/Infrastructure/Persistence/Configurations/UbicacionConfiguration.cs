using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.Catalogo;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="Ubicacion"/> (Nivel 4, ADR-0047, PR1).
/// Tabla <c>almacen.ubicaciones</c>. UNIQUE compuesto
/// <c>(sub_almacen_id, clave)</c>. FK física a <c>almacen.sub_almacenes</c>
/// (mismo esquema).
/// </summary>
public sealed class UbicacionConfiguration : IEntityTypeConfiguration<Ubicacion>
{
    public void Configure(EntityTypeBuilder<Ubicacion> builder)
    {
        builder.ToTable("ubicaciones", t =>
        {
            t.HasCheckConstraint("ck_ubicaciones_estatus", "estatus BETWEEN 0 AND 2");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.SubAlmacenId).IsRequired();
        builder.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        builder.Property(x => x.Estatus).HasConversion<short>().IsRequired();
        builder.Property(x => x.EsDefault).IsRequired();

        builder.HasIndex(x => new { x.SubAlmacenId, x.Clave })
            .IsUnique()
            .HasDatabaseName("ux_ubicaciones_sub_almacen_clave");
        builder.HasIndex(x => x.SubAlmacenId);
        builder.HasIndex(x => x.Estatus);

        // A lo sumo UNA ubicación default por sub-almacén (ADR-0047, PR2).
        // Índice único parcial (overload con nombre → índice distinto del
        // ix_ubicaciones_sub_almacen_id no-único, no lo reemplaza). El trigger
        // de saldos enruta por esta bandera.
        builder.HasIndex(x => x.SubAlmacenId, "IX_UbicacionDefaultPorSubAlmacen")
            .IsUnique()
            .HasFilter("es_default")
            .HasDatabaseName("ux_ubicaciones_default_por_sub_almacen");

        // FK física a sub_almacenes (mismo esquema `almacen`). Sin propiedad
        // de navegación en ninguno de los dos lados: la ubicación se gestiona
        // standalone por su DbSet, igual que SubAlmacen respecto de Almacen.
        // Restrict: no se puede borrar un sub-almacén con ubicaciones.
        builder.HasOne<SubAlmacen>()
            .WithMany()
            .HasForeignKey(x => x.SubAlmacenId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
