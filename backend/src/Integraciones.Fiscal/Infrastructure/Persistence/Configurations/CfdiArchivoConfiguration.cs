using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Integraciones.Fiscal.Domain.Cfdi;

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="CfdiArchivo"/> (F2-PR1 Facturación).
/// Tabla <c>integraciones_fiscal.cfdi_archivo</c>. UUID único por empresa.
/// </summary>
public sealed class CfdiArchivoConfiguration : IEntityTypeConfiguration<CfdiArchivo>
{
    public void Configure(EntityTypeBuilder<CfdiArchivo> builder)
    {
        builder.ToTable("cfdi_archivo");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Uuid).HasMaxLength(36).IsRequired();

        // XML completo → text. PDF estándar → bytea (nullable).
        builder.Property(e => e.XmlContenido).HasColumnType("text").IsRequired();
        builder.Property(e => e.PdfContenido);

        builder.Property(e => e.SelloCfdi).HasColumnType("text");
        builder.Property(e => e.SelloSat).HasColumnType("text");
        builder.Property(e => e.NoCertificadoSat).HasMaxLength(20);
        builder.Property(e => e.RfcProveedorCertificacion).HasMaxLength(13);
        builder.Property(e => e.FechaTimbrado).IsRequired();
        builder.Property(e => e.XmlHashSha256).HasMaxLength(64);

        builder.HasIndex(e => new { e.EmpresaId, e.Uuid })
            .IsUnique()
            .HasDatabaseName("ix_cfdi_archivo_empresa_uuid");
    }
}
