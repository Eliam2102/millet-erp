using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.Viaticos;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class SolicitudViaticosConfiguration : IEntityTypeConfiguration<SolicitudViaticos>
{
    public void Configure(EntityTypeBuilder<SolicitudViaticos> builder)
    {
        builder.ToTable("solicitudes_viaticos", t =>
        {
            t.HasCheckConstraint("ck_via_monto_positivo", "monto_solicitado > 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.EmpleadoId).IsRequired();
        builder.Property(e => e.PuestoId).IsRequired();
        builder.Property(e => e.JefeDirectoId).IsRequired();

        builder.Property(e => e.Destino).HasMaxLength(400).IsRequired();
        builder.Property(e => e.TipoDestino).HasConversion<short>().IsRequired();
        builder.Property(e => e.FechaSalida).IsRequired();
        builder.Property(e => e.FechaRegreso).IsRequired();

        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.MontoSolicitado).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.TopePoliticaSnapshot).HasPrecision(18, 4).IsRequired();

        builder.Property(e => e.ExcedePolitica).IsRequired();
        builder.Property(e => e.JustificacionExceso).HasMaxLength(2000);

        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();

        builder.Property(e => e.AutorizadoPorJefe);
        builder.Property(e => e.FechaAutorizacionJefe);
        builder.Property(e => e.AutorizadoPorDf);
        builder.Property(e => e.FechaAutorizacionDf);
        builder.Property(e => e.RechazadoPor);
        builder.Property(e => e.FechaRechazo);
        builder.Property(e => e.MotivoRechazo).HasMaxLength(1000);

        builder.Property(e => e.FechaSolicitud).IsRequired();
        builder.Property(e => e.FechaAnticipoPagado);
        builder.Property(e => e.FechaComprobacion);
        builder.Property(e => e.FechaLiquidacion);

        builder.Property(e => e.MontoComprobado).HasPrecision(18, 4);
        builder.Property(e => e.DiferenciaLiquidacion).HasPrecision(18, 4);

        builder.Ignore(e => e.DiasEstimados);

        builder.HasMany(e => e.Lineas)
            .WithOne()
            .HasForeignKey(l => l.SolicitudViaticosId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(SolicitudViaticos.Lineas))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(e => new { e.EmpleadoId, e.Estado })
            .HasDatabaseName("ix_via_empleado_estado");
        builder.HasIndex(e => new { e.JefeDirectoId, e.Estado })
            .HasDatabaseName("ix_via_jefe_estado");
    }
}

public sealed class LineaComprobacionViaticosConfiguration : IEntityTypeConfiguration<LineaComprobacionViaticos>
{
    public void Configure(EntityTypeBuilder<LineaComprobacionViaticos> builder)
    {
        builder.ToTable("lineas_comprobacion_viaticos", t =>
        {
            t.HasCheckConstraint("ck_linea_via_total_positivo", "total > 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.SolicitudViaticosId).IsRequired();

        builder.Property(e => e.FacturaProveedorId);
        builder.Property(e => e.CfdiRecibidoId);
        builder.Property(e => e.UuidCfdi).HasMaxLength(36);
        builder.Property(e => e.ProveedorId);
        builder.Property(e => e.FolioProveedor).HasMaxLength(40);
        builder.Property(e => e.FechaGasto).IsRequired();

        builder.Property(e => e.Subtotal).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.ImpuestosTrasladados).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Retenciones).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Total).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Concepto).HasMaxLength(400).IsRequired();

        builder.Property(e => e.EsTicketNoFiscal).IsRequired();

        builder.HasIndex(e => e.FacturaProveedorId)
            .HasDatabaseName("ix_linea_via_factura")
            .HasFilter("factura_proveedor_id IS NOT NULL");
    }
}
