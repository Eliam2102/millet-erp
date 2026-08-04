using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="NotaCreditoProveedor"/> (F6-PR1).
/// Tabla <c>notas_credito_proveedor</c>.
///
/// <para>
/// Índice parcial <c>(uuid_relacion_cfdi, proveedor_id)</c> filtrado
/// por <c>estado = EnEspera</c>: el worker
/// <c>NotaCreditoEnEsperaMatchWorker</c> escanea NCs pendientes y
/// busca facturas nuevas del proveedor por ese UUID. Postgres
/// mantiene el índice pequeño aunque la tabla crezca a miles.
/// </para>
/// </summary>
public sealed class NotaCreditoProveedorConfiguration : IEntityTypeConfiguration<NotaCreditoProveedor>
{
    public void Configure(EntityTypeBuilder<NotaCreditoProveedor> builder)
    {
        builder.ToTable("notas_credito_proveedor", t =>
        {
            t.HasCheckConstraint("ck_nc_total_positivo", "total > 0");
            t.HasCheckConstraint("ck_nc_monto_aplicado_no_excede_total",
                "monto_aplicado >= 0 AND monto_aplicado <= total");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.CfdiRecibidoId);

        builder.Property(e => e.UuidCfdi).HasMaxLength(36).IsRequired();
        builder.Property(e => e.ProveedorId).IsRequired();

        builder.Property(e => e.FolioProveedor).HasMaxLength(40);
        builder.Property(e => e.SerieProveedor).HasMaxLength(25);

        builder.Property(e => e.FechaCfdi).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.TipoCambio).HasPrecision(18, 6);

        builder.Property(e => e.Subtotal).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.ImpuestosTrasladados).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Retenciones).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Total).HasPrecision(18, 4).IsRequired();

        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.TipoRelacionCfdi).HasConversion<short>().IsRequired();
        builder.Property(e => e.UuidRelacionCfdi).HasMaxLength(36).IsRequired();
        builder.Property(e => e.FacturaOrigenId);

        builder.Property(e => e.MontoAplicado).HasPrecision(18, 4).IsRequired();
        builder.Ignore(e => e.SaldoPorAplicar);

        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.FechaCaptura).IsRequired();
        builder.Property(e => e.FechaMatch);
        builder.Property(e => e.FechaCancelacion);
        builder.Property(e => e.MotivoCancelacion).HasMaxLength(400);
        builder.Property(e => e.CapturadoPor);

        // Unicidad por UUID — el SAT garantiza unicidad global.
        builder.HasIndex(e => e.UuidCfdi)
            .HasDatabaseName("ux_notas_credito_proveedor_uuid")
            .IsUnique();

        // Match EnEspera (A19): query parcial barato.
        builder.HasIndex(e => new { e.ProveedorId, e.UuidRelacionCfdi })
            .HasDatabaseName("ix_nc_en_espera_match")
            .HasFilter("estado = 1"); // EstadoNotaCredito.EnEspera

        // Bandeja por proveedor.
        builder.HasIndex(e => new { e.ProveedorId, e.FechaCfdi })
            .HasDatabaseName("ix_nc_proveedor_fecha");

        // Bandeja por factura origen.
        builder.HasIndex(e => e.FacturaOrigenId)
            .HasDatabaseName("ix_nc_factura_origen")
            .HasFilter("factura_origen_id IS NOT NULL");
    }
}
