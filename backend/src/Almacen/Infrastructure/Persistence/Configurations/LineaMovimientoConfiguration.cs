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
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.MovimientoId).IsRequired();
        builder.Property(x => x.Posicion).IsRequired();
        builder.Property(x => x.ArticuloId).IsRequired();
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
        builder.Property(x => x.CantidadTeoricaAlContar).HasPrecision(14, 4);
        builder.Property(x => x.CantidadRealContada).HasPrecision(14, 4);
        builder.Property(x => x.UbicacionReferencia).HasMaxLength(100);
        builder.Property(x => x.ComentarioLinea).HasMaxLength(500);
        // ADR-0047 C7.2a: bin explícito de la línea. Nullable (NULL = ÚNICA).
        builder.Property(x => x.UbicacionId);

        builder.HasIndex(x => x.MovimientoId);
        builder.HasIndex(x => x.ArticuloId);
        builder.HasIndex(x => x.UbicacionId);

        // FK física a almacen.ubicaciones (mismo esquema). Restrict: no borrar
        // una ubicación referenciada por líneas de movimiento.
        builder.HasOne<Ubicacion>()
            .WithMany()
            .HasForeignKey(x => x.UbicacionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
