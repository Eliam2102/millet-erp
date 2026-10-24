using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="LineaMovimiento"/> (F2-PR1).
/// Tabla <c>almacen.lineas_movimiento</c>. CHECK <c>cantidad > 0</c>
/// y <c>costo_unitario_mxn >= 0</c>.
/// </summary>
public sealed class LineaMovimientoConfiguration : IEntityTypeConfiguration<LineaMovimiento>
{
    public void Configure(EntityTypeBuilder<LineaMovimiento> builder)
    {
        builder.ToTable("lineas_movimiento", t =>
        {
            t.HasCheckConstraint("ck_lineas_cantidad_positiva", "cantidad > 0");
            t.HasCheckConstraint("ck_lineas_costo_no_negativo", "costo_unitario_mxn >= 0");
            // F1-MP-01 (E07 §5.3): variante y snapshot de medida van juntos.
            t.HasCheckConstraint("ck_lineas_variante_medida",
                "(producto_aw_variante_id IS NULL AND ancho_mm IS NULL AND alto_mm IS NULL) OR " +
                "(producto_aw_variante_id IS NOT NULL AND ancho_mm IS NOT NULL AND ancho_mm > 0 AND alto_mm IS NOT NULL AND alto_mm > 0)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.MovimientoId).IsRequired();
        builder.Property(x => x.Posicion).IsRequired();
        builder.Property(x => x.ArticuloId).IsRequired();
        builder.Property(x => x.CantidadCapturada).HasPrecision(14, 4);
        builder.Property(x => x.UnidadCapturada).HasMaxLength(20);
        builder.Property(x => x.Cantidad).HasPrecision(14, 4).IsRequired();
        builder.Property(x => x.UnidadMedida).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CostoUnitarioMxn).HasPrecision(14, 4).IsRequired();
        builder.Property(x => x.MontoTotalMxn).HasPrecision(14, 2).IsRequired();
        builder.Property(x => x.MonedaOriginal).HasMaxLength(3).IsRequired();
        builder.Property(x => x.TipoCambioAplicado).HasPrecision(10, 4);

        builder.Property(x => x.LineaFacturaId);
        builder.Property(x => x.CentroCostoId);
        builder.Property(x => x.ProyectoId);
        // ADR-0043: línea de RQ surtida (canal de entrega). Nullable.
        builder.Property(x => x.LineaRqId);
        builder.Property(x => x.LineaOcId);
        builder.Property(x => x.LineaSalidaOrigenId);
        builder.HasIndex(x => x.LineaSalidaOrigenId);
        builder.HasIndex(x => x.LineaOcId);
        builder.Property(x => x.CantidadTeoricaAlContar).HasPrecision(14, 4);
        builder.Property(x => x.CantidadRealContada).HasPrecision(14, 4);
        builder.Property(x => x.UbicacionReferencia).HasMaxLength(100);
        builder.Property(x => x.ComentarioLinea).HasMaxLength(500);
        // ADR-0047 C7.2a: bin explícito de la línea. Nullable (NULL = ÚNICA).
        builder.Property(x => x.UbicacionId);

        builder.HasIndex(x => x.MovimientoId);
        builder.Property(x => x.ProductoAwVarianteId);
        builder.Property(x => x.AnchoMm).HasPrecision(12, 3);
        builder.Property(x => x.AltoMm).HasPrecision(12, 3);
        // Prefijo (articulo_id) cubre las búsquedas por artículo que antes usaban ix_lineas_movimiento_articulo_id.
        builder.HasIndex(x => new { x.ArticuloId, x.ProductoAwVarianteId });
        builder.HasIndex(x => x.UbicacionId);

        // FK física a almacen.ubicaciones (mismo esquema). Restrict: no borrar
        // una ubicación referenciada por líneas de movimiento.
        builder.HasOne<Ubicacion>()
            .WithMany()
            .HasForeignKey(x => x.UbicacionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
