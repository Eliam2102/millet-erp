using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.ComprobacionGastos;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class ComprobacionGastosConfiguration : IEntityTypeConfiguration<ComprobacionGastos>
{
    public void Configure(EntityTypeBuilder<ComprobacionGastos> builder)
    {
        builder.ToTable("comprobaciones_gastos", t =>
        {
            t.HasCheckConstraint("ck_comprobacion_monto_no_negativo", "monto_total >= 0");
            t.HasCheckConstraint("ck_comprobacion_destino_reposicion", "destino_reposicion IS NULL OR destino_reposicion IN (1, 2)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.SucursalId).IsRequired();
        builder.Property(e => e.ResponsableId).IsRequired();

        builder.Property(e => e.FechaInicio).IsRequired();
        builder.Property(e => e.FechaFin).IsRequired();

        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.MontoTotal).HasPrecision(18, 4).IsRequired();

        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();

        builder.Property(e => e.NumeroPedimento).HasMaxLength(40);

        builder.Property(e => e.DestinoReposicion).HasConversion<short?>();
        builder.Property(e => e.ReposicionId);

        // Bandeja de saldos por reponer: caja chica aplicada sin reposición.
        builder.HasIndex(e => new { e.SucursalId, e.DestinoReposicion, e.ReposicionId })
            .HasDatabaseName("ix_comprobaciones_reposicion_pendiente")
            .HasFilter("reposicion_id IS NULL AND destino_reposicion IS NOT NULL");

        builder.Property(e => e.AutorizadoPorNivel1);
        builder.Property(e => e.FechaAutorizacionNivel1);

        builder.Property(e => e.AutorizadoPor);
        builder.Property(e => e.AplicadoPor);
        builder.Property(e => e.RechazadoPor);

        builder.Property(e => e.FechaCreacion).IsRequired();
        builder.Property(e => e.FechaEnvioRevision);
        builder.Property(e => e.FechaAutorizacion);
        builder.Property(e => e.FechaAplicacion);
        builder.Property(e => e.FechaRechazo);

        builder.Property(e => e.MotivoRechazo).HasMaxLength(1000);
        builder.Property(e => e.Observaciones).HasMaxLength(2000);

        builder.HasMany(e => e.Lineas)
            .WithOne()
            .HasForeignKey(l => l.ComprobacionGastosId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(ComprobacionGastos.Lineas))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(e => new { e.Tipo, e.Estado, e.FechaCreacion })
            .HasDatabaseName("ix_comprobaciones_tipo_estado_fecha");

        builder.HasIndex(e => new { e.ResponsableId, e.Estado })
            .HasDatabaseName("ix_comprobaciones_responsable_estado");
    }
}

public sealed class LineaComprobacionGastosConfiguration : IEntityTypeConfiguration<LineaComprobacionGastos>
{
    public void Configure(EntityTypeBuilder<LineaComprobacionGastos> builder)
    {
        builder.ToTable("lineas_comprobacion_gastos", t =>
        {
            t.HasCheckConstraint("ck_linea_comp_total_positivo", "total > 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.ComprobacionGastosId).IsRequired();
        builder.Property(e => e.FacturaProveedorId).IsRequired();
        builder.Property(e => e.CfdiRecibidoId);

        builder.Property(e => e.UuidCfdi).HasMaxLength(36);
        builder.Property(e => e.ProveedorId).IsRequired();
        builder.Property(e => e.FolioProveedor).HasMaxLength(40);
        builder.Property(e => e.FechaCfdi).IsRequired();

        builder.Property(e => e.Subtotal).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.ImpuestosTrasladados).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Retenciones).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Total).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();

        builder.Property(e => e.Concepto).HasMaxLength(400);

        builder.HasIndex(e => e.FacturaProveedorId)
            .HasDatabaseName("ux_linea_comp_factura")
            .IsUnique();
    }
}
