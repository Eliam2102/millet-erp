using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.Cfdi;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

/// <summary>Tabla <c>cuentas_por_pagar.solicitudes_cancelacion_cfdi</c> (P8.7).</summary>
public sealed class SolicitudCancelacionCfdiConfiguration : IEntityTypeConfiguration<SolicitudCancelacionCfdi>
{
    public void Configure(EntityTypeBuilder<SolicitudCancelacionCfdi> builder)
    {
        builder.ToTable("solicitudes_cancelacion_cfdi");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.CfdiRecibidoId).IsRequired();
        builder.Property(e => e.Area).HasConversion<short>().IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.Notas).HasMaxLength(1000);
        builder.Property(e => e.ComentarioDecision).HasMaxLength(1000);
        builder.Property(e => e.EvidenciaBlobRef).HasMaxLength(400);
        builder.Property(e => e.EvidenciaNombre).HasMaxLength(255);
        builder.Property(e => e.SustitutoCfdiId);
        builder.Property(e => e.AutorizadoPor);
        builder.Ignore(e => e.PlazoSatVence);
        // Una sola solicitud pendiente por CFDI.
        builder.HasIndex(e => e.CfdiRecibidoId).IsUnique().HasFilter("estado = 1")
            .HasDatabaseName("ux_solicitudes_cancelacion_cfdi_pendiente");
    }
}
