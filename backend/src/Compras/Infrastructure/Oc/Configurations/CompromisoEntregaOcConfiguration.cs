using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Infrastructure.Oc.Configurations;

public sealed class CompromisoEntregaOcConfiguration : IEntityTypeConfiguration<CompromisoEntregaOc>
{
    public void Configure(EntityTypeBuilder<CompromisoEntregaOc> builder)
    {
        builder.ToTable("oc_compromisos_entrega",
            t => t.HasCheckConstraint("ck_oc_compromisos_entrega_cantidad", "cantidad > 0"));
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Ignore(c => c.AggregateRootId);
        builder.Property(c => c.Cantidad).HasColumnType("numeric(15,4)");
        builder.HasOne<LineaOrdenCompra>().WithMany()
            .HasForeignKey(c => c.LineaOrdenCompraId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.LineaOrdenCompraId);
    }
}
