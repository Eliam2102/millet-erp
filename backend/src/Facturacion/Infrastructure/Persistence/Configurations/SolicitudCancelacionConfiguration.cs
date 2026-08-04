using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Cancelaciones;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="SolicitudCancelacion"/> (F5-PR2). Tabla
/// <c>facturacion.solicitud_cancelacion</c>. <c>EmpresaId</c> y el <c>row_version</c>
/// los gestiona el <c>BaseDbContext</c>.
/// </summary>
public sealed class SolicitudCancelacionConfiguration : IEntityTypeConfiguration<SolicitudCancelacion>
{
    public void Configure(EntityTypeBuilder<SolicitudCancelacion> builder)
    {
        builder.ToTable("solicitud_cancelacion");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.ComprobanteId).IsRequired();

        builder.Property(e => e.MotivoSat).HasMaxLength(2).IsRequired();
        builder.Property(e => e.UuidSustituto).HasMaxLength(36);
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.EstatusSat).HasMaxLength(50);
        builder.Property(e => e.MensajeError).HasMaxLength(500);
        builder.Property(e => e.SolicitadaEn).IsRequired();
        builder.Property(e => e.ResueltaEn);

        builder.HasIndex(e => e.ComprobanteId)
            .HasDatabaseName("ix_solicitud_cancelacion_comprobante");

        // El poller barre las solicitudes no resueltas (EnProceso).
        builder.HasIndex(e => e.Estado)
            .HasDatabaseName("ix_solicitud_cancelacion_estado");
    }
}
