using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core del agregado <see cref="FacturaProveedor"/>
/// (F3-PR1). Schema <c>cuentas_por_pagar</c>, tabla
/// <c>facturas_proveedor</c>.
///
/// <para>
/// Índices del §5.1 del 01-diseno:
/// <list type="bullet">
///   <item><c>ix_facturas_proveedor_saldo</c> — antigüedad de saldos
///         (parcial Capturada/Autorizada + saldo &gt; 0).</item>
///   <item><c>ix_facturas_revision_dependencia</c> — bandeja en revisión
///         por área (entra en F4-PR1 cuando exista la columna; aquí
///         registramos el índice ya, vacío hasta entonces).</item>
/// </list>
/// </para>
///
/// <para>
/// **CHECK constraints**: <c>total > 0</c>, <c>saldo_pendiente</c>
/// derivado &gt;= 0 (validado en código + check defensiva en BD).
/// </para>
/// </summary>
public sealed class FacturaProveedorConfiguration : IEntityTypeConfiguration<FacturaProveedor>
{
    public void Configure(EntityTypeBuilder<FacturaProveedor> builder)
    {
        builder.ToTable("facturas_proveedor", t =>
        {
            t.HasCheckConstraint("ck_facturas_proveedor_total_positivo", "total > 0");
            t.HasCheckConstraint("ck_facturas_proveedor_saldo_no_negativo",
                "total - anticipo_aplicado_total - nc_aplicadas_total - importe_pagado >= 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.CfdiRecibidoId);
        builder.Property(e => e.UuidCfdi).HasMaxLength(36);

        builder.Property(e => e.ProveedorId).IsRequired();
        builder.Property(e => e.SucursalId).IsRequired();

        builder.Property(e => e.FolioProveedor).HasMaxLength(40);
        builder.Property(e => e.SerieProveedor).HasMaxLength(25);

        builder.Property(e => e.FechaDocumento).IsRequired();
        builder.Property(e => e.FechaContabilizacion).IsRequired();
        builder.Property(e => e.FechaVencimiento).IsRequired();

        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.TipoCambio).HasPrecision(18, 6);

        // TES-PR8 [T-G11]: PUE/PPD copiado del CFDI ligado en la captura.
        builder.Property(e => e.MetodoPago).HasMaxLength(3);

        builder.Property(e => e.Subtotal).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Descuentos).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.ImpuestosTrasladados).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Retenciones).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Total).HasPrecision(18, 4).IsRequired();

        builder.Property(e => e.OrdenCompraId);
        builder.Property(e => e.EncargadoComprasSnapshot);

        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.ToleranciaTipo).HasConversion<short?>();
        builder.Property(e => e.ToleranciaValor).HasPrecision(18, 4);
        builder.Property(e => e.DiferenciaContraOc).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.RedondeoAplicado).HasPrecision(18, 4).IsRequired();

        builder.Property(e => e.AnticipoAplicadoTotal).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.NcAplicadasTotal).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.ImportePagado).HasPrecision(18, 4).IsRequired();

        // SaldoPendiente es propiedad calculada — no la mapeamos (EF la ignora).
        builder.Ignore(e => e.SaldoPendiente);

        builder.Property(e => e.MotivoDeCancelacion).HasConversion<short?>().HasColumnName("motivo_cancelacion");
        builder.Property(e => e.MotivoCancelacionTexto).HasMaxLength(400);
        builder.Property(e => e.FechaCancelacion);

        builder.Property(e => e.EnRevision).IsRequired();
        builder.Property(e => e.MotivoRevisionId);
        builder.Property(e => e.DependenciaRevisoraId);
        builder.Property(e => e.FechaEntradaRevision);

        builder.HasMany(e => e.Lineas)
            .WithOne()
            .HasForeignKey(l => l.FacturaProveedorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Bitacora)
            .WithOne()
            .HasForeignKey(b => b.FacturaProveedorId)
            .OnDelete(DeleteBehavior.Cascade);

        // Índices §5.1.
        builder.HasIndex(e => new { e.ProveedorId, e.FechaVencimiento })
            .HasDatabaseName("ix_facturas_proveedor_saldo")
            .HasFilter("estado IN (1, 3) AND (total - anticipo_aplicado_total - nc_aplicadas_total - importe_pagado) > 0");

        builder.HasIndex(e => new { e.DependenciaRevisoraId, e.FechaEntradaRevision })
            .HasDatabaseName("ix_facturas_revision_dependencia")
            .HasFilter("en_revision = true");

        builder.HasIndex(e => new { e.EmpresaId, e.Estado, e.FechaDocumento })
            .HasDatabaseName("ix_facturas_proveedor_bandeja");

        // UUID puede repetirse en escenarios de re-emisión por el proveedor;
        // la unicidad estricta vive en CfdiRecibido. Aquí solo índice
        // secundario para lookup por UUID.
        builder.HasIndex(e => e.UuidCfdi)
            .HasDatabaseName("ix_facturas_proveedor_uuid")
            .HasFilter("uuid_cfdi IS NOT NULL");
    }
}
