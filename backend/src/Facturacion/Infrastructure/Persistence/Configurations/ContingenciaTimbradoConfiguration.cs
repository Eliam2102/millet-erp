using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Comprobantes;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Tabla <c>facturacion.contingencia_timbrado</c> (F6.8). Una sola contingencia
/// abierta por comprobante; índice por estado y fecha límite para el aviso a Fiscal.
/// </summary>
public sealed class ContingenciaTimbradoConfiguration : IEntityTypeConfiguration<ContingenciaTimbrado>
{
    public void Configure(EntityTypeBuilder<ContingenciaTimbrado> builder)
    {
        builder.ToTable("contingencia_timbrado");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.SucursalId).IsRequired();
        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.Origen).HasConversion<short>().IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.Motivo).HasMaxLength(2000).IsRequired();
        builder.Property(e => e.ErrorCodigo).HasMaxLength(50);
        builder.Property(e => e.OcurridaAt).IsRequired();
        builder.Property(e => e.FechaLimite).IsRequired();
        builder.Property(e => e.ComprobanteFolio).HasMaxLength(40);
        builder.Property(e => e.RegistradaPorNombre).HasMaxLength(200);
        builder.Property(e => e.UuidFinal).HasMaxLength(36);
        builder.Property(e => e.NotaCierre).HasMaxLength(2000);
        builder.Property(e => e.CerradaPorNombre).HasMaxLength(200);

        builder.HasIndex(e => e.ComprobanteId)
            .HasDatabaseName("ux_contingencia_timbrado_comprobante_abierta")
            .IsUnique()
            .HasFilter("comprobante_id IS NOT NULL AND estado = 1");
        builder.HasIndex(e => new { e.Estado, e.FechaLimite })
            .HasDatabaseName("ix_contingencia_timbrado_estado_limite");
        builder.HasIndex(e => e.OcurridaAt)
            .HasDatabaseName("ix_contingencia_timbrado_ocurrida");
    }
}
