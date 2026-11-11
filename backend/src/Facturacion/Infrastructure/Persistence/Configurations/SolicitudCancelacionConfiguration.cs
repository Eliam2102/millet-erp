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

        // F6.5: quién solicita y quién (Contador General) autoriza o rechaza.
        builder.Property(e => e.SolicitadaPorNombre).HasMaxLength(200);
        builder.Property(e => e.DecididaPorNombre).HasMaxLength(200);
        builder.Property(e => e.MotivoRechazo).HasMaxLength(500);

        // M-235: firmas por operación y monto, y huella de lo autorizado.
        builder.Property(e => e.FirmasRequeridas).HasDefaultValue((short)1).IsRequired();
        builder.Property(e => e.ImporteMxn).HasPrecision(18, 2).HasDefaultValue(0m).IsRequired();
        builder.Property(e => e.PrimeraFirmaPorNombre).HasMaxLength(200);
        builder.Property(e => e.PrimeraFirmaHuella).HasMaxLength(64);
        builder.Property(e => e.FirmaAnuladaMotivo).HasMaxLength(200);
        builder.Ignore(e => e.Huella);
        builder.Ignore(e => e.FirmasDadas);
        builder.ToTable(t => t.HasCheckConstraint("ck_solicitud_cancelacion_firmas", "firmas_requeridas IN (1, 2)"));

        builder.HasIndex(e => e.ComprobanteId)
            .HasDatabaseName("ix_solicitud_cancelacion_comprobante");

        // El poller barre las solicitudes no resueltas (EnProceso); la bandeja, las pendientes de autorizar.
        builder.HasIndex(e => e.Estado)
            .HasDatabaseName("ix_solicitud_cancelacion_estado");
    }
}
