using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Integraciones.Fiscal.Domain.Certificados;

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="CertificadoCsd"/> (F6.9). Tabla
/// <c>integraciones_fiscal.certificados_csd</c>: historial de CSD por empresa.
/// </summary>
public sealed class CertificadoCsdConfiguration : IEntityTypeConfiguration<CertificadoCsd>
{
    public void Configure(EntityTypeBuilder<CertificadoCsd> builder)
    {
        builder.ToTable("certificados_csd");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.EmpresaId).IsRequired();
        builder.Property(c => c.ConfiguracionPacId).IsRequired();
        builder.Property(c => c.NumeroSerie).HasMaxLength(40).IsRequired();
        builder.Property(c => c.Rfc).HasMaxLength(13);
        builder.Property(c => c.VigenteDesde).IsRequired();
        builder.Property(c => c.VenceEn).IsRequired();
        builder.Property(c => c.Estado).HasConversion<short>().IsRequired();
        builder.Property(c => c.RegistradoEn).IsRequired();
        builder.Property(c => c.RegistradoPor).HasMaxLength(200);
        builder.Property(c => c.FinalizadoEn);
        builder.Property(c => c.FinalizadoPor).HasMaxLength(200);
        builder.Property(c => c.MotivoRevocacion).HasMaxLength(500);

        builder.HasIndex(c => new { c.EmpresaId, c.NumeroSerie })
            .HasDatabaseName("ix_certificados_csd_empresa_numero_serie");
    }
}
