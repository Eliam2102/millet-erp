using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Cierre;
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

    /// <summary>F6-PR1: devoluciones a proveedor (sub-flujo 8.B).</summary>
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

    /// <summary>
    /// Outbox de eventos de integración del módulo Almacén. Lo escribe
    /// <c>OutboxSaveChangesInterceptor</c> drenando el buffer scoped
    /// antes de SaveChanges; lo lee el worker publisher para enviar a
    /// Service Bus (ADR-0009).
    /// </summary>
    public DbSet<IntegrationEventOutboxEntry> OutboxEntries => Set<IntegrationEventOutboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("almacen");
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
        modelBuilder.ApplyConfiguration(new DevolucionAProveedorConfiguration());
        modelBuilder.ApplyConfiguration(new LineaDevolucionProveedorConfiguration());
        modelBuilder.ApplyConfiguration(new EvidenciaDevolucionProveedorConfiguration());
        modelBuilder.ApplyConfiguration(new ConteoInventarioConfiguration());
        modelBuilder.ApplyConfiguration(new LineaConteoConfiguration());
        modelBuilder.ApplyConfiguration(new RecuentoConteoConfiguration());
        modelBuilder.ApplyConfiguration(new BloqueoInventarioConfiguration());
        modelBuilder.ApplyConfiguration(new PeriodoCerradoConfiguration());
        modelBuilder.ApplyConfiguration(new AlmacenSettingsConfiguration());

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
