using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Proformas;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="ProformaPedido"/> (U1.8a). Tabla
/// <c>facturacion.proformas_pedido</c>. El contenido comparable y los avisos se
/// guardan como <c>jsonb</c>; la versión es única por pedido.
/// </summary>
public sealed class ProformaPedidoConfiguration : IEntityTypeConfiguration<ProformaPedido>
{
    public void Configure(EntityTypeBuilder<ProformaPedido> builder)
    {
        builder.ToTable("proformas_pedido");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.SucursalId).IsRequired();
        builder.Property(e => e.PedidoFacturableId).IsRequired();
        builder.Property(e => e.NumeroVersion).IsRequired();
        builder.Property(e => e.Folio).HasMaxLength(80).IsRequired();
        builder.Property(e => e.NumeroPedido).HasMaxLength(60);
        builder.Property(e => e.ContenidoJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.AvisosJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.Total).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.GeneradaPorUsuarioId);
        builder.Property(e => e.GeneradaPorNombre).HasMaxLength(200);
        builder.Property(e => e.GeneradaEn).IsRequired();

        builder.HasIndex(e => new { e.PedidoFacturableId, e.NumeroVersion })
            .IsUnique()
            .HasDatabaseName("ux_proformas_pedido_pedido_version");
    }
}
