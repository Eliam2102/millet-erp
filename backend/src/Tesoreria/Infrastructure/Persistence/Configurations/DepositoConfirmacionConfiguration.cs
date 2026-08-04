using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

public sealed class DepositoConfirmacionConfiguration : IEntityTypeConfiguration<DepositoConfirmacion>
{
    public void Configure(EntityTypeBuilder<DepositoConfirmacion> builder)
    {
        builder.ToTable("deposito_confirmacion", t =>
        {
            t.HasCheckConstraint("ck_deposito_confirmacion_estado", "estado IN (1, 2, 3)");
            // Todo depósito tiene un origen: propuesta CxC o expectativa de Caja.
            // GI-PR4a agregó el origen viáticos; sin él en el check, la
            // proyección del depósito esperado tronaba con 23514 (5
            // reintentos → DLQ, incidente dev 2026-07-17).
            t.HasCheckConstraint("ck_deposito_confirmacion_origen",
                "propuesta_cxc_id IS NOT NULL OR caja_sesion_id IS NOT NULL OR solicitud_viaticos_id IS NOT NULL");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.MovimientoId);
        builder.Property(e => e.PropuestaCxcId);
        builder.Property(e => e.ClienteId);
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.MotivoRechazo).HasMaxLength(400);
        builder.Property(e => e.CajaSesionId);
        builder.Property(e => e.SolicitudViaticosId);
        builder.Property(e => e.ReppTimbrado).IsRequired();
        builder.Property(e => e.FacturasJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.DepositoRef).HasMaxLength(80);
        builder.Property(e => e.MontoEsperado).HasPrecision(18, 2);
        builder.Property(e => e.Moneda).HasMaxLength(3);
        builder.Property(e => e.ResueltaPor);
        builder.Property(e => e.ResueltaEn);

        builder.HasOne<MovimientoBancario>()
            .WithMany()
            .HasForeignKey(e => e.MovimientoId)
            .OnDelete(DeleteBehavior.Restrict);

        // Bandeja de depósitos por confirmar (P2 server-side).
        builder.HasIndex(e => new { e.EmpresaId, e.Estado })
            .HasDatabaseName("ix_deposito_confirmacion_empresa_estado");

        // Idempotencia del listener: una fila por propuesta / sesión de caja.
        builder.HasIndex(e => e.PropuestaCxcId)
            .IsUnique()
            .HasFilter("propuesta_cxc_id IS NOT NULL")
            .HasDatabaseName("ux_deposito_confirmacion_propuesta");

        builder.HasIndex(e => e.CajaSesionId)
            .IsUnique()
            .HasFilter("caja_sesion_id IS NOT NULL")
            .HasDatabaseName("ux_deposito_confirmacion_caja_sesion");

        // GI-PR4: una expectativa por liquidación de viáticos.
        builder.HasIndex(e => e.SolicitudViaticosId)
            .IsUnique()
            .HasFilter("solicitud_viaticos_id IS NOT NULL")
            .HasDatabaseName("ux_deposito_confirmacion_solicitud_viaticos");
    }
}
