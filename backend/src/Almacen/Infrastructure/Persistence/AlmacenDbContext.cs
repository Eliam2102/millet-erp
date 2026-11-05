using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Domain.Consumo;
using Millet.Almacen.Domain.Taller;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence.Configurations;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Almacen.Infrastructure.Persistence;

/// <summary>
/// DbContext del módulo Almacén. Schema: <c>almacen</c> (ADR-0030).
/// Hereda de <see cref="BaseDbContext"/> que aplica los 4 interceptors
/// transversales (Metadata, EmpresaContext, Audit, Outbox) automáticamente.
///
/// <para>
/// F0-PR1 trajo solo la fundación (schema + outbox). F1-PR1 agrega el
/// catálogo de almacenes y sub-almacenes, con copy-from aditivo desde
/// <c>compartido.almacenes</c> (el DROP del placeholder lo hace F1-PR2).
/// Movimientos / saldos / conteos / reservas entran en F2/F3/F7.
/// </para>
///
/// <para>
/// Outbox table: <c>almacen.integration_events_outbox</c> alineada a la
/// convención existente de Compras y CxP. Lo escribe
/// <c>OutboxSaveChangesInterceptor</c> drenando el buffer scoped antes
/// de SaveChanges; lo lee <c>OutboxPublisherWorker&lt;AlmacenDbContext&gt;</c>
/// publicando al topic <c>almacen-events</c>.
/// </para>
/// </summary>
// PR6a: no-sellado para permitir un DbContext de test que sobreescriba
// OnModelCreatingProviderSpecific y le dé ToInMemoryQuery a la vista keyless
// v_movimiento_sub_almacen (inexistente en EF InMemory). Producción usa esta
// clase tal cual; el paquete InMemory no entra a este proyecto.
public class AlmacenDbContext : BaseDbContext
{
    public AlmacenDbContext(
        DbContextOptions<AlmacenDbContext> options,
        ICurrentEmpresaContext empresaContext) : base(options, empresaContext) { }

    /// <summary>
    /// PR6a: ctor con opciones no-genéricas para un DbContext de test derivado
    /// (ver <c>OnModelCreatingProviderSpecific</c>). Producción usa el ctor
    /// tipado de arriba; este permite que el subclass de InMemory pase sus
    /// propias <c>DbContextOptions&lt;InMemoryAlmacenDbContext&gt;</c> sin chocar
    /// con la validación de tipo de EF.
    /// </summary>
    protected AlmacenDbContext(
        DbContextOptions options,
        ICurrentEmpresaContext empresaContext) : base(options, empresaContext) { }

    /// <summary>F1-PR1: catálogo de almacenes físicos del módulo Almacén.</summary>
    public DbSet<Almacen.Domain.Catalogo.Almacen> Almacenes => Set<Almacen.Domain.Catalogo.Almacen>();

    /// <summary>F1-PR1: sub-almacenes dentro de cada <see cref="Almacenes"/>.</summary>
    public DbSet<SubAlmacen> SubAlmacenes => Set<SubAlmacen>();

    /// <summary>PR1 (ADR-0047): ubicaciones Nivel 4 dentro de cada sub-almacén.</summary>
    public DbSet<Ubicacion> Ubicaciones => Set<Ubicacion>();

    /// <summary>PR3 (ADR-0047): asignación artículo→ubicación (OITW) con política de reposición.</summary>
    public DbSet<AsignacionArticuloUbicacion> AsignacionesArticuloUbicacion => Set<AsignacionArticuloUbicacion>();

    /// <summary>PR5.A (ADR-0047, enmienda 2026-07-06): config de reorden por artículo a Nivel 1/2.</summary>
    public DbSet<ConfiguracionReorden> ConfiguracionesReorden => Set<ConfiguracionReorden>();

    /// <summary>Configuración del módulo por empresa (interruptor de reabasto automático). Molde ComprasSettings.</summary>
    public DbSet<AlmacenSettings> AlmacenSettings => Set<AlmacenSettings>();

