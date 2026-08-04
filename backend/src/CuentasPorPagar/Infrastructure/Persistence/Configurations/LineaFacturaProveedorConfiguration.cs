using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class LineaFacturaProveedorConfiguration : IEntityTypeConfiguration<LineaFacturaProveedor>
{
    public void Configure(EntityTypeBuilder<LineaFacturaProveedor> builder)
    {
        builder.ToTable("lineas_factura_proveedor");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.FacturaProveedorId).IsRequired();
        builder.Property(e => e.Posicion).IsRequired();

        builder.Property(e => e.ArticuloId);
        builder.Property(e => e.ClaveProdServ).HasMaxLength(40);
        builder.Property(e => e.Descripcion).HasMaxLength(1000).IsRequired();

        builder.Property(e => e.Cantidad).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.ClaveUnidad).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Unidad).HasMaxLength(40);

        builder.Property(e => e.PrecioUnitario).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Importe).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Descuento).HasPrecision(18, 4);

        builder.Property(e => e.LineaOcId);
        builder.Property(e => e.ConceptoContableId);

        builder.HasIndex(e => new { e.FacturaProveedorId, e.Posicion })
            .HasDatabaseName("ux_lineas_factura_proveedor_pos")
            .IsUnique();
    }
}
