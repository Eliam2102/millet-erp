using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Activos;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="AutorizacionVentaActivo"/> (F9). Tabla
/// <c>facturacion.autorizacion_venta_activo</c>.
/// </summary>
public sealed class AutorizacionVentaActivoConfiguration : IEntityTypeConfiguration<AutorizacionVentaActivo>
{
    public void Configure(EntityTypeBuilder<AutorizacionVentaActivo> builder)
    {
        builder.ToTable("autorizacion_venta_activo");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.ActivoRef).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Descripcion).HasMaxLength(254).IsRequired();
        builder.Property(e => e.ValorEnLibros).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.DepreciacionAcumulada).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.EsImportacion).IsRequired();
        builder.Property(e => e.PrecioVenta).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.AutorizadoPor).IsRequired();
        builder.Property(e => e.FechaAutorizacion).IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.FacturaVentaId);

        builder.HasIndex(e => new { e.EmpresaId, e.Estado })
            .HasDatabaseName("ix_autorizacion_venta_activo_estado");
    }
}
