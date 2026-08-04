using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Integraciones.Fiscal.Domain;

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="RfcReceptor"/>. Tabla
/// <c>integraciones_fiscal.rfcs_receptores</c>. UNIQUE
/// <c>(empresa_id, rfc)</c> previene duplicados; el constructor del
/// agregado normaliza RFC a UPPERCASE.
/// </summary>
public sealed class RfcReceptorConfiguration : IEntityTypeConfiguration<RfcReceptor>
{
    public void Configure(EntityTypeBuilder<RfcReceptor> builder)
    {
        builder.ToTable("rfcs_receptores", t =>
        {
            t.HasCheckConstraint("ck_rfcs_receptores_longitud",
                "char_length(rfc) BETWEEN 12 AND 13");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.EmpresaId).IsRequired();
        builder.Property(r => r.Rfc).HasMaxLength(13).IsRequired();
        builder.Property(r => r.DescargaHabilitada).IsRequired();
        builder.Property(r => r.RefreshHabilitada).IsRequired();
        builder.Property(r => r.CheckpointDescargaAt);

        // PR-10: aprovisionamiento FIEL en FiscalAPI. Todos nullable —
        // el RFC se puede crear desde el UI antes de subir FIEL.
        builder.Property(r => r.PersonIdExterno).HasMaxLength(100);
        builder.Property(r => r.CerFileIdExterno).HasMaxLength(100);
        builder.Property(r => r.KeyFileIdExterno).HasMaxLength(100);
        builder.Property(r => r.FielValidFrom);
        builder.Property(r => r.FielValidTo);
        builder.Property(r => r.FielSubidaAt);

        builder.HasIndex(r => new { r.EmpresaId, r.Rfc })
            .IsUnique()
            .HasDatabaseName("uq_rfcs_receptores_empresa_rfc");

        // Índice para el worker: filas habilitadas por empresa.
        builder.HasIndex(r => new { r.EmpresaId, r.DescargaHabilitada })
            .HasDatabaseName("ix_rfcs_receptores_empresa_descarga")
            .HasFilter("descarga_habilitada = true AND deleted_at IS NULL");
    }
}
