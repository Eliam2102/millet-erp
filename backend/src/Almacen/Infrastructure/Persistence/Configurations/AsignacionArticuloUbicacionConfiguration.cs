using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.Catalogo;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="AsignacionArticuloUbicacion"/> (OITW,
/// ADR-0047 PR3). Tabla <c>almacen.asignaciones_articulo_ubicacion</c>. Clave
/// única compuesta <c>(ubicacion_id, articulo_id)</c>. FK física a
/// <c>ubicaciones</c>; <c>articulo_id</c> lógica (compartido.articulos, sin FK).
/// </summary>
public sealed class AsignacionArticuloUbicacionConfiguration
    : IEntityTypeConfiguration<AsignacionArticuloUbicacion>
{
    public void Configure(EntityTypeBuilder<AsignacionArticuloUbicacion> builder)
    {
        builder.ToTable("asignaciones_articulo_ubicacion", t =>
        {
            t.HasCheckConstraint("ck_asignaciones_estatus", "estatus BETWEEN 0 AND 2");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.UbicacionId).IsRequired();
        // FK lógica a compartido.articulos (otro DbContext) — sin HasOne, como saldos.
        builder.Property(x => x.ArticuloId).IsRequired();
        builder.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        builder.HasIndex(x => new { x.UbicacionId, x.ArticuloId })
            .IsUnique()
            .HasDatabaseName("ux_asignaciones_ubicacion_articulo");
        builder.HasIndex(x => x.ArticuloId);
        builder.HasIndex(x => x.Estatus);

        // FK física a almacen.ubicaciones (mismo esquema). Restrict: no borrar
        // una ubicación con asignaciones.
        builder.HasOne<Ubicacion>()
            .WithMany()
            .HasForeignKey(x => x.UbicacionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
