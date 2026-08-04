using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Repp;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="ReciboPago"/> (F6). Tabla
/// <c>facturacion.recibo_pago</c> — subtipo TPT (PK = FK 1:1 a <c>comprobante</c>).
/// </summary>
public sealed class ReciboPagoConfiguration : IEntityTypeConfiguration<ReciboPago>
{
    public void Configure(EntityTypeBuilder<ReciboPago> builder)
    {
        builder.ToTable("recibo_pago");

        builder.Property(e => e.FechaPago).IsRequired();
        builder.Property(e => e.ImporteTotalPago).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.MonedaPago).HasMaxLength(3).IsRequired();

        builder.HasMany(e => e.FacturasPagadas)
            .WithOne()
            .HasForeignKey(f => f.ReciboPagoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
