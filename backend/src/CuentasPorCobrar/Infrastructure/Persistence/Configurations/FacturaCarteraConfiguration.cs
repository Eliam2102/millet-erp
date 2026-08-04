using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorCobrar.Domain.Cartera;

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Configurations;

public sealed class FacturaCarteraConfiguration : IEntityTypeConfiguration<FacturaCartera>
{
    public void Configure(EntityTypeBuilder<FacturaCartera> builder)
    {
        builder.ToTable("factura_cartera", t =>
        {
            t.HasCheckConstraint("ck_factura_cartera_total_positivo", "total > 0");
            t.HasCheckConstraint("ck_factura_cartera_estado", "estado IN (1, 2, 3, 4)");
            t.HasCheckConstraint(
                "ck_factura_cartera_acumulados",
                "monto_pagado >= 0 AND monto_nc >= 0 AND monto_pagado + monto_nc <= total");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.FacturaVentaId).IsRequired();
        builder.Property(e => e.ClienteId);
        builder.Property(e => e.ReceptorRfc).HasMaxLength(13).IsRequired();
        builder.Property(e => e.ReceptorNombre).HasMaxLength(300).IsRequired();

        builder.Property(e => e.Uuid).HasMaxLength(36).IsRequired();
        builder.Property(e => e.Folio).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Total).HasPrecision(14, 2).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.MetodoPago).HasMaxLength(3).IsRequired();

        builder.Property(e => e.FechaTimbrado).IsRequired();
        builder.Property(e => e.FechaVencimiento).IsRequired();

        builder.Property(e => e.MontoPagado).HasPrecision(14, 2).IsRequired();
        builder.Property(e => e.MontoNc).HasPrecision(14, 2).IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();

        // §5.1 del 01-diseño — índices críticos de la proyección.
        builder.HasIndex(e => e.FacturaVentaId)
            .HasDatabaseName("ux_factura_cartera_factura_venta")
            .IsUnique();

        builder.HasIndex(e => new { e.ClienteId, e.Estado })
            .HasDatabaseName("ix_factura_cartera_cliente_estado");

        builder.HasIndex(e => e.FechaVencimiento)
            .HasDatabaseName("ix_factura_cartera_vencimiento_abiertas")
            .HasFilter("estado IN (1, 2)");

        builder.HasIndex(e => e.ReceptorRfc)
            .HasDatabaseName("ix_factura_cartera_receptor_rfc");
    }
}

public sealed class MovimientoCarteraConfiguration : IEntityTypeConfiguration<MovimientoCartera>
{
    public void Configure(EntityTypeBuilder<MovimientoCartera> builder)
    {
        builder.ToTable("movimiento_cartera", t =>
        {
            t.HasCheckConstraint("ck_movimiento_cartera_importe_positivo", "importe > 0");
            t.HasCheckConstraint("ck_movimiento_cartera_tipo", "tipo IN (1, 2)");
            t.HasCheckConstraint(
                "ck_movimiento_cartera_reversa_consistente",
                "(revertido = TRUE AND revertido_en IS NOT NULL) OR (revertido = FALSE)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.FacturaCarteraId).IsRequired();
        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.OrigenComprobanteId).IsRequired();
        builder.Property(e => e.Importe).HasPrecision(14, 2).IsRequired();
        builder.Property(e => e.FechaMovimiento).IsRequired();
        builder.Property(e => e.Revertido).IsRequired();
        builder.Property(e => e.RevertidoEn);

        builder.HasOne<FacturaCartera>()
            .WithMany()
            .HasForeignKey(e => e.FacturaCarteraId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reversa por comprobante origen (cancelaciones de REPP / NC / cobro).
        builder.HasIndex(e => e.OrigenComprobanteId)
            .HasDatabaseName("ix_movimiento_cartera_origen");

        builder.HasIndex(e => e.FacturaCarteraId)
            .HasDatabaseName("ix_movimiento_cartera_factura");
    }
}