    /// <summary>F2-PR1: movimientos de inventario (tabla polimórfica con discriminador <c>tipo</c>).</summary>
    public DbSet<MovimientoInventario> Movimientos => Set<MovimientoInventario>();

    // PR6a: vista keyless que deriva el sub-almacén de un movimiento desde la
    // ubicación de su primera línea (una fila por movimiento). Reemplaza la
    // columna de cabecera retirada; la usan los lectores vía join.
    public DbSet<MovimientoSubAlmacen> MovimientosSubAlmacen => Set<MovimientoSubAlmacen>();

    /// <summary>F2-PR1: líneas de cada movimiento (cantidad + costo snapshot).</summary>
    public DbSet<LineaMovimiento> LineasMovimiento => Set<LineaMovimiento>();

    /// <summary>F2-PR1: saldos materializados por (sub_almacen, articulo). Actualizados por trigger PG.</summary>
    public DbSet<SaldoInventario> SaldosInventario => Set<SaldoInventario>();

    /// <summary>F2-PR1: secuencia atómica de folios por (prefijo, año).</summary>
    public DbSet<FolioSecuenciaMovimiento> FolioSecuenciasMovimiento => Set<FolioSecuenciaMovimiento>();

    /// <summary>F3-PR1: dedupe de eventos cross-módulo (A12).</summary>
    public DbSet<EventoProcesado> EventosProcesados => Set<EventoProcesado>();

    /// <summary>ALM-APP-2: mensajes recibidos de la app de Almacenes (idempotencia por mensajeId).</summary>
    public DbSet<MensajeApp> MensajesApp => Set<MensajeApp>();

    /// <summary>A4.8: normas de consumo por artículo componente y consumos por composición leídos de A+W.</summary>
    public DbSet<NormaConsumoComponente> NormasConsumoComponente => Set<NormaConsumoComponente>();
    public DbSet<ConsumoComponenteAw> ConsumosComponenteAw => Set<ConsumoComponenteAw>();

    /// <summary>PT5.10: descargas del taller por pedido interno Serie 9000 y cierres de día del almacenista.</summary>
    public DbSet<DescargaTallerAw> DescargasTallerAw => Set<DescargaTallerAw>();
    public DbSet<CierreTaller> CierresTaller => Set<CierreTaller>();

    /// <summary>A4.4: desperdicio de mesa de corte y merma por etapa informados por A+W.</summary>
    public DbSet<Domain.Mermas.MermaProceso> MermasProceso => Set<Domain.Mermas.MermaProceso>();
    public DbSet<Domain.Roturas.SalidaRotura> SalidasRotura => Set<Domain.Roturas.SalidaRotura>();

    /// <summary>A4.6: corridas de la conciliación diaria contra A+W y sus diferencias (salidas no planificadas).</summary>
    public DbSet<Domain.Conciliacion.ConciliacionDiaria> ConciliacionesDiarias => Set<Domain.Conciliacion.ConciliacionDiaria>();
    public DbSet<Domain.Conciliacion.DiferenciaConciliacion> DiferenciasConciliacion => Set<Domain.Conciliacion.DiferenciaConciliacion>();

    /// <summary>F6-PR1: devoluciones a proveedor (sub-flujo 8.B).</summary>
    public DbSet<Domain.Revision.DictamenRevision> DictamenesRevision => Set<Domain.Revision.DictamenRevision>();
    public DbSet<DevolucionAProveedor> DevolucionesProveedor => Set<DevolucionAProveedor>();
    public DbSet<LineaDevolucionProveedor> LineasDevolucionProveedor => Set<LineaDevolucionProveedor>();
    public DbSet<EvidenciaDevolucionProveedor> EvidenciasDevolucionProveedor => Set<EvidenciaDevolucionProveedor>();

    /// <summary>F7-PR1: conteos de inventario físico.</summary>
    public DbSet<ConteoInventario> Conteos => Set<ConteoInventario>();
    public DbSet<LineaConteo> LineasConteo => Set<LineaConteo>();
    public DbSet<RecuentoConteo> RecuentosConteo => Set<RecuentoConteo>();

