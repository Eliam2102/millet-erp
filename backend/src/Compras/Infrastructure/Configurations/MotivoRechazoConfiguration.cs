using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain;

namespace Millet.Compras.Infrastructure.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="MotivoRechazo"/> (tabla
/// <c>compras.motivos_rechazo</c>, diseño §3.bis.3). UNIQUE sobre
/// <c>clave</c> para que no se dupliquen códigos. CHECK sobre
/// <c>aplica_a</c> para que sea un bitmask válido (1..7).
/// </summary>
public sealed class MotivoRechazoConfiguration : IEntityTypeConfiguration<MotivoRechazo>
{
    public void Configure(EntityTypeBuilder<MotivoRechazo> builder)
    {
        builder.ToTable("motivos_rechazo", t =>
        {
            // F3-PR2 (OC): bitmask amplía de [1..7] a [1..15] al agregar
            // OrdenCompra = 8. ADR-0043 lo amplía a [1..31] al agregar
            // CierreManual = 16. Cada migración ALTER cambia el CHECK BD.
            t.HasCheckConstraint("ck_motivos_rechazo_aplica_a", "aplica_a BETWEEN 1 AND 31");
        });

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Clave).HasMaxLength(20).IsRequired();
        builder.HasIndex(m => m.Clave).IsUnique();

        builder.Property(m => m.Descripcion).HasMaxLength(200).IsRequired();

        builder.Property(m => m.PermiteTextoLibre).IsRequired();

        builder.Property(m => m.AplicaA)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(m => m.Activo).IsRequired();
    }
}
