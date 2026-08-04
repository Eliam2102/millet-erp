using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Ingesta;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="PedidoFacturableSnapshot"/> (F3-PR1).
/// Tabla <c>facturacion.pedido_facturable_snapshot</c>. El payload crudo se
/// guarda como <c>jsonb</c> (§5 diseño).
/// </summary>
public sealed class PedidoFacturableSnapshotConfiguration : IEntityTypeConfiguration<PedidoFacturableSnapshot>
{
    public void Configure(EntityTypeBuilder<PedidoFacturableSnapshot> builder)
    {
        builder.ToTable("pedido_facturable_snapshot");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.PedidoFacturableId).IsRequired();
        builder.Property(e => e.PayloadCrudo).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.LeidoAt).IsRequired();

        builder.HasIndex(e => e.PedidoFacturableId)
            .HasDatabaseName("ix_pedido_facturable_snapshot_pedido");
    }
}
