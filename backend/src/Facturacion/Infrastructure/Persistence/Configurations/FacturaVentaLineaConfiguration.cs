using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Facturas;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="FacturaVentaLinea"/> (F1-PR1). Tabla
/// <c>facturacion.factura_venta_linea</c>. Importes unitarios con precisión
/// decimal(18,6); importes/totales decimal(18,2) (§1 cuidados-infra).
/// </summary>
public sealed class FacturaVentaLineaConfiguration : IEntityTypeConfiguration<FacturaVentaLinea>
{
    public void Configure(EntityTypeBuilder<FacturaVentaLinea> builder)
    {
        builder.ToTable("factura_venta_linea");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.FacturaVentaId).IsRequired();
        builder.Property(e => e.Posicion).IsRequired();

        builder.Property(e => e.ProductoId);
        builder.Property(e => e.ClaveProdServSat).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Descripcion).HasMaxLength(1000).IsRequired();
        builder.Property(e => e.ClaveUnidadSat).HasMaxLength(10).IsRequired();

        builder.Property(e => e.Cantidad).HasPrecision(18, 6).IsRequired();
        builder.Property(e => e.ValorUnitario).HasPrecision(18, 6).IsRequired();
        builder.Property(e => e.Descuento).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Importe).HasPrecision(18, 2).IsRequired();

        builder.Property(e => e.ObjetoImp).HasMaxLength(2).IsRequired();

        builder.Property(e => e.TasaIvaTraslado).HasPrecision(8, 6);
        builder.Property(e => e.ImpuestoTrasladadoImporte).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.TasaRetencionIva).HasPrecision(8, 6);
        builder.Property(e => e.TasaRetencionIsr).HasPrecision(8, 6);
        builder.Property(e => e.RetencionTotalImporte).HasPrecision(18, 2).IsRequired();

        // Datos aduaneros / pedimento (F7-PR2).
        builder.Property(e => e.RequierePedimento).IsRequired();
        builder.Property(e => e.Pedimento).HasMaxLength(21);
        builder.Property(e => e.FechaDocAduanero);
        builder.Property(e => e.IdentificacionMercancia).HasMaxLength(50);

        builder.HasIndex(e => new { e.FacturaVentaId, e.Posicion })
            .IsUnique()
            .HasDatabaseName("ix_factura_venta_linea_posicion");
    }
}
