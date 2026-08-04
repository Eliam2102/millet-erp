using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.DevolucionesProveedor;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="DevolucionAProveedor"/> (F6-PR1,
/// sub-flujo 8.B). Tabla <c>almacen.devoluciones_proveedor</c>. CHECK
/// constraints en el estado + autorización con timestamps.
/// </summary>
public sealed class DevolucionAProveedorConfiguration : IEntityTypeConfiguration<DevolucionAProveedor>
{
    public void Configure(EntityTypeBuilder<DevolucionAProveedor> builder)
    {
        builder.ToTable("devoluciones_proveedor", t =>
        {
            t.HasCheckConstraint("ck_devoluciones_prov_estado",
                "estado BETWEEN 0 AND 5");
            // estado 3 (Registrada) y 4 (ConciliadaConNcFiscal) exigen
            // movimiento de salida. La versión anterior — `(estado = 3
            // AND mov NOT NULL) OR (estado <> 3 AND estado <> 4)` — hacía
            // IMPOSIBLE el estado 4: ninguna rama lo satisfacía, y el
            // listener de NC fiscal moría con 23514 al conciliar
            // (incidente verificación e2e 2026-07-15).
            t.HasCheckConstraint("ck_devoluciones_prov_registrada_tiene_movimiento",
                "(estado IN (3, 4) AND movimiento_salida_id IS NOT NULL) OR (estado <> 3 AND estado <> 4)");
            t.HasCheckConstraint("ck_devoluciones_prov_conciliada_tiene_nc",
                "(estado = 4 AND nota_credito_fiscal_id IS NOT NULL) OR (estado <> 4)");
            t.HasCheckConstraint("ck_devoluciones_prov_rechazada_tiene_motivo",
                "(estado = 5 AND motivo_rechazo IS NOT NULL) OR (estado <> 5)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.EmpresaId).IsRequired();
        builder.Property(x => x.ProveedorId).IsRequired();
        builder.Property(x => x.RecepcionOrigenId);
        builder.Property(x => x.FacturaProveedorOrigenId);
        builder.Property(x => x.OrdenCompraOrigenId);
        builder.Property(x => x.SubAlmacenOrigenId);
        builder.Property(x => x.Estado).HasConversion<short>().IsRequired();
        builder.Property(x => x.Motivo).HasMaxLength(1000).IsRequired();

        builder.Property(x => x.SolicitadaPor).IsRequired();
        builder.Property(x => x.SolicitadaAt).IsRequired();
        builder.Property(x => x.AutorizadaPor);
        builder.Property(x => x.AutorizadaAt);
        builder.Property(x => x.MotivoRechazo).HasMaxLength(500);
        builder.Property(x => x.RechazadaAt);

        builder.Property(x => x.MovimientoSalidaId);
        builder.Property(x => x.FolioMovimientoSalida).HasMaxLength(30);
        builder.Property(x => x.RegistradaAt);

        builder.Property(x => x.NotaCreditoFiscalId);
        builder.Property(x => x.ConciliadaConNcFiscalAt);

        // Bandeja "Devoluciones pendientes de NC fiscal".
        builder.HasIndex(x => x.ProveedorId)
            .HasDatabaseName("ix_devoluciones_prov_proveedor");
        builder.HasIndex(x => x.Estado)
            .HasDatabaseName("ix_devoluciones_prov_estado");
        builder.HasIndex(x => x.ProveedorId)
            .HasDatabaseName("ix_devoluciones_prov_pendientes_nc_fiscal")
            .HasFilter("estado = 3"); // Registrada (esperando NC fiscal).

        // Hijos: líneas + evidencias.
        builder.HasMany(x => x.Lineas)
            .WithOne()
            .HasForeignKey(l => l.DevolucionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Evidencias)
            .WithOne()
            .HasForeignKey(e => e.DevolucionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LineaDevolucionProveedorConfiguration : IEntityTypeConfiguration<LineaDevolucionProveedor>
{
    public void Configure(EntityTypeBuilder<LineaDevolucionProveedor> builder)
    {
        builder.ToTable("lineas_devolucion_proveedor", t =>
        {
            t.HasCheckConstraint("ck_lineas_dev_prov_cantidad_positiva", "cantidad > 0");
            t.HasCheckConstraint("ck_lineas_dev_prov_costo_no_negativo", "costo_unitario_mxn >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.DevolucionId).IsRequired();
        builder.Property(x => x.Posicion).IsRequired();
        builder.Property(x => x.ArticuloId).IsRequired();
        builder.Property(x => x.Cantidad).HasPrecision(14, 4).IsRequired();
        builder.Property(x => x.UnidadMedida).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CostoUnitarioMxn).HasPrecision(14, 4).IsRequired();
        builder.Property(x => x.MontoTotalMxn).HasPrecision(14, 2).IsRequired();
        builder.Property(x => x.LineaRecepcionOrigenId);

        builder.HasIndex(x => x.DevolucionId);
        builder.HasIndex(x => x.ArticuloId);
    }
}

public sealed class EvidenciaDevolucionProveedorConfiguration : IEntityTypeConfiguration<EvidenciaDevolucionProveedor>
{
    public void Configure(EntityTypeBuilder<EvidenciaDevolucionProveedor> builder)
    {
        builder.ToTable("evidencias_devolucion_proveedor");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.DevolucionId).IsRequired();
        builder.Property(x => x.TipoEvidencia).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NombreArchivo).HasMaxLength(254).IsRequired();
        builder.Property(x => x.BlobRef).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Comentario).HasMaxLength(500);

        builder.HasIndex(x => x.DevolucionId);
    }
}
