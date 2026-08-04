using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Repp;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

public sealed class ReppProveedorRecibidoConfiguration : IEntityTypeConfiguration<ReppProveedorRecibido>
{
    public void Configure(EntityTypeBuilder<ReppProveedorRecibido> builder)
    {
        builder.ToTable("repp_proveedor_recibido");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.FacturaProveedorId).IsRequired();
        builder.Property(e => e.UuidComplemento).IsRequired();
        builder.Property(e => e.FechaComplemento).IsRequired();
        builder.Property(e => e.XmlBlobRef).HasMaxLength(400);
        builder.Property(e => e.RegistradoPor).IsRequired();
        builder.Property(e => e.RegistradoEn).IsRequired();

        builder.HasIndex(e => e.UuidComplemento)
            .HasDatabaseName("ux_repp_recibido_uuid")
            .IsUnique();

        builder.HasIndex(e => e.FacturaProveedorId)
            .HasDatabaseName("ix_repp_recibido_factura");
    }
}
