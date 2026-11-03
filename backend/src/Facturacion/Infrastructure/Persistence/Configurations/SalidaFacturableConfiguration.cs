using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Pedidos;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// FAC-08: <see cref="SalidaFacturable"/> en <c>facturacion.salida_facturable</c>. Los índices
/// únicos son la idempotencia: una parcialidad por número dentro del pedido y una salida por
/// referencia de origen.
/// </summary>
public sealed class SalidaFacturableConfiguration : IEntityTypeConfiguration<SalidaFacturable>
{
    public void Configure(EntityTypeBuilder<SalidaFacturable> builder)
    {
        builder.ToTable("salida_facturable");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.PedidoFacturableId).IsRequired();
        builder.Property(e => e.NumeroParcialidad).IsRequired();
        builder.Property(e => e.Origen).HasConversion<short>().IsRequired();
        builder.Property(e => e.Referencia).HasMaxLength(SalidaFacturable.ReferenciaMaxLength).IsRequired();
        builder.Property(e => e.RegistradaEn).IsRequired();
        builder.Property(e => e.Total).HasPrecision(18, 2).IsRequired();

        builder.HasOne<PedidoFacturable>()
            .WithMany()
            .HasForeignKey(e => e.PedidoFacturableId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Lineas)
            .WithOne()
            .HasForeignKey(l => l.SalidaFacturableId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.PedidoFacturableId, e.NumeroParcialidad })
            .IsUnique()
            .HasDatabaseName("ux_salida_facturable_parcialidad");

        builder.HasIndex(e => new { e.EmpresaId, e.Origen, e.Referencia })
            .IsUnique()
            .HasDatabaseName("ux_salida_facturable_referencia");
    }
}

/// <summary>FAC-08: <see cref="SalidaFacturableLinea"/> en <c>facturacion.salida_facturable_linea</c>.</summary>
public sealed class SalidaFacturableLineaConfiguration : IEntityTypeConfiguration<SalidaFacturableLinea>
{
    public void Configure(EntityTypeBuilder<SalidaFacturableLinea> builder)
    {
        builder.ToTable("salida_facturable_linea");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.SalidaFacturableId).IsRequired();
        builder.Property(e => e.Posicion).IsRequired();
        builder.Property(e => e.Cantidad).HasPrecision(18, 6).IsRequired();
        builder.Property(e => e.Importe).HasPrecision(18, 2).IsRequired();

        builder.HasIndex(e => new { e.SalidaFacturableId, e.Posicion })
            .IsUnique()
            .HasDatabaseName("ux_salida_facturable_linea_posicion");
    }
}
