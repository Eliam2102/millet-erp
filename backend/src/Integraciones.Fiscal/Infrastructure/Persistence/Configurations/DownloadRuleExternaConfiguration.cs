using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Integraciones.Fiscal.Domain;

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="DownloadRuleExterna"/>. Tabla
/// <c>integraciones_fiscal.download_rules_externas</c>. Cache local del
/// id que FiscalAPI asigna a la rule. UNIQUE
/// <c>(rfc_receptor_id, sat_query_type, download_type, sat_invoice_status)</c>
/// — una rule por combinación.
/// </summary>
public sealed class DownloadRuleExternaConfiguration : IEntityTypeConfiguration<DownloadRuleExterna>
{
    public void Configure(EntityTypeBuilder<DownloadRuleExterna> builder)
    {
        builder.ToTable("download_rules_externas");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.EmpresaId).IsRequired();
        builder.Property(r => r.RfcReceptorId).IsRequired();
        builder.Property(r => r.RuleIdExterno).HasMaxLength(100).IsRequired();
        builder.Property(r => r.SatQueryType).HasConversion<short>().IsRequired();
        builder.Property(r => r.DownloadType).HasConversion<short>().IsRequired();
        builder.Property(r => r.SatInvoiceStatus).HasConversion<short>().IsRequired();
        builder.Property(r => r.Activa).IsRequired();

        builder.HasIndex(r => new { r.RfcReceptorId, r.SatQueryType, r.DownloadType, r.SatInvoiceStatus })
            .IsUnique()
            .HasDatabaseName("uq_download_rules_combo");

        builder.HasIndex(r => new { r.EmpresaId, r.Activa })
            .HasDatabaseName("ix_download_rules_empresa_activa")
            .HasFilter("activa = true AND deleted_at IS NULL");
    }
}
