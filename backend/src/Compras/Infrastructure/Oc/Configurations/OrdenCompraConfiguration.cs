using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Infrastructure.Oc.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="OrdenCompra"/> (tabla
/// <c>compras.ordenes_compra</c>, diseño §10.1). Aplica:
///
/// <list type="bullet">
///   <item>Tipos numéricos: enums como <c>smallint</c> con CHECK
///         constraint por rango (§10.1).</item>
///   <item>CHECK constraints del §10.1: <c>ck_oc_tipo_cambio</c>,
///         <c>ck_oc_import_campos</c>, <c>ck_oc_sin_rq_motivo</c>.</item>
///   <item>UNIQUE: <c>(empresa_id, sucursal_destino_id, folio)</c> —
///         anti-colisión de folios por sucursal+año (el año está
///         embebido en el folio: <c>OC-MID2026-000001</c>).</item>
///   <item>10 índices del §10.1 (empresa+estado, empresa+proveedor,
///         empresa+comprador, empresa+fecha, referencia proveedor,
///         partidas abiertas, contenedor, ruta, semana, origen-duplicado).</item>
///   <item>VO <see cref="Folio"/> mapeado vía <c>HasConversion</c>.</item>
///   <item>Self-FK <c>oc_origen_id</c> con <c>OnDelete(Restrict)</c>
///         para preservar trazabilidad del comando
///         <c>DuplicarOrdenCompraCommand</c> (C4, F6-PR2).</item>
/// </list>
///
/// <para>
/// **Shadow properties:** columnas del §10.1 que aún no están
/// cableadas al agregado en F1-PR1 (logística, importación, contacto
/// proveedor, descuento global, gastos, redondeo, referencia
/// proveedor, motivos) se declaran aquí como shadow properties para
/// que la migración genere la tabla completa con los CHECKs e índices
/// del diseño. F2-PR2/F2-PR3 promueven estas shadow a propiedades
/// reales del agregado.
/// </para>
///
/// <para>
/// **Soft locks de Capa 2 (ADR-0012):** se cubrirán por el ComprasHub
/// cuando F3-PR3 declare <see cref="OrdenCompra"/> en
/// <c>EntidadesConSoftLock</c>. Capa 1 (<see cref="Version"/> con
/// <c>IsConcurrencyToken</c>) viene de <c>BaseEntity</c>.
/// </para>
/// </summary>
public sealed class OrdenCompraConfiguration : IEntityTypeConfiguration<OrdenCompra>
{
    public void Configure(EntityTypeBuilder<OrdenCompra> builder)
    {
        builder.ToTable("ordenes_compra", t =>
        {
            // CHECK constraints del §10.1.
            t.HasCheckConstraint("ck_oc_tipo_cambio",
                "(moneda = 'MXN' AND tipo_cambio IS NULL) OR (moneda <> 'MXN' AND tipo_cambio > 0)");
            t.HasCheckConstraint("ck_oc_import_campos",
                "(es_importacion = false) OR (es_importacion = true " +
                "AND info_import_incoterm_id IS NOT NULL " +
                "AND info_import_pais_origen IS NOT NULL " +
                "AND info_import_numero_contenedor IS NOT NULL)");
            t.HasCheckConstraint("ck_oc_sin_rq_motivo",
                "sin_requisicion_previa = false OR motivo_sin_requisicion IS NOT NULL");

            // CHECK constraints de rango por convención de enums (espejo
            // de ck_requisiciones_estado / ck_requisiciones_prioridad).
            t.HasCheckConstraint("ck_oc_estado", "estado BETWEEN 0 AND 6");
            t.HasCheckConstraint("ck_oc_sub_recepcion", "sub_estado_recepcion BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_oc_sub_facturacion", "sub_estado_facturacion BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_oc_sub_pago", "sub_estado_pago BETWEEN 0 AND 2");
        });

        builder.HasKey(o => o.Id);

        builder.Property(o => o.EmpresaId).IsRequired();

        // VO Folio → string. El value comparer del record cubre la
        // comparación por valor automáticamente.
        builder.Property(o => o.Folio)
            .HasConversion(
                f => f.Valor,
                v => Folio.Parse(v))
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(o => o.FolioAnio).IsRequired();

        builder.Property(o => o.ProveedorId).IsRequired();
        builder.Property(o => o.SucursalDestinoId).IsRequired();
        builder.Property(o => o.CondicionesPagoId).IsRequired();
        builder.Property(o => o.UsoPrincipalId).IsRequired();

        // Moneda: ISO 4217 3-letter code, default MXN.
        builder.Property(o => o.Moneda)
            .HasColumnType("char(3)")
            .HasDefaultValue("MXN")
            .IsRequired();

        builder.Property(o => o.TipoCambio)
            .HasColumnType("numeric(18,6)");

        builder.Property(o => o.CompradorTitularId).IsRequired();
        builder.Property(o => o.EncargadoComprasId).IsRequired();

        builder.Property(o => o.Observaciones).HasColumnType("text");

        builder.Property(o => o.SinRequisicionPrevia).HasDefaultValue(false).IsRequired();
        builder.Property(o => o.EsImportacion).HasDefaultValue(false).IsRequired();
        builder.Property(o => o.CotizacionExcepcionada).HasDefaultValue(false).IsRequired();

        builder.Property(o => o.FechaDocumento).IsRequired();
        builder.Property(o => o.FechaContabilizacion);
        builder.Property(o => o.FechaEntregaEsperada);

        builder.Property(o => o.Estado)
            .HasConversion<short>()
            .IsRequired();
        builder.Property(o => o.SubEstadoRecepcion)
            .HasConversion<short>()
            .HasDefaultValue(SubEstadoRecepcion.SinRecepcion)
            .IsRequired();
        builder.Property(o => o.SubEstadoFacturacion)
            .HasConversion<short>()
            .HasDefaultValue(SubEstadoFacturacion.SinFactura)
            .IsRequired();
        builder.Property(o => o.SubEstadoPago)
            .HasConversion<short>()
            .HasDefaultValue(SubEstadoPago.SinPago)
            .IsRequired();

        builder.Property(o => o.MotivoSinRequisicion).HasColumnType("text");
        builder.Property(o => o.MotivoCancelacionId);
        builder.Property(o => o.MotivoCancelacion).HasColumnType("text");
        builder.Property(o => o.MotivoRechazoId);
        builder.Property(o => o.MotivoRechazoTexto).HasColumnType("text");

        builder.Property(o => o.OcOrigenId);

        // F2-PR3: propiedades de referencia proveedor, contacto, logística
        // e importación promovidas de shadow properties a propiedades
        // reales del agregado. Los VOs ContactoProveedor?, InformacionLogistica?,
        // InformacionImportacion? son getters computed (no se persisten);
        // las 14 columnas inline mapean a los scalar fields del agregado.
        // EF Core detecta nombres snake_case idénticos — la migración no
        // genera ALTER.
        builder.Property(o => o.ReferenciaProveedor).HasMaxLength(60);
        builder.Property(o => o.ContactoProveedorNombre).HasMaxLength(200);
        builder.Property(o => o.ContactoProveedorEmail).HasMaxLength(200);
        builder.Property(o => o.ContactoProveedorTelefono).HasMaxLength(50);
        builder.Property(o => o.InfoLogisticaDireccion).HasColumnType("text");
        builder.Property(o => o.InfoLogisticaTransportistaId);
        builder.Property(o => o.InfoLogisticaTransportistaTexto).HasMaxLength(200);
        builder.Property(o => o.InfoLogisticaNumeroGuia).HasMaxLength(80);
        builder.Property(o => o.InfoLogisticaInstrucciones).HasColumnType("text");
        builder.Property(o => o.InfoImportIncotermId);
        builder.Property(o => o.InfoImportPaisOrigen).HasColumnType("char(2)");
        builder.Property(o => o.InfoImportNumeroContenedor).HasMaxLength(40);
        builder.Property(o => o.InfoImportCodigoRuta).HasMaxLength(40);
        builder.Property(o => o.InfoImportSemanaEmbarque).HasMaxLength(40);
        builder.Property(o => o.InfoImportNumeroPedimento).HasMaxLength(60);

        // VOs computed (getters) — no se persisten.
        builder.Ignore(o => o.ContactoProveedor);
        builder.Ignore(o => o.InformacionLogistica);
        builder.Ignore(o => o.InformacionImportacion);
        // F2-PR1: DescuentoGlobal, GastosAdicionales y Redondeo promovidos
        // de shadow properties a propiedades reales del agregado (los
        // necesita CalcularTotales). EF Core detecta que las columnas ya
        // existen en la tabla con el mismo nombre snake_case.
        //
        // Para evitar problemas con ComplexProperty + nullable struct
        // (EF Core 9 soporte limitado), mapeamos los 2 campos del VO como
        // properties scalar nullable: NULL ⇔ sin descuento global. El
        // agregado expone un getter `DescuentoGlobal?` que reconstruye la
        // VO desde ambos.
        builder.Property(o => o.DescuentoGlobalTipo)
            .HasConversion<short?>();
        builder.Property(o => o.DescuentoGlobalValor)
            .HasColumnType("numeric(15,4)");
        builder.Ignore(o => o.DescuentoGlobal);

        builder.Property(o => o.GastosAdicionales)
            .HasColumnType("numeric(15,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(o => o.Redondeo)
            .HasColumnType("numeric(15,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        // F5-PR1: sub-estados materializados — pago denormalizado + fecha de
        // cierre automático.
        builder.Property(o => o.MontoPagado)
            .HasColumnType("numeric(15,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(o => o.FechaCierre);

        // F2-PR1: collection Lineas → FK orden_compra_id CASCADE.
        builder.HasMany(o => o.Lineas)
            .WithOne()
            .HasForeignKey(l => l.OrdenCompraId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(OrdenCompra.Lineas))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // F2-PR4: collection Adjuntos → FK orden_compra_id CASCADE.
        builder.HasMany(o => o.Adjuntos)
            .WithOne()
            .HasForeignKey(a => a.OrdenCompraId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(OrdenCompra.Adjuntos))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // F3-PR1: collection Autorizaciones → FK orden_compra_id CASCADE.
        builder.HasMany(o => o.Autorizaciones)
            .WithOne()
            .HasForeignKey(a => a.OrdenCompraId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(OrdenCompra.Autorizaciones))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // UNIQUE de §10.1: anti-colisión de folios por (empresa, sucursal).
        // El año va embebido en el VO Folio (formato OC-MID2026-000001).
        builder.HasIndex(o => new { o.EmpresaId, o.SucursalDestinoId, o.Folio })
            .IsUnique()
            .HasDatabaseName("uq_ordenes_compra_folio");

        // Índices §10.1 (10 en total).
        builder.HasIndex(o => new { o.EmpresaId, o.Estado })
            .HasDatabaseName("ix_oc_empresa_estado");
        builder.HasIndex(o => new { o.EmpresaId, o.ProveedorId })
            .HasDatabaseName("ix_oc_empresa_proveedor");
        builder.HasIndex(o => new { o.EmpresaId, o.CompradorTitularId })
            .HasDatabaseName("ix_oc_empresa_comprador");
        builder.HasIndex(o => new { o.EmpresaId, o.FechaDocumento })
            .HasDatabaseName("ix_oc_empresa_fecha")
            .IsDescending(false, true);

        builder.HasIndex("EmpresaId", "ReferenciaProveedor")
            .HasDatabaseName("ix_oc_referencia_proveedor")
            .HasFilter("referencia_proveedor IS NOT NULL");

        // Partidas abiertas (vista crítica §8.2 del mapa funcional). Solo
        // filas en estado Autorizada (= 3) — el resto de estados no aporta
        // a este reporte y reduce el tamaño del índice considerablemente.
        builder.HasIndex(o => new
            {
                o.EmpresaId,
                o.Estado,
                o.SubEstadoRecepcion,
                o.SubEstadoFacturacion,
                o.SubEstadoPago,
            })
            .HasDatabaseName("ix_oc_partidas_abiertas")
            .HasFilter("estado = 3");

        builder.HasIndex("InfoImportNumeroContenedor")
            .HasDatabaseName("ix_oc_contenedor")
            .HasFilter("info_import_numero_contenedor IS NOT NULL");
        builder.HasIndex("InfoImportCodigoRuta")
            .HasDatabaseName("ix_oc_ruta")
            .HasFilter("info_import_codigo_ruta IS NOT NULL");
        builder.HasIndex("InfoImportSemanaEmbarque")
            .HasDatabaseName("ix_oc_semana")
            .HasFilter("info_import_semana_embarque IS NOT NULL");

        builder.HasIndex(o => o.OcOrigenId)
            .HasDatabaseName("ix_oc_origen")
            .HasFilter("oc_origen_id IS NOT NULL");

        // FK self-referencial para trazabilidad C4 (duplicación). Restrict
        // para evitar borrar la OC origen mientras alguien la referencia.
        builder.HasOne<OrdenCompra>()
            .WithMany()
            .HasForeignKey(o => o.OcOrigenId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_oc_origen");
    }
}
