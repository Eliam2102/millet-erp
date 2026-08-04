using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CentrosCosto.Domain;

namespace Millet.CentrosCosto.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="Dim2"/> (Nivel 2, CECO-PR4). Tabla
/// <c>centros_costo.dim2</c>. Clave ÚNICA GLOBAL. FKs físicas
/// <c>Restrict</c> a <c>dim1</c> y <c>grupos_dim2</c> (mismo esquema), sin
/// navegaciones (table-per-level standalone).
/// </summary>
public sealed class Dim2Configuration : IEntityTypeConfiguration<Dim2>
{
    public void Configure(EntityTypeBuilder<Dim2> builder)
    {
        builder.ToTable("dim2", t =>
        {
            t.HasCheckConstraint("ck_dim2_estatus", "estatus BETWEEN 0 AND 2");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        // Nombres explícitos: la convención snake_case no separa
        // dígito→mayúscula ("Dim1Id" → "dim1id") — mismo caso que el
        // clave_ceco de A1.
        builder.Property(x => x.Dim1Id).HasColumnName("dim1_id").IsRequired();
        builder.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        builder.Property(x => x.GrupoDim2Id).HasColumnName("grupo_dim2_id").IsRequired();
        builder.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        builder.HasIndex(x => x.Clave)
            .IsUnique()
            .HasDatabaseName("ux_dim2_clave");
        builder.HasIndex(x => x.Dim1Id);
        builder.HasIndex(x => x.GrupoDim2Id);
        builder.HasIndex(x => x.Estatus);

        // Restrict: sin borrado físico de padres con hijos (la baja operativa
        // es lógica vía estatus, con cascada — ADR-0049).
        builder.HasOne<Dim1>()
            .WithMany()
            .HasForeignKey(x => x.Dim1Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<GrupoDim2>()
            .WithMany()
            .HasForeignKey(x => x.GrupoDim2Id)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
