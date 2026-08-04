using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Anticipos;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="AnticipoVinculacion"/> (F4-PR1). Tabla
/// <c>facturacion.anticipo_vinculacion</c> — entidad hija del agregado
/// <see cref="Anticipo"/> (sin query filter propio; se accede a través del padre).
/// </summary>
public sealed class AnticipoVinculacionConfiguration : IEntityTypeConfiguration<AnticipoVinculacion>
{
    public void Configure(EntityTypeBuilder<AnticipoVinculacion> builder)
    {
        builder.ToTable("anticipo_vinculacion");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AnticipoId).IsRequired();
        builder.Property(e => e.FacturaVentaId).IsRequired();
        builder.Property(e => e.NcAmortizacionId);
        builder.Property(e => e.Importe).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.CreadoEn).IsRequired();

        // Un anticipo se aplica a lo sumo una vez por factura final.
        builder.HasIndex(e => new { e.AnticipoId, e.FacturaVentaId })
            .IsUnique()
            .HasDatabaseName("ix_anticipo_vinculacion_unica");

        builder.HasIndex(e => e.FacturaVentaId)
            .HasDatabaseName("ix_anticipo_vinculacion_factura");
    }
}
