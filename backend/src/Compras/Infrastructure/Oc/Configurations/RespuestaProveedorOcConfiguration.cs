using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Infrastructure.Oc.Configurations;

public sealed class RespuestaProveedorOcConfiguration : IEntityTypeConfiguration<RespuestaProveedorOc>
{
    public void Configure(EntityTypeBuilder<RespuestaProveedorOc> builder)
    {
        builder.ToTable("oc_respuestas_proveedor",
            t => t.HasCheckConstraint("ck_oc_respuestas_proveedor_tipo", "tipo IN (1, 2)"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Ignore(r => r.AggregateRootId);
        builder.Property(r => r.Tipo).HasConversion<short>();
        builder.Property(r => r.Evidencia).HasMaxLength(300).IsRequired();
        builder.Property(r => r.ContactoProveedor).HasMaxLength(120);
        builder.Property(r => r.Motivo).HasMaxLength(500);
        builder.HasIndex(r => new { r.OrdenCompraId, r.Numero }).IsUnique()
            .HasDatabaseName("ux_oc_respuestas_proveedor_numero");
        builder.HasMany(r => r.Compromisos).WithOne()
            .HasForeignKey(c => c.RespuestaProveedorOcId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Compromisos).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
