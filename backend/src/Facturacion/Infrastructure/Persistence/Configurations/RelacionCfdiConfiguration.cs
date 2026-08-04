using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Comprobantes;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="RelacionCfdi"/> (F4-PR2). Tabla
/// <c>facturacion.relacion_cfdi</c> — entidad hija de <see cref="Comprobante"/>
/// (la FK al comprobante la declara <c>ComprobanteConfiguration</c> vía
/// <c>HasMany(e =&gt; e.Relaciones)</c>).
/// </summary>
public sealed class RelacionCfdiConfiguration : IEntityTypeConfiguration<RelacionCfdi>
{
    public void Configure(EntityTypeBuilder<RelacionCfdi> builder)
    {
        builder.ToTable("relacion_cfdi");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ComprobanteId).IsRequired();
        builder.Property(e => e.UuidRelacionado).HasMaxLength(36).IsRequired();
        builder.Property(e => e.TipoRelacion).HasMaxLength(2).IsRequired();

        builder.HasIndex(e => e.ComprobanteId)
            .HasDatabaseName("ix_relacion_cfdi_comprobante");
    }
}
