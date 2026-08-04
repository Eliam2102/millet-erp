using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CentrosCosto.Domain;

namespace Millet.CentrosCosto.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="Asignacion"/> (alcance congelado,
/// CECO-PR6). Tabla <c>centros_costo.asignaciones</c>. UNIQUE
/// (usuario_id, dim3_id); <c>usuario_id</c> es Guid lógico (sin FK a
/// Identidad), <c>dim3_id</c> FK física <c>Restrict</c>.
/// </summary>
public sealed class AsignacionConfiguration : IEntityTypeConfiguration<Asignacion>
{
    public void Configure(EntityTypeBuilder<Asignacion> builder)
    {
        builder.ToTable("asignaciones");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        // Nombres explícitos (04-cuidados §1.5): la convención snake_case
        // no separa dígito→mayúscula ("Dim3Id" → "dim3id").
        builder.Property(x => x.UsuarioId).HasColumnName("usuario_id").IsRequired();
        builder.Property(x => x.Dim3Id).HasColumnName("dim3_id").IsRequired();

        builder.HasIndex(x => new { x.UsuarioId, x.Dim3Id })
            .IsUnique()
            .HasDatabaseName("ux_asignaciones_usuario_dim3");

        builder.HasIndex(x => x.UsuarioId).HasDatabaseName("ix_asignaciones_usuario");
        builder.HasIndex(x => x.Dim3Id).HasDatabaseName("ix_asignaciones_dim3");

        builder.HasOne<Dim3>()
            .WithMany()
            .HasForeignKey(x => x.Dim3Id)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
