using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class BitacoraEstadoFacturaConfiguration : IEntityTypeConfiguration<BitacoraEstadoFactura>
{
    public void Configure(EntityTypeBuilder<BitacoraEstadoFactura> builder)
    {
        builder.ToTable("bitacora_estado_factura");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.FacturaProveedorId).IsRequired();
        builder.Property(e => e.EstadoAnterior).HasConversion<short>().IsRequired();
        builder.Property(e => e.EstadoNuevo).HasConversion<short>().IsRequired();
        builder.Property(e => e.OcurridoEn).IsRequired();
        builder.Property(e => e.Motivo).HasMaxLength(400);
        builder.Property(e => e.UsuarioId);

        builder.HasIndex(e => new { e.FacturaProveedorId, e.OcurridoEn })
            .HasDatabaseName("ix_bitacora_estado_factura_factura_fecha");
    }
}
