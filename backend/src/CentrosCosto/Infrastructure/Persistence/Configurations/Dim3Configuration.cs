using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CentrosCosto.Domain;

namespace Millet.CentrosCosto.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="Dim3"/> (Nivel 3 — hoja, CECO-PR4).
/// Tabla <c>centros_costo.dim3</c>. Clave ÚNICA GLOBAL. FKs físicas
/// <c>Restrict</c> a <c>dim2</c> y <c>grupos_dim3</c>, sin navegaciones.
/// </summary>
public sealed class Dim3Configuration : IEntityTypeConfiguration<Dim3>
{
    public void Configure(EntityTypeBuilder<Dim3> builder)
    {
        builder.ToTable("dim3", t =>
        {
            t.HasCheckConstraint("ck_dim3_estatus", "estatus BETWEEN 0 AND 2");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        // Nombres explícitos: la convención snake_case no separa
        // dígito→mayúscula ("Dim2Id" → "dim2id") — mismo caso que el
        // clave_ceco de A1.
        builder.Property(x => x.Dim2Id).HasColumnName("dim2_id").IsRequired();
        builder.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        builder.Property(x => x.GrupoDim3Id).HasColumnName("grupo_dim3_id").IsRequired();
        builder.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        builder.HasIndex(x => x.Clave)
            .IsUnique()
            .HasDatabaseName("ux_dim3_clave");
        builder.HasIndex(x => x.Dim2Id);
        builder.HasIndex(x => x.GrupoDim3Id);
        builder.HasIndex(x => x.Estatus);

        builder.HasOne<Dim2>()
            .WithMany()
            .HasForeignKey(x => x.Dim2Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<GrupoDim3>()
            .WithMany()
            .HasForeignKey(x => x.GrupoDim3Id)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
