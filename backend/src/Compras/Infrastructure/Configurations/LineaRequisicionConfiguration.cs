using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain;

namespace Millet.Compras.Infrastructure.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="LineaRequisicion"/> (tabla
/// <c>compras.requisicion_lineas</c>, diseño §10.3).
///
/// CHECKs:
/// <list type="bullet">
///   <item><c>cantidad &gt; 0</c></item>
///   <item><c>cant_de_almacen + cant_de_compra &lt;= cantidad</c></item>
///   <item><c>cant_recibida &lt;= cant_de_compra</c></item>
///   <item><c>cant_entregada &gt;= 0</c> (ADR-0043; sin tope superior por
///     diseño — la entrega es lenient/robusta y puede exceder el techo
///     almacén+recibida sin lanzar, ver <c>Requisicion.RegistrarEntrega</c>)</item>
///   <item><c>moneda ~ '^[A-Z]{3}$'</c></item>
/// </list>
///
/// FK <c>requisicion_id → compras.requisiciones</c> con
/// <c>ON DELETE CASCADE</c>: borrar la requisición borra sus líneas.
/// (FK física segura por ser misma schema, cuidado §1.4 OK.)
///
/// UNIQUE <c>(requisicion_id, posicion)</c>: la posición es única por
/// requisición.
///
/// El VO <see cref="Money"/> se mapea con <c>OwnsOne</c> a las columnas
/// <c>precio_estimado</c> (decimal 18,6) y <c>moneda</c> (char(3)
/// DEFAULT 'MXN').
///
/// La <see cref="LineaRequisicion.Cubrimiento"/> es computed; sus 3
/// cantidades de cubrimiento se persisten como columnas separadas y el
/// VO se reconstruye en memoria al leer.
/// </summary>
public sealed class LineaRequisicionConfiguration : IEntityTypeConfiguration<LineaRequisicion>
{
    public void Configure(EntityTypeBuilder<LineaRequisicion> builder)
    {
        builder.ToTable("requisicion_lineas", t =>
        {
            t.HasCheckConstraint("ck_requisicion_lineas_cantidad_positiva", "cantidad > 0");
            t.HasCheckConstraint(
                "ck_requisicion_lineas_cubrimiento_no_excede",
                "cant_de_almacen + cant_de_compra <= cantidad");
            t.HasCheckConstraint(
                "ck_requisicion_lineas_recibida_no_excede_compra",
                "cant_recibida <= cant_de_compra");
            // ADR-0043: solo no-negatividad. Sin tope superior — la entrega
            // es robusta (acumula y avisa, no lanza) y puede exceder el techo
            // almacén+recibida durante la convivencia con el cierre viejo.
            t.HasCheckConstraint(
                "ck_requisicion_lineas_entregada_no_negativa",
                "cant_entregada >= 0");
            t.HasCheckConstraint("ck_requisicion_lineas_moneda_iso", "moneda ~ '^[A-Z]{3}$'");
        });

        builder.HasKey(l => l.Id);

        // Indicar a EF Core que el Id no se genera por BD; lo asigna el
        // dominio con Guid.CreateVersion7. Sin esto, al agregar una línea
        // vía collection navigation (Requisicion.AgregarLinea), EF Core
        // infiere "el Id no es default → ya existe en BD" y emite UPDATE
        // en lugar de INSERT, causando DbUpdateConcurrencyException.
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.RequisicionId).IsRequired();
        builder.Property(l => l.Posicion).IsRequired();

        builder.Property(l => l.ArticuloId).IsRequired();

        builder.Property(l => l.Cantidad)
            .HasPrecision(18, 5)
            .IsRequired();

        builder.Property(l => l.UnidadMedida)
            .HasMaxLength(20)
            .IsRequired();

        // VO Money (readonly record struct) → 2 columnas (precio_estimado,
        // moneda). EF Core 8+ usa ComplexProperty para value objects que
        // son struct (OwnsOne está pensado para clases mutables).
        builder.ComplexProperty(l => l.PrecioEstimado, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("precio_estimado")
                .HasPrecision(18, 6)
                .IsRequired();
            money.Property(m => m.Currency)
                .HasColumnName("moneda")
                .HasMaxLength(3)
                .HasDefaultValue("MXN")
                .IsRequired();
        });

        builder.Property(l => l.CuentaContableId);
        builder.Property(l => l.CentroCostoId);
        builder.Property(l => l.Proyecto).HasMaxLength(200);
        builder.Property(l => l.FechaRequerida);
        builder.Property(l => l.Notas).HasMaxLength(500);

        builder.Property(l => l.CantidadDeAlmacen)
            .HasColumnName("cant_de_almacen")
            .HasPrecision(18, 5)
            .HasDefaultValue(0m)
            .IsRequired();
        builder.Property(l => l.CantidadDeCompra)
            .HasColumnName("cant_de_compra")
            .HasPrecision(18, 5)
            .HasDefaultValue(0m)
            .IsRequired();
        builder.Property(l => l.CantidadRecibida)
            .HasColumnName("cant_recibida")
            .HasPrecision(18, 5)
            .HasDefaultValue(0m)
            .IsRequired();
        builder.Property(l => l.CantidadEntregada)
            .HasColumnName("cant_entregada")
            .HasPrecision(18, 5)
            .HasDefaultValue(0m)
            .IsRequired();

        // Ignorar los computed: no se persisten, se derivan al leer
        // a partir de las cantidades (Cubrimiento y, ADR-0043 #3, el
        // pendiente de entregar = (almacén+recibida) − entregada).
        builder.Ignore(l => l.Cubrimiento);
        builder.Ignore(l => l.CantidadPendienteEntregar);

        builder.HasIndex(l => new { l.RequisicionId, l.Posicion }).IsUnique();
    }
}
