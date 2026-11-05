using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Pasivos;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

public sealed class AutorizacionPagoComercioExteriorConfiguration : IEntityTypeConfiguration<AutorizacionPagoComercioExterior>
{
    public void Configure(EntityTypeBuilder<AutorizacionPagoComercioExterior> builder)
    {
        builder.ToTable("autorizacion_pago_comercio_exterior", t =>
            t.HasCheckConstraint("ck_autorizacion_pago_ce_tipo", "tipo IN (1, 2)"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.FacturaProveedorId).IsRequired();
        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.MotivoRechazo).HasMaxLength(400);
        builder.HasIndex(e => e.FacturaProveedorId).HasDatabaseName("ux_autorizacion_pago_ce_factura").IsUnique();
    }
}
