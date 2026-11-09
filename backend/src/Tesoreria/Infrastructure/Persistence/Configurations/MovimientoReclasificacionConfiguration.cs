using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

public sealed class MovimientoReclasificacionConfiguration : IEntityTypeConfiguration<MovimientoReclasificacion>
{
    public void Configure(EntityTypeBuilder<MovimientoReclasificacion> builder)
    {
        builder.ToTable("movimiento_reclasificaciones", t =>
            t.HasCheckConstraint("ck_movimiento_reclasificaciones_estado", "estado IN (1, 2, 3)"));

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.MovimientoId).IsRequired();
        builder.Property(e => e.ConceptoNuevoId).IsRequired();
        builder.Property(e => e.Motivo).HasMaxLength(400).IsRequired();
        builder.Property(e => e.SolicitadoPor).IsRequired();
        builder.Property(e => e.SolicitadoEn).IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.MotivoResolucion).HasMaxLength(400);

        builder.HasIndex(e => e.MovimientoId).HasDatabaseName("ix_movimiento_reclasificaciones_movimiento");
        // Una sola solicitud abierta por movimiento, también ante doble clic.
        builder.HasIndex(e => e.MovimientoId).IsUnique()
            .HasFilter("estado = 1").HasDatabaseName("ux_movimiento_reclasificaciones_pendiente");
    }
}
