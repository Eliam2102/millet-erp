using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Anticipos;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="FacturaAnticipo"/> (F4-PR1). Tabla
/// <c>facturacion.factura_anticipo</c> — subtipo TPT cuya PK es FK 1:1 a
/// <c>comprobante</c> (igual que <c>factura_venta</c>). Aquí viven solo las
/// columnas propias del CFDI de anticipo; el saldo vive en <c>anticipo</c>.
/// </summary>
public sealed class FacturaAnticipoConfiguration : IEntityTypeConfiguration<FacturaAnticipo>
{
    public void Configure(EntityTypeBuilder<FacturaAnticipo> builder)
    {
        builder.ToTable("factura_anticipo");

        builder.Property(e => e.TipoAnticipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.PedidoFacturableId);
        builder.Property(e => e.AnticipoId).IsRequired();

        builder.Property(e => e.ClaveProdServSat).HasMaxLength(10).IsRequired();
        builder.Property(e => e.ClaveUnidadSat).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Descripcion).HasMaxLength(1000).IsRequired();

        builder.HasIndex(e => e.AnticipoId)
            .IsUnique()
            .HasDatabaseName("ix_factura_anticipo_anticipo");

        builder.HasIndex(e => e.PedidoFacturableId)
            .HasDatabaseName("ix_factura_anticipo_pedido")
            .HasFilter("pedido_facturable_id IS NOT NULL");
    }
}
