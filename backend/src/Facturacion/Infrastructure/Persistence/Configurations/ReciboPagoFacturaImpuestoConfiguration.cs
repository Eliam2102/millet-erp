using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Repp;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="ReciboPagoFacturaImpuesto"/> (P9-H8).
/// Tabla <c>facturacion.recibo_pago_factura_impuesto</c> — desglose ImpuestosDR
/// del documento relacionado, nieta del agregado <see cref="ReciboPago"/>.
/// </summary>
public sealed class ReciboPagoFacturaImpuestoConfiguration : IEntityTypeConfiguration<ReciboPagoFacturaImpuesto>
{
    public void Configure(EntityTypeBuilder<ReciboPagoFacturaImpuesto> builder)
    {
        builder.ToTable("recibo_pago_factura_impuesto");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ReciboPagoFacturaId).IsRequired();
        builder.Property(e => e.Impuesto).HasMaxLength(3).IsRequired();
        builder.Property(e => e.TipoFactor).HasMaxLength(10).IsRequired();
        builder.Property(e => e.TasaOCuota).HasPrecision(18, 6).IsRequired();
        builder.Property(e => e.EsRetencion).IsRequired();
        builder.Property(e => e.BaseDR).HasPrecision(18, 6).IsRequired();
        builder.Property(e => e.ImporteDR).HasPrecision(18, 6).IsRequired();

        builder.HasIndex(e => e.ReciboPagoFacturaId)
            .HasDatabaseName("ix_recibo_pago_factura_impuesto_doc");
    }
}
