using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.Saldos;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="SaldoInventario"/> (F2-PR1).
/// Tabla materializada <c>almacen.saldos_inventario</c>. PK compuesto
/// <c>(sub_almacen_id, articulo_id)</c>.
///
/// <para>
/// <b>Columnas generadas STORED</b>:
/// <list type="bullet">
///   <item><c>cantidad_disponible</c> = <c>cantidad - cantidad_reservada</c>.</item>
///   <item><c>valor_inventario_mxn</c> = <c>cantidad * costo_promedio_mxn</c>.</item>
/// </list>
/// Se declaran como <c>HasComputedColumnSql(..., stored: true)</c>; el
/// trigger PG no las escribe (PostgreSQL las calcula automáticamente).
/// </para>
///
/// <para>
/// <b>CHECK constraints</b>: cantidad >= 0, cantidad_reservada >= 0,
/// cantidad_reservada &lt;= cantidad. La invariante del módulo
/// (cuidado §3.1 del 04-cuidados-infra) prohíbe saldo negativo —
/// el trigger PG verifica ANTES de aplicar y aborta la transacción
/// si una salida dejaría stock &lt; 0.
/// </para>
/// </summary>
public sealed class SaldoInventarioConfiguration : IEntityTypeConfiguration<SaldoInventario>
{
    public void Configure(EntityTypeBuilder<SaldoInventario> builder)
    {
        builder.ToTable("saldos_inventario", t =>
        {
            t.HasCheckConstraint("ck_saldos_cantidad_no_negativa", "cantidad >= 0");
        });
        builder.HasKey(x => new { x.UbicacionId, x.ArticuloId });

        builder.Property(x => x.UbicacionId).IsRequired();
        // SubAlmacenId denormalizado: se conserva junto a UbicacionId durante
        // PR2→PR4 para que el código de reserva (llaveado por sub_almacen_id)
        // siga intacto. PR4 lo elimina. Ver ADR-0047 (estado intermedio cut-A).
        builder.Property(x => x.SubAlmacenId).IsRequired();
        builder.Property(x => x.ArticuloId).IsRequired();
        builder.Property(x => x.Cantidad).HasPrecision(14, 4).IsRequired();
        builder.Property(x => x.CostoPromedioMxn).HasPrecision(14, 4).IsRequired();

        // Columnas generadas STORED — PostgreSQL las calcula al insert/update.
        // PR4 (ADR-0047): sin reservas, disponible == cantidad física.
        builder.Property(x => x.CantidadDisponible)
            .HasPrecision(14, 4)
            .HasComputedColumnSql("cantidad", stored: true);
        builder.Property(x => x.ValorInventarioMxn)
            .HasPrecision(14, 2)
            .HasComputedColumnSql("cantidad * costo_promedio_mxn", stored: true);

        builder.Property(x => x.UltimaActualizacionAt).IsRequired();
        builder.Property(x => x.UltimoMovimientoId);

        // Índices §5.2 del 01-diseno.
        builder.HasIndex(x => new { x.SubAlmacenId, x.ArticuloId })
            .HasDatabaseName("ix_saldos_sub_almacen");
        builder.HasIndex(x => new { x.ArticuloId, x.SubAlmacenId })
            .HasDatabaseName("ix_saldos_con_stock")
            .HasFilter("cantidad > 0");

        // FK a almacen.ubicaciones (Nivel 4, nuevo dueño de la existencia).
        builder.HasOne<Almacen.Domain.Catalogo.Ubicacion>()
            .WithMany()
            .HasForeignKey(x => x.UbicacionId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK a almacen.sub_almacenes conservada (columna denormalizada). Se
        // elimina en PR4 junto con el desmonte de reservas.
        builder.HasOne<Almacen.Domain.Catalogo.SubAlmacen>()
            .WithMany()
            .HasForeignKey(x => x.SubAlmacenId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
