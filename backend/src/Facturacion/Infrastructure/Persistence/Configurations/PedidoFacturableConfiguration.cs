using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Pedidos;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="PedidoFacturable"/> (F1-PR2). Tabla
/// <c>facturacion.pedido_facturable</c>.
/// </summary>
public sealed class PedidoFacturableConfiguration : IEntityTypeConfiguration<PedidoFacturable>
{
    public void Configure(EntityTypeBuilder<PedidoFacturable> builder)
    {
        builder.ToTable("pedido_facturable");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Origen).HasConversion<short>().IsRequired();
        builder.Property(e => e.NumeroPedido).HasMaxLength(50);
        builder.Property(e => e.SucursalId).IsRequired();

        builder.Property(e => e.ClienteId).IsRequired();
        builder.Property(e => e.ClienteNombre).HasMaxLength(254).IsRequired();

        // FAC-ING-PR2: el enum pasó a catálogo (compartido.canales_venta); la
        // columna smallint conserva su nombre para no migrar datos.
        builder.Property(e => e.CanalVentaId).HasColumnName("canal_venta").IsRequired();
        builder.Property(e => e.ComportamientoFiscal).HasConversion<short>().IsRequired();

        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();

        builder.Property(e => e.ObraId);
        builder.Property(e => e.ObraNombre).HasMaxLength(200);

        builder.Property(e => e.Total).HasPrecision(18, 2).IsRequired();

        // RANURA-PR1: descuento de cabecera A+W (KO_FALZ), bruto con IVA.
        builder.Property(e => e.Ranura).HasPrecision(18, 2);

        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.ComprobanteVigenteId);
        builder.Property(e => e.CapturadoPor);
        builder.Property(e => e.Comentarios).HasMaxLength(1000);
        builder.Property(e => e.VersionOrigen);
        builder.Property(e => e.EstadoOrigen).HasMaxLength(20);

        builder.HasMany(e => e.Lineas)
            .WithOne()
            .HasForeignKey(l => l.PedidoFacturableId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.EmpresaId, e.Estado })
            .HasDatabaseName("ix_pedido_facturable_bandeja");

        builder.HasIndex(e => new { e.Origen, e.NumeroPedido })
            .HasDatabaseName("ix_pedido_facturable_origen_numero")
            .HasFilter("numero_pedido IS NOT NULL");

        builder.HasIndex(e => e.ObraId)
            .HasDatabaseName("ix_pedido_facturable_obra")
            .HasFilter("obra_id IS NOT NULL");
    }
}
