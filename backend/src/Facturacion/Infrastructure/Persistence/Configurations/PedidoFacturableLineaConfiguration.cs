using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Pedidos;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="PedidoFacturableLinea"/> (F1-PR2). Tabla
/// <c>facturacion.pedido_facturable_linea</c>.
/// </summary>
public sealed class PedidoFacturableLineaConfiguration : IEntityTypeConfiguration<PedidoFacturableLinea>
{
    public void Configure(EntityTypeBuilder<PedidoFacturableLinea> builder)
    {
        builder.ToTable("pedido_facturable_linea");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.PedidoFacturableId).IsRequired();
        builder.Property(e => e.Posicion).IsRequired();

        builder.Property(e => e.ProductoId);
        builder.Property(e => e.ProductoDescripcion).HasMaxLength(1000).IsRequired();
        builder.Property(e => e.ClaveProdServSat).HasMaxLength(10);
        builder.Property(e => e.ClaveUnidadSat).HasMaxLength(10);

        builder.Property(e => e.Cantidad).HasPrecision(18, 6).IsRequired();
        builder.Property(e => e.Precio).HasPrecision(18, 6).IsRequired();
        builder.Property(e => e.Descuento).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Importe).HasPrecision(18, 2).IsRequired();

        builder.Property(e => e.RequierePedimento).IsRequired();

        // ADR-0048 D8: BOM/componentes del origen (informativo). jsonb para
        // consultarlo por SQL sin participar en importes ni CFDI.
        builder.Property(e => e.BomJson).HasColumnType("jsonb");

        // FAC-DET-PR2: tasa de IVA de la línea (fracción 0–1).
        builder.Property(e => e.TasaIva).HasPrecision(5, 4);

        builder.HasIndex(e => new { e.PedidoFacturableId, e.Posicion })
            .IsUnique()
            .HasDatabaseName("ix_pedido_facturable_linea_posicion");
    }
}
