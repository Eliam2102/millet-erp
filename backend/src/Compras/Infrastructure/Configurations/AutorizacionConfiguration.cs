using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain;

namespace Millet.Compras.Infrastructure.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="Autorizacion"/> (tabla
/// <c>compras.requisicion_autorizaciones</c>, diseño §10.1).
///
/// CHECK <c>nivel IN (1, 2)</c>. UNIQUE <c>(requisicion_id, nivel)</c>
/// para que no haya dos autorizaciones del mismo nivel en una RQ. FK
/// <c>requisicion_id → requisiciones</c> con <c>ON DELETE CASCADE</c>.
/// Índice en <c>(usuario_id, fecha_hora DESC)</c> para queries de
/// "mis autorizaciones recientes".
/// </summary>
public sealed class AutorizacionConfiguration : IEntityTypeConfiguration<Autorizacion>
{
    public void Configure(EntityTypeBuilder<Autorizacion> builder)
    {
        builder.ToTable("requisicion_autorizaciones", t =>
        {
            t.HasCheckConstraint("ck_requisicion_autorizaciones_nivel", "nivel IN (1, 2)");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.RequisicionId).IsRequired();

        builder.Property(a => a.Nivel)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(a => a.UsuarioId).IsRequired();
        builder.Property(a => a.FechaHora).IsRequired();
        builder.Property(a => a.Notas).HasMaxLength(500);

        builder.HasIndex(a => new { a.RequisicionId, a.Nivel }).IsUnique();
        builder.HasIndex(a => new { a.UsuarioId, a.FechaHora })
            .IsDescending(false, true);
    }
}
