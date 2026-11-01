using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Integraciones.Fiscal.Domain.Certificados;

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="AlertaCsd"/> (F6.9). Tabla
/// <c>integraciones_fiscal.alertas_csd</c>; un aviso por certificado y umbral.
/// </summary>
public sealed class AlertaCsdConfiguration : IEntityTypeConfiguration<AlertaCsd>
{
    public void Configure(EntityTypeBuilder<AlertaCsd> builder)
    {
        builder.ToTable("alertas_csd");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.EmpresaId).IsRequired();
        builder.Property(a => a.CertificadoCsdId).IsRequired();
        builder.Property(a => a.UmbralDias).IsRequired();
        builder.Property(a => a.DiasRestantes).IsRequired();
        builder.Property(a => a.DisparadaEn).IsRequired();
        builder.Property(a => a.Destinatarios).HasMaxLength(2000).IsRequired();

        builder.HasIndex(a => new { a.CertificadoCsdId, a.UmbralDias })
            .IsUnique()
            .HasDatabaseName("ux_alertas_csd_certificado_umbral");
    }
}