    /// <summary>F7-PR3: bloqueos por conteo anual (A18).</summary>
    public DbSet<BloqueoInventario> BloqueosInventario => Set<BloqueoInventario>();

    /// <summary>F8-PR2: periodos cerrados (cierre de mes).</summary>
    public DbSet<PeriodoCerrado> PeriodosCerrados => Set<PeriodoCerrado>();

    /// <summary>F1-ALM-13: foto de la valuación al cierre mensual.</summary>
    public DbSet<ValuacionCierre> ValuacionesCierre => Set<ValuacionCierre>();

    /// <summary>
    /// Outbox de eventos de integración del módulo Almacén. Lo escribe
    /// <c>OutboxSaveChangesInterceptor</c> drenando el buffer scoped
    /// antes de SaveChanges; lo lee el worker publisher para enviar a
    /// Service Bus (ADR-0009).
    /// </summary>
    public DbSet<IntegrationEventOutboxEntry> OutboxEntries => Set<IntegrationEventOutboxEntry>();

    public DbSet<Domain.Apartados.ApartadoRequisicion> ApartadosRequisicion => Set<Domain.Apartados.ApartadoRequisicion>();

    public DbSet<RecepcionImportacion> RecepcionesImportacion => Set<RecepcionImportacion>();

    public DbSet<RecepcionActivo> RecepcionesActivos => Set<RecepcionActivo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("almacen");
        var importacion = modelBuilder.Entity<RecepcionImportacion>();
        importacion.ToTable("recepciones_importacion"); importacion.HasKey(x => x.Id);
        importacion.Property(x => x.Id).ValueGeneratedNever();
        importacion.Property(x => x.NumeroPedimento).HasMaxLength(15);
        importacion.Property(x => x.NumeroBl).HasMaxLength(50);
        importacion.Property(x => x.NumeroContenedor).HasMaxLength(40);
        importacion.Property(x => x.Aduana).HasMaxLength(2);
        importacion.Property(x => x.ClavePedimento).HasMaxLength(2);
        importacion.HasIndex(x => new { x.EmpresaId, x.NumeroPedimento });
        importacion.HasIndex(x => new { x.EmpresaId, x.OrdenCompraId });
        importacion.HasIndex(x => x.RecepcionId).IsUnique();
        importacion.HasMany(x => x.Partidas).WithOne().HasForeignKey("RecepcionImportacionId");
        var partidaImportacion = modelBuilder.Entity<RecepcionImportacionPartida>();
        partidaImportacion.ToTable("recepcion_importacion_partidas"); partidaImportacion.HasKey(x => x.Id);
        partidaImportacion.Property(x => x.Id).ValueGeneratedNever();
        partidaImportacion.Property(x => x.FraccionArancelaria).HasMaxLength(10);
        var af = modelBuilder.Entity<RecepcionActivo>();
        af.ToTable("recepciones_activos", "almacen"); af.HasKey(x => x.Id); af.Property(x => x.Id).ValueGeneratedNever();
        af.Property(x => x.Cantidad).HasPrecision(18, 4); af.HasIndex(x => new { x.EmpresaId, x.OrdenCompraId, x.LineaOcId });
        af.Property(x => x.Folio).HasMaxLength(30); af.Property(x => x.MotivoAnulacion).HasMaxLength(500); af.Ignore(x => x.EstaAnulada);
        var ajuste = modelBuilder.Entity<AjusteCostoExpediente>();
        ajuste.ToTable("ajustes_costo_expediente"); ajuste.HasKey(x => x.Id); ajuste.Property(x => x.Id).ValueGeneratedNever();
        ajuste.HasIndex(x => new { x.EmpresaId, x.ExpedienteId, x.NumeroVersion, x.PartidaId, x.UbicacionId }).IsUnique();
        ajuste.Property(x => x.AcumuladoMxn).HasPrecision(20, 2); ajuste.Property(x => x.DiferenciaMxn).HasPrecision(20, 2);
        ajuste.Property(x => x.InventarioMxn).HasPrecision(20, 2); ajuste.Property(x => x.CostoVentasMxn).HasPrecision(20, 2);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new IntegrationEventOutboxEntryConfiguration());
        modelBuilder.ApplyConfiguration(new AlmacenConfiguration());
        modelBuilder.ApplyConfiguration(new SubAlmacenConfiguration());
        modelBuilder.ApplyConfiguration(new UbicacionConfiguration());
        modelBuilder.ApplyConfiguration(new AsignacionArticuloUbicacionConfiguration());
        modelBuilder.ApplyConfiguration(new ConfiguracionReordenConfiguration());
        modelBuilder.ApplyConfiguration(new MovimientoInventarioConfiguration());
        modelBuilder.ApplyConfiguration(new MovimientoSubAlmacenConfiguration());
        modelBuilder.ApplyConfiguration(new LineaMovimientoConfiguration());
        modelBuilder.ApplyConfiguration(new SaldoInventarioConfiguration());
        modelBuilder.ApplyConfiguration(new FolioSecuenciaMovimientoConfiguration());
        modelBuilder.ApplyConfiguration(new EventoProcesadoConfiguration());
        modelBuilder.ApplyConfiguration(new MensajeAppConfiguration());
        modelBuilder.ApplyConfiguration(new DevolucionAProveedorConfiguration());
        modelBuilder.ApplyConfiguration(new LineaDevolucionProveedorConfiguration());
        modelBuilder.ApplyConfiguration(new EvidenciaDevolucionProveedorConfiguration());
        modelBuilder.ApplyConfiguration(new DictamenRevisionConfiguration());
        modelBuilder.ApplyConfiguration(new ConteoInventarioConfiguration());
        modelBuilder.ApplyConfiguration(new LineaConteoConfiguration());
        modelBuilder.ApplyConfiguration(new RecuentoConteoConfiguration());
        modelBuilder.ApplyConfiguration(new BloqueoInventarioConfiguration());
        modelBuilder.ApplyConfiguration(new PeriodoCerradoConfiguration());
        modelBuilder.ApplyConfiguration(new AlmacenSettingsConfiguration());

        modelBuilder.Entity<Domain.Apartados.ApartadoRequisicion>(b =>
        {
            b.ToTable("apartados_requisicion", t => t.HasCheckConstraint("ck_apartado_pendiente", "pendiente >= 0"));
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedNever();
            b.Property(a => a.Pendiente).HasPrecision(14, 4);
            b.Property(a => a.CantidadLiberada).HasPrecision(14, 4).HasDefaultValue(0m);
            b.Property(a => a.MotivoLiberacion).HasMaxLength(500);
            b.HasIndex(a => a.LineaRequisicionId).IsUnique();
            b.HasIndex(a => new { a.SucursalId, a.ArticuloId });
        });
        modelBuilder.Entity<ValuacionCierre>(b =>
        {
            b.ToTable("valuaciones_cierre");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Lote).HasMaxLength(50);
            b.Property(x => x.Cantidad).HasPrecision(18, 4);
            b.Property(x => x.ValorMxn).HasPrecision(18, 2);
            b.HasOne<PeriodoCerrado>().WithMany().HasForeignKey(x => x.PeriodoCerradoId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.EmpresaId, x.Anio, x.Mes, x.SubAlmacenId });
        });
        modelBuilder.Entity<NormaConsumoComponente>(b =>
        {
            b.ToTable("normas_consumo_componente");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ConsumoPorUnidad).HasPrecision(14, 6);
            b.Property(x => x.PorcentajeMerma).HasPrecision(7, 4);
            b.Property(x => x.FactorM3PorM2).HasPrecision(14, 6);
            b.HasIndex(x => new { x.EmpresaId, x.ArticuloId }).IsUnique();
        });
        modelBuilder.Entity<ConsumoComponenteAw>(b =>
        {
            b.ToTable("consumos_componente_aw");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Origen).HasMaxLength(10).IsRequired();
            b.Property(x => x.Etiqueta).HasMaxLength(50).IsRequired();
            b.Property(x => x.ComponenteRef).HasMaxLength(50).IsRequired();
            b.Property(x => x.Auftnr).HasMaxLength(50).IsRequired();
            b.Property(x => x.AnchoMm).HasPrecision(14, 4);
            b.Property(x => x.AltoMm).HasPrecision(14, 4);
            b.Property(x => x.ClaveSucursal).HasMaxLength(50);
            b.Property(x => x.BearbZeit).HasColumnType("timestamp without time zone");
            b.Property(x => x.Estado).HasMaxLength(20).IsRequired();
            b.Property(x => x.Cantidad).HasPrecision(14, 4);
            b.Property(x => x.Folio).HasMaxLength(100);
            b.Property(x => x.Error).HasMaxLength(1000);
            // D2: un componente de una pieza en una operación se descuenta una sola vez.
            b.HasIndex(x => new { x.EmpresaId, x.Origen, x.Etiqueta, x.Operacion, x.ComponenteRef }).IsUnique();
            b.HasIndex(x => new { x.EmpresaId, x.Estado, x.IntentadoAt });
        });
        modelBuilder.Entity<DescargaTallerAw>(b =>
        {
            b.ToTable("descargas_taller_aw");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Origen).HasMaxLength(10).IsRequired();
            b.Property(x => x.NumeroPedido).HasMaxLength(50).IsRequired();
            b.Property(x => x.ArticuloRef).HasMaxLength(50).IsRequired();
            b.Property(x => x.Cantidad).HasPrecision(14, 4);
            b.Property(x => x.AnchoMm).HasPrecision(14, 4);
            b.Property(x => x.AltoMm).HasPrecision(14, 4);
            b.Property(x => x.ClaveSucursal).HasMaxLength(50);
            b.Property(x => x.M2).HasPrecision(14, 4);
            b.Property(x => x.Estado).HasMaxLength(20).IsRequired();
            b.Property(x => x.Folio).HasMaxLength(100);
            b.Property(x => x.Error).HasMaxLength(1000);
            // Una posición de un pedido Serie 9000 baja el inventario una sola vez.
            b.HasIndex(x => new { x.EmpresaId, x.Origen, x.NumeroPedido, x.Posicion }).IsUnique();
            b.HasIndex(x => new { x.EmpresaId, x.Estado, x.IntentadoAt });
            b.HasIndex(x => new { x.EmpresaId, x.SucursalId, x.FechaPedido });
        });
        modelBuilder.Entity<CierreTaller>(b =>
        {
            b.ToTable("cierres_taller");
            b.Property(x => x.HojasTomadas).HasPrecision(14, 4);
            b.Property(x => x.RetazosTomados).HasPrecision(14, 4);
            b.Property(x => x.M2Descargados).HasPrecision(14, 4);
            b.Property(x => x.M2Cortados).HasPrecision(14, 4);
            b.Property(x => x.FactorOptimizacion).HasPrecision(14, 4);
            b.Property(x => x.Notas).HasMaxLength(500);
            // Un cierre por taller y día.
            b.HasIndex(x => new { x.EmpresaId, x.SucursalId, x.Fecha }).IsUnique();
        });
        modelBuilder.Entity<Domain.Mermas.MermaProceso>(b =>
        {
            b.ToTable("mermas_proceso", t =>
            {
                t.HasCheckConstraint("ck_mermas_proceso_m2", "m2 > 0");
                t.HasCheckConstraint("ck_mermas_proceso_kg", "kg IS NULL OR kg > 0");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Tipo).HasMaxLength(20).IsRequired();
            b.Property(x => x.Mesa).HasMaxLength(30);
            b.Property(x => x.Etapa).HasMaxLength(20).IsRequired();
            b.Property(x => x.Motivo).HasMaxLength(200).IsRequired();
            b.Property(x => x.Kg).HasPrecision(14, 4);
            b.Property(x => x.M2).HasPrecision(14, 4);
            b.Property(x => x.CostoUnitarioM2Mxn).HasPrecision(14, 4);
            b.Property(x => x.ValorMxn).HasPrecision(18, 2);
            b.Property(x => x.Estado).HasMaxLength(20).IsRequired();
            b.Property(x => x.MensajeId).HasMaxLength(64);
            b.Property(x => x.MotivoRechazo).HasMaxLength(500);
            // A4.4-a: un desperdicio por (sucursal, día, mesa, artículo); la merma no tiene esa llave.
            b.HasIndex(x => new { x.EmpresaId, x.SucursalId, x.Fecha, x.Mesa, x.ArticuloId }).IsUnique()
                .HasFilter("tipo = 'DESPERDICIO_MESA'");
            b.HasIndex(x => new { x.EmpresaId, x.Estado, x.Fecha });
        });
        modelBuilder.Entity<Domain.Roturas.SalidaRotura>(b =>
        {
            b.ToTable("salidas_rotura", t => t.HasCheckConstraint("ck_salidas_rotura_cantidad", "cantidad > 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Cantidad).HasPrecision(14, 4);
            b.Property(x => x.UnidadMedida).HasMaxLength(20).IsRequired();
            b.Property(x => x.MotivoCodigo).HasMaxLength(30).IsRequired();
            b.Property(x => x.Comentario).HasMaxLength(500);
            b.Property(x => x.Estado).HasMaxLength(20).IsRequired();
            b.Property(x => x.MotivoRechazo).HasMaxLength(500);
            b.Property(x => x.FolioMovimiento).HasMaxLength(30);
            b.Property(x => x.CostoUnitarioMxn).HasPrecision(14, 4);
            b.Property(x => x.ValorMxn).HasPrecision(18, 2);
            b.HasIndex(x => new { x.EmpresaId, x.Estado, x.Fecha });
        });
        modelBuilder.Entity<Domain.Conciliacion.ConciliacionDiaria>(b =>
        {
            b.ToTable("conciliaciones_diarias");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Fuente).HasMaxLength(10).IsRequired();
            b.HasIndex(x => new { x.EmpresaId, x.AlmacenId, x.Fecha });
        });
        modelBuilder.Entity<Domain.Conciliacion.DiferenciaConciliacion>(b =>
        {
            b.ToTable("diferencias_conciliacion", t =>
                t.HasCheckConstraint("ck_diferencias_conciliacion_distintas", "teorico <> fisico"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Ignore(x => x.Diferencia);
            b.Property(x => x.Lote).HasMaxLength(50);
            b.Property(x => x.AreaHojaM2).HasPrecision(14, 6);
            b.Property(x => x.Teorico).HasPrecision(14, 4);
            b.Property(x => x.Fisico).HasPrecision(14, 4);
            b.Property(x => x.Sentido).HasMaxLength(10).IsRequired();
            b.Property(x => x.CostoUnitarioMxn).HasPrecision(14, 4);
            b.Property(x => x.ValorMxn).HasPrecision(18, 2);
            b.Property(x => x.FolioMovimiento).HasMaxLength(200);
            b.Property(x => x.Estado).HasMaxLength(10).IsRequired();
            b.Property(x => x.MotivoAclaracion).HasMaxLength(500);
            b.HasIndex(x => new { x.EmpresaId, x.Estado, x.Fecha });
            b.HasIndex(x => x.ConciliacionId);
        });
        OnModelCreatingProviderSpecific(modelBuilder);
    }

    /// <summary>
    /// PR6a: hook para mapeo específico de proveedor. En producción (Npgsql) la
    /// vista keyless <c>v_movimiento_sub_almacen</c> ya está mapeada con
    /// <c>ToView</c> — no hay nada que hacer. Los tests que corren sobre EF
    /// InMemory lo sobreescriben para darle a esa entidad una
    /// <c>ToInMemoryQuery</c> (la misma derivación en LINQ), porque una vista no
    /// existe en InMemory y el paquete InMemory es test-only. Así el paquete de
    /// test no entra a producción.
    /// </summary>
    protected virtual void OnModelCreatingProviderSpecific(ModelBuilder modelBuilder)
    {
    }
}
