using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Facturas;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="FacturaVenta"/> (F1-PR1). Tabla
/// <c>facturacion.factura_venta</c> — subtipo TPT cuya PK es FK 1:1 a
/// <c>comprobante</c> (EF lo infiere al mapear a una tabla distinta de la base).
/// Aquí viven las columnas propias de la factura de venta + sus líneas.
/// </summary>
public sealed class FacturaVentaConfiguration : IEntityTypeConfiguration<FacturaVenta>
{
    public void Configure(EntityTypeBuilder<FacturaVenta> builder)
    {
        builder.ToTable("factura_venta");

        // canal_venta subió a la tabla base comprobante ([Decisión 12-D],
        // CAJAS-PR2); ver ComprobanteConfiguration.
        builder.Property(e => e.ComportamientoFiscal).HasConversion<short>().IsRequired();

        builder.Property(e => e.PedidoFacturableId);
        builder.Property(e => e.ObraId);
        builder.Property(e => e.ObraNombre).HasMaxLength(200);
        builder.Property(e => e.FacturaAgrupada).IsRequired();
        builder.Property(e => e.AutorizacionId);

        builder.HasMany(e => e.Lineas)
            .WithOne()
            .HasForeignKey(l => l.FacturaVentaId)
            .OnDelete(DeleteBehavior.Cascade);

        // CCE 1:1 opcional (F7-PR1).
        builder.HasOne(e => e.ComplementoCce)
            .WithOne()
            .HasForeignKey<Millet.Facturacion.Domain.Cce.ComplementoCce>(c => c.FacturaVentaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.ObraId)
            .HasDatabaseName("ix_factura_venta_obra")
            .HasFilter("obra_id IS NOT NULL");

        builder.HasIndex(e => e.PedidoFacturableId)
            .HasDatabaseName("ix_factura_venta_pedido")
            .HasFilter("pedido_facturable_id IS NOT NULL");
    }
}
