using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="MovimientoBancario"/> y
/// <see cref="AplicacionPagoProveedor"/> (§5 del 01-diseño). Incluye el
/// índice parcial único <c>ux_pago_cuenta_abierto</c> que respalda RN-2
/// (máximo un pago no aplicado abierto por proveedor) — no confiar solo
/// en la validación del command ante dobles submit (cuidados-infra §5).
/// </summary>
public sealed class MovimientoBancarioConfiguration : IEntityTypeConfiguration<MovimientoBancario>
{
    public void Configure(EntityTypeBuilder<MovimientoBancario> builder)
    {
        builder.ToTable("movimiento_bancario", t =>
        {
            t.HasCheckConstraint("ck_movimiento_bancario_monto_positivo", "monto > 0");
            t.HasCheckConstraint("ck_movimiento_bancario_sentido", "sentido IN (1, 2)");
            t.HasCheckConstraint("ck_movimiento_bancario_estado_aplicacion", "estado_aplicacion IN (1, 2, 3)");
            t.HasCheckConstraint("ck_movimiento_bancario_estado_conciliacion", "estado_conciliacion IN (1, 2)");
            t.HasCheckConstraint(
                "ck_movimiento_bancario_beneficiario_tipo",
                "beneficiario_tipo IS NULL OR beneficiario_tipo IN (1, 2, 3)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.CuentaBancariaId).IsRequired();
        builder.Property(e => e.Sentido).HasConversion<short>().IsRequired();
        builder.Property(e => e.Monto).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.FechaValor).IsRequired();
        builder.Property(e => e.ReferenciaBancaria).HasMaxLength(120);
        builder.Property(e => e.ConceptoId);

        builder.Property(e => e.EstadoAplicacion).HasConversion<short>().IsRequired();
        builder.Property(e => e.EstadoConciliacion).HasConversion<short>().IsRequired();
        builder.Property(e => e.BeneficiarioTipo).HasConversion<short>();
        builder.Property(e => e.BeneficiarioRef);
        builder.Property(e => e.ContramovimientoDe);
        builder.Property(e => e.MotivoNoAplicado).HasMaxLength(400);
        builder.Property(e => e.CreadoPor).IsRequired();
        builder.Property(e => e.CreadoEn).IsRequired();

        builder.HasOne<CuentaBancaria>()
            .WithMany()
            .HasForeignKey(e => e.CuentaBancariaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ConceptoMovimiento>()
            .WithMany()
            .HasForeignKey(e => e.ConceptoId)
            .OnDelete(DeleteBehavior.Restrict);

        // RN-10: el contramovimiento referencia al movimiento original.
        builder.HasOne<MovimientoBancario>()
            .WithMany()
            .HasForeignKey(e => e.ContramovimientoDe)
            .OnDelete(DeleteBehavior.Restrict);

        // §5.1: auxiliares por cuenta/periodo y matching de conciliación.
        builder.HasIndex(e => new { e.CuentaBancariaId, e.FechaValor })
            .HasDatabaseName("ix_movimiento_bancario_cuenta_fecha");

        // §5.2: respaldo del gate RN-2 — máximo un egreso NoAplicado a
        // proveedor abierto por (empresa, proveedor); los contramovimientos
        // quedan fuera del gate.
        builder.HasIndex(e => new { e.EmpresaId, e.BeneficiarioRef })
            .HasDatabaseName("ux_pago_cuenta_abierto")
            .IsUnique()
            .HasFilter("sentido = 2 AND estado_aplicacion = 1 AND beneficiario_tipo = 1 AND contramovimiento_de IS NULL");
    }
}

public sealed class AplicacionPagoProveedorConfiguration : IEntityTypeConfiguration<AplicacionPagoProveedor>
{
    public void Configure(EntityTypeBuilder<AplicacionPagoProveedor> builder)
    {
        builder.ToTable("aplicacion_pago_proveedor", t =>
        {
            t.HasCheckConstraint("ck_aplicacion_pago_importe_positivo", "importe_aplicado > 0");
        });

        // El Id de la fila ES el PagoId del evento aplicado.v1 (§5 DDL).
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.MovimientoId).IsRequired();
        builder.Property(e => e.FacturaProveedorId).IsRequired();
        builder.Property(e => e.ProveedorId).IsRequired();
        builder.Property(e => e.ImporteAplicado).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.CorridaId);
        builder.Property(e => e.Revertida).IsRequired();
        builder.Property(e => e.CreadoEn).IsRequired();

        builder.HasOne<MovimientoBancario>()
            .WithMany()
            .HasForeignKey(e => e.MovimientoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Corridas.CorridaPago>()
            .WithMany()
            .HasForeignKey(e => e.CorridaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.MovimientoId, e.FacturaProveedorId })
            .HasDatabaseName("ux_aplicacion_pago_movimiento_factura")
            .IsUnique();

        // §5.1: trazabilidad por pasivo.
        builder.HasIndex(e => e.FacturaProveedorId)
            .HasDatabaseName("ix_aplicacion_pago_factura");
    }
}
