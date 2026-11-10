using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Comprobantes;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>Tabla <c>facturacion.efecto_comprobante</c> (M-285): un efecto por tipo y comprobante.</summary>
public sealed class EfectoComprobanteConfiguration : IEntityTypeConfiguration<EfectoComprobante>
{
    public void Configure(EntityTypeBuilder<EfectoComprobante> builder)
    {
        builder.ToTable("efecto_comprobante");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.ComprobanteId).IsRequired();
        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.UltimoErrorCodigo).HasMaxLength(50);
        builder.Property(e => e.UltimoError).HasMaxLength(EfectoComprobante.ErrorMaxLength);

        builder.HasIndex(e => new { e.ComprobanteId, e.Tipo })
            .HasDatabaseName("ux_efecto_comprobante_comprobante_tipo")
            .IsUnique();
        builder.HasIndex(e => e.Estado)
            .HasDatabaseName("ix_efecto_comprobante_pendiente")
            .HasFilter("estado = 1");
    }
}
