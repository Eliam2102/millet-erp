using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Anticipos;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

public sealed class ReversaAplicacionAnticipoConfiguration : IEntityTypeConfiguration<ReversaAplicacionAnticipo>
{
    public void Configure(EntityTypeBuilder<ReversaAplicacionAnticipo> builder)
    {
        builder.ToTable("anticipo_reversa_aplicacion");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Importe).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Motivo).HasMaxLength(ReversaAplicacionAnticipo.MotivoMax).IsRequired();
        builder.Property(e => e.AutorizadoPorNombre).HasMaxLength(200).IsRequired();
        builder.HasIndex(e => new { e.AnticipoId, e.FacturaVentaId }).HasDatabaseName("ix_anticipo_reversa_anticipo_factura");
        builder.HasIndex(e => e.NcAmortizacionId).IsUnique().HasDatabaseName("ux_anticipo_reversa_nc");
    }
}
