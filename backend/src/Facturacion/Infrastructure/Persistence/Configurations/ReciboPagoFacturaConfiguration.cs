using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Repp;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="ReciboPagoFactura"/> (F6). Tabla
/// <c>facturacion.recibo_pago_factura</c> — entidad hija del <see cref="ReciboPago"/>.
/// </summary>
public sealed class ReciboPagoFacturaConfiguration : IEntityTypeConfiguration<ReciboPagoFactura>
{
    public void Configure(EntityTypeBuilder<ReciboPagoFactura> builder)
    {
        builder.ToTable("recibo_pago_factura");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ReciboPagoId).IsRequired();
        builder.Property(e => e.FacturaVentaId).IsRequired();
        builder.Property(e => e.FacturaUuid).HasMaxLength(36).IsRequired();
        builder.Property(e => e.NumParcialidad).IsRequired();
        builder.Property(e => e.MonedaFactura).HasMaxLength(3).IsRequired();

        builder.Property(e => e.ImportePagado).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.SaldoAnterior).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.SaldoInsoluto).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.GananciaPerdidaCambiaria).HasPrecision(18, 2).IsRequired();

        builder.Property(e => e.FormaPagoReal).HasMaxLength(5).IsRequired();
        builder.Property(e => e.TcPago).HasPrecision(18, 6);

        builder.Property(e => e.CuentaOrdenante).HasMaxLength(50);
        builder.Property(e => e.CuentaBeneficiaria).HasMaxLength(50);
        builder.Property(e => e.ReferenciaPago).HasMaxLength(100);

        // ImpuestosDR (Pago 2.0): objeto de impuesto + equivalencia + base gravada.
        builder.Property(e => e.ObjetoImpDR).HasMaxLength(2).IsRequired();
        builder.Property(e => e.Equivalencia).HasPrecision(18, 6).IsRequired();
        builder.Property(e => e.BaseGravablePagada).HasPrecision(18, 6).IsRequired();

        builder.HasMany(e => e.Impuestos)
            .WithOne()
            .HasForeignKey(i => i.ReciboPagoFacturaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.FacturaVentaId)
            .HasDatabaseName("ix_recibo_pago_factura_factura");
    }
}
