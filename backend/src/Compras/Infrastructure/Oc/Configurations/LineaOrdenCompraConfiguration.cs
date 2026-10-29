using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Infrastructure.Oc.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="LineaOrdenCompra"/> (tabla
/// <c>compras.orden_compra_lineas</c>, diseño §10.1). Aplica:
///
/// <list type="bullet">
///   <item>CHECK constraints del §10.1: <c>cantidad &gt; 0</c>,
///         <c>precio_unitario &gt;= 0</c>, <c>cantidad_recibida &gt;= 0</c>,
///         <c>cantidad_facturada &gt;= 0</c>,
///         <c>cantidad_recibida &lt;= cantidad</c>,
///         <c>cantidad_facturada &lt;= cantidad</c>,
///         coherencia RQ (<c>requisicion_id</c> y <c>linea_requisicion_id</c>
///         null/no-null juntos).</item>
///   <item>UNIQUE <c>(orden_compra_id, posicion)</c>.</item>
///   <item>4 índices del §10.1.</item>
///   <item>VO <see cref="Domain.Oc.DescuentoLinea"/> como owned (2 columnas
///         <c>descuento_tipo</c> + <c>descuento_valor</c>, ambas nullable
///         a nivel BD para que líneas sin descuento no las llenen).</item>
///   <item>VO <see cref="IndicadorImpuestos"/> mapeado vía
///         <c>HasConversion</c>.</item>
/// </list>
///
/// La FK al agregado raíz con CASCADE se declara en
/// <see cref="OrdenCompraConfiguration"/> vía <c>HasMany().WithOne()</c>
/// para no duplicarla aquí.
/// </summary>
public sealed class LineaOrdenCompraConfiguration : IEntityTypeConfiguration<LineaOrdenCompra>
{
    public void Configure(EntityTypeBuilder<LineaOrdenCompra> builder)
    {
        builder.ToTable("orden_compra_lineas", t =>
        {
            t.HasCheckConstraint("ck_oc_lineas_cantidad_pos", "cantidad > 0");
            t.HasCheckConstraint("ck_oc_lineas_precio_nn", "precio_unitario >= 0");
            t.HasCheckConstraint("ck_oc_lineas_recibida_nn", "cantidad_recibida >= 0");
            t.HasCheckConstraint("ck_oc_lineas_facturada_nn", "cantidad_facturada >= 0");
            // El techo exacto (= cantidad, sin tolerancia en piezas) lo valida el dominio; la base conserva una cota de seguridad (× 2) por datos históricos.
            t.HasCheckConstraint("ck_oc_lineas_recibida_max", "cantidad_recibida <= cantidad * 2");
            t.HasCheckConstraint("ck_oc_lineas_facturada_max", "cantidad_facturada <= GREATEST(cantidad, cantidad_recibida)");
            t.HasCheckConstraint("ck_oc_lineas_rq_coherente",
                "(requisicion_id IS NULL AND linea_requisicion_id IS NULL) OR " +
                "(requisicion_id IS NOT NULL AND linea_requisicion_id IS NOT NULL)");
            t.HasCheckConstraint("ck_oc_lineas_descuento_tipo",
                "descuento_tipo BETWEEN 0 AND 1");
            // F1-MP-01 (E1): variante y snapshot de medida van juntos (igual que ck_lineas_variante_medida en Almacén).
            t.HasCheckConstraint("ck_oc_lineas_variante_medida",
                "(producto_aw_variante_id IS NULL AND ancho_mm IS NULL AND alto_mm IS NULL) OR " +
                "(producto_aw_variante_id IS NOT NULL AND ancho_mm IS NOT NULL AND ancho_mm > 0 AND alto_mm IS NOT NULL AND alto_mm > 0)");
        });

        builder.HasKey(l => l.Id);

        // CRÍTICO: Indicar a EF Core que el Id NO se genera por BD; lo
        // asigna el dominio con Guid.CreateVersion7() en
        // OrdenCompra.AgregarLineaManual / AgregarLineaDesdeRq. Sin esta
        // declaración, EF Core ve un Id no-default al insertar y trata
        // la entity como Modified (asume que ya existe en DB), generando
        // un UPDATE con WHERE id=... AND version=0 que afecta 0 rows →
        // DbUpdateConcurrencyException 500. Mismo bug que ya estaba
        // documentado en LineaRequisicionConfiguration.
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.OrdenCompraId).IsRequired();
        builder.Property(l => l.Posicion).IsRequired();
        builder.Property(l => l.ArticuloId).IsRequired();

        // GAP-9: snapshot de naturaleza del artículo — las líneas de
        // servicio se excluyen del sub-estado de Recepción (no se reciben
        // en Almacén). Backfill en la migration LineaOcEsServicio desde
        // compartido.articulos.naturaleza = 1 (Servicio).
        builder.Property(l => l.EsServicio)
            .HasColumnName("es_servicio")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(l => l.DescripcionExtendida).HasColumnType("text");

        builder.Property(l => l.Cantidad)
            .HasColumnType("numeric(15,4)")
            .IsRequired();

        builder.Property(l => l.UnidadMedida)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(l => l.PrecioUnitario)
            .HasColumnType("numeric(15,4)")
            .IsRequired();

        // Descuento (VO value-type, readonly record struct) → ComplexProperty
        // (EF Core 9+ soporta value types como complex types). Mapea a 2
        // columnas inline; ambas requeridas porque el struct siempre tiene
        // valores (default = (Porcentaje, 0)). La línea "sin descuento" usa
        // DescuentoLinea.Cero.
        builder.ComplexProperty(l => l.Descuento, d =>
        {
            d.Property(p => p.Tipo)
                .HasColumnName("descuento_tipo")
                .HasConversion<short>()
                .IsRequired();
            d.Property(p => p.Valor)
                .HasColumnName("descuento_valor")
                .HasColumnType("numeric(15,4)")
                .IsRequired();
        });

        // IndicadorImpuestos (VO) → varchar(40). Convertimos manualmente
        // porque el record struct con un solo campo no es trivial para EF.
        builder.Property(l => l.IndicadorImpuestos)
            .HasConversion(
                v => v.Codigo,
                v => new IndicadorImpuestos(v))
            .HasMaxLength(40)
            .HasColumnName("indicador_impuestos")
            .IsRequired();

        builder.Property(l => l.IvaImporte)
            .HasColumnType("numeric(15,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(l => l.RetencionIsr)
            .HasColumnType("numeric(15,2)");

        builder.Property(l => l.DepartamentoSolicitanteId).IsRequired();

        builder.Property(l => l.RequisicionId);
        builder.Property(l => l.LineaRequisicionId);

        // Fase E PR3: CC-Máquina (Dim3) de la línea. Nullable (opcional en PR3).
        // Columna centro_costo_id por convención snake_case. Sin FK (CentrosCosto
        // es otro esquema/módulo; el vínculo es lógico, ADR-0030/ADR-0050).
        builder.Property(l => l.CentroCostoId);

        builder.Property(l => l.FechaEntregaLinea);

        builder.Property(l => l.CantidadRecibida)
            .HasColumnType("numeric(15,4)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(l => l.CantidadFacturada)
            .HasColumnType("numeric(15,4)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(l => l.TextoAdicional).HasColumnType("text");
        // PLATFORM-TODO(F1-COM-06): supuesto Vilo, confirmar con lista de Millet.
        builder.Property(l => l.FraccionArancelaria).HasMaxLength(10);

        // F1-MP-05 (A4.2): lote del proveedor, opcional.
        builder.Property(l => l.LoteProveedor).HasMaxLength(LineaOrdenCompra.LoteProveedorMaxLength);

        // F1-MP-01 (E1): medida de la hoja de vidrio crudo. Sin FK (A+W vive en otro esquema, ADR-0030).
        builder.Property(l => l.ProductoAwVarianteId);
        builder.Property(l => l.AnchoMm).HasPrecision(12, 3);
        builder.Property(l => l.AltoMm).HasPrecision(12, 3);

        // SubtotalLinea y AreaHojaM2 son computed (getter) — no se persisten como columna.
        builder.Ignore(l => l.SubtotalLinea);
        builder.Ignore(l => l.AreaHojaM2);

        // UNIQUE §10.1: posición única dentro de la OC.
        builder.HasIndex(l => new { l.OrdenCompraId, l.Posicion })
            .IsUnique()
            .HasDatabaseName("uq_oc_lineas_posicion");

        // Índices del §10.1.
        builder.HasIndex(l => l.OrdenCompraId).HasDatabaseName("ix_oc_lineas_oc");
        builder.HasIndex(l => l.ArticuloId).HasDatabaseName("ix_oc_lineas_articulo");
        builder.HasIndex(l => l.RequisicionId)
            .HasDatabaseName("ix_oc_lineas_rq")
            .HasFilter("requisicion_id IS NOT NULL");
    }
}
