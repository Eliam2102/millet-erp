using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.NotasCredito;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="NotaCredito"/> (F4-PR2). Tabla
/// <c>facturacion.nota_credito</c> — subtipo TPT cuya PK es FK 1:1 a
/// <c>comprobante</c> (igual que <c>factura_venta</c>/<c>factura_anticipo</c>).
/// </summary>
public sealed class NotaCreditoConfiguration : IEntityTypeConfiguration<NotaCredito>
{
    public void Configure(EntityTypeBuilder<NotaCredito> builder)
    {
        builder.ToTable("nota_credito");

        builder.Property(e => e.Motivo).HasConversion<short>().IsRequired();
        builder.Property(e => e.AnticipoOrigenId);
        builder.Property(e => e.FacturaRelacionadaId);
        builder.Property(e => e.ImporteAfectaInventario).HasPrecision(18, 2).IsRequired();

        builder.Property(e => e.ClaveProdServSat).HasMaxLength(10).IsRequired();
        builder.Property(e => e.ClaveUnidadSat).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Descripcion).HasMaxLength(1000).IsRequired();

        builder.HasIndex(e => e.AnticipoOrigenId)
            .HasDatabaseName("ix_nota_credito_anticipo")
            .HasFilter("anticipo_origen_id IS NOT NULL");

        builder.HasIndex(e => e.FacturaRelacionadaId)
            .HasDatabaseName("ix_nota_credito_factura")
            .HasFilter("factura_relacionada_id IS NOT NULL");
    }
}
