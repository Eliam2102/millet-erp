using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Configurations;

public sealed class PropuestaAplicacionPagoConfiguration : IEntityTypeConfiguration<PropuestaAplicacionPago>
{
    public void Configure(EntityTypeBuilder<PropuestaAplicacionPago> builder)
    {
        builder.ToTable("propuesta_aplicacion_pago", t =>
        {
            t.HasCheckConstraint("ck_propuesta_aplicacion_monto_positivo", "monto_deposito > 0");
            t.HasCheckConstraint("ck_propuesta_aplicacion_estado", "estado IN (1, 2, 3)");
            t.HasCheckConstraint("ck_propuesta_aplicacion_ajuste_no_positivo", "ajuste_no_fiscal <= 0");
            t.HasCheckConstraint(
                "ck_propuesta_aplicacion_rechazo_consistente",
                "(estado = 3 AND motivo_rechazo IS NOT NULL) OR (estado != 3 AND motivo_rechazo IS NULL)");
            t.HasCheckConstraint(
                "ck_propuesta_aplicacion_resolucion_consistente",
                "(estado = 1 AND resuelta_por IS NULL AND resuelta_en IS NULL) OR " +
                "(estado != 1 AND resuelta_por IS NOT NULL AND resuelta_en IS NOT NULL)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.ClienteId).IsRequired();
        builder.Property(e => e.DepositoRef).HasMaxLength(80).IsRequired();
        builder.Property(e => e.MontoDeposito).HasPrecision(14, 2).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.RemittanceRef).HasMaxLength(120).IsRequired();
        builder.Property(e => e.AjusteNoFiscal).HasPrecision(14, 2).IsRequired();

        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.MotivoRechazo).HasMaxLength(400);
        builder.Property(e => e.ResueltaPor);
        builder.Property(e => e.ResueltaEn);

        builder.HasMany(e => e.Facturas)
            .WithOne()
            .HasForeignKey(f => f.PropuestaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(PropuestaAplicacionPago.Facturas))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // §5.1: bandeja de pendientes de Ingresos.
        builder.HasIndex(e => e.Estado)
            .HasDatabaseName("ix_propuesta_aplicacion_pendientes")
            .HasFilter("estado = 1");

        builder.HasIndex(e => new { e.ClienteId, e.Estado })
            .HasDatabaseName("ix_propuesta_aplicacion_cliente_estado");
    }
}

public sealed class PropuestaAplicacionFacturaConfiguration : IEntityTypeConfiguration<PropuestaAplicacionFactura>
{
    public void Configure(EntityTypeBuilder<PropuestaAplicacionFactura> builder)
    {
        builder.ToTable("propuesta_aplicacion_factura", t =>
        {
            t.HasCheckConstraint("ck_propuesta_factura_importe_positivo", "importe_aplicado > 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.PropuestaId).IsRequired();
        builder.Property(e => e.FacturaCarteraId).IsRequired();
        builder.Property(e => e.FacturaUuid).HasMaxLength(36).IsRequired();
        builder.Property(e => e.ImporteAplicado).HasPrecision(14, 2).IsRequired();
        builder.Property(e => e.NumParcialidad);

        builder.HasIndex(e => new { e.PropuestaId, e.FacturaUuid })
            .HasDatabaseName("ux_propuesta_factura_uuid")
            .IsUnique();
    }
}
