using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Tesoreria.Domain.Corridas;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Eventos;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Domain.Repp;
using Millet.Tesoreria.Infrastructure.Persistence.Configurations;

namespace Millet.Tesoreria.Infrastructure.Persistence;

/// <summary>
/// DbContext del módulo Tesorería / Bancos (TES-PR1). Schema:
/// <c>tesoreria</c> (ADR-0030 — un esquema por módulo).
///
/// <para>
/// TES-PR1 crea el esquema completo del 01-diseño §5 EXCEPTO las tablas
/// de conciliación (<c>conciliacion</c> y <c>extracto_linea</c>, que
/// llegan con PR-9) + la tabla <c>integration_events_outbox</c>
/// (ADR-0009). El <c>OutboxPublisherWorker&lt;TesoreriaDbContext&gt;</c>
/// que la drena hacia el topic <c>tesoreria-events</c> se registra en
/// PR-4 junto con el publisher de los 4 eventos espejo congelados.
/// </para>
/// </summary>
public sealed class TesoreriaDbContext : BaseDbContext
{
    public const string SchemaName = "tesoreria";

    public TesoreriaDbContext(
        DbContextOptions<TesoreriaDbContext> options,
        ICurrentEmpresaContext empresaContext) : base(options, empresaContext) { }

    /// <summary>Cuentas bancarias propias — master data del módulo; CRUD propio [TES-7 revisada], seed script solo bootstrap.</summary>
    public DbSet<CuentaBancaria> CuentasBancarias => Set<CuentaBancaria>();

    /// <summary>Catálogo de conceptos con clasificación de flujo — seed provisional (§5.2).</summary>
    public DbSet<ConceptoMovimiento> ConceptosMovimiento => Set<ConceptoMovimiento>();

    /// <summary>Libro de movimientos bancarios — agregado central (§4.2, comportamiento en PR-2/PR-4).</summary>
    public DbSet<MovimientoBancario> MovimientosBancarios => Set<MovimientoBancario>();

    /// <summary>Aplicaciones movimiento↔pasivo; el Id es el PagoId del evento <c>aplicado.v1</c> (PR-4).</summary>
    public DbSet<AplicacionPagoProveedor> AplicacionesPagoProveedor => Set<AplicacionPagoProveedor>();

    /// <summary>Proyección de pasivos autorizados por CxP — solo mutada por listener y aplicaciones (PR-3).</summary>
    public DbSet<PasivoPendientePago> PasivosPendientesPago => Set<PasivoPendientePago>();

    /// <summary>Corridas de pago con máquina de estados RN-5 (PR-5).</summary>
    public DbSet<CorridaPago> CorridasPago => Set<CorridaPago>();

    /// <summary>Confirmaciones de depósito de cliente — patrón propuesta/confirmación con CxC (PR-7).</summary>
    public DbSet<DepositoConfirmacion> DepositosConfirmacion => Set<DepositoConfirmacion>();

    /// <summary>REPP recibidos de proveedor — registro inmutable (PR-8).</summary>
    public DbSet<ReppProveedorRecibido> ReppsProveedorRecibidos => Set<ReppProveedorRecibido>();

    /// <summary>Marcas de idempotencia para integration events consumidos (PR-3/PR-7).</summary>
    public DbSet<EventoProcesado> EventosProcesados => Set<EventoProcesado>();

    /// <summary>
    /// Outbox de eventos de integración del módulo (ADR-0009). Lo escribe
    /// <c>OutboxSaveChangesInterceptor</c> drenando el buffer scoped antes
    /// de SaveChanges; lo lee
    /// <c>OutboxPublisherWorker&lt;TesoreriaDbContext&gt;</c> (PR-4) para
    /// publicar al topic <c>tesoreria-events</c>.
    /// </summary>
    public DbSet<IntegrationEventOutboxEntry> OutboxEntries => Set<IntegrationEventOutboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new IntegrationEventOutboxEntryConfiguration());
        modelBuilder.ApplyConfiguration(new CuentaBancariaConfiguration());
        modelBuilder.ApplyConfiguration(new ConceptoMovimientoConfiguration());
        modelBuilder.ApplyConfiguration(new MovimientoBancarioConfiguration());
        modelBuilder.ApplyConfiguration(new AplicacionPagoProveedorConfiguration());
        modelBuilder.ApplyConfiguration(new PasivoPendientePagoConfiguration());
        modelBuilder.ApplyConfiguration(new CorridaPagoConfiguration());
        modelBuilder.ApplyConfiguration(new CorridaPagoLineaConfiguration());
        modelBuilder.ApplyConfiguration(new DepositoConfirmacionConfiguration());
        modelBuilder.ApplyConfiguration(new ReppProveedorRecibidoConfiguration());
        modelBuilder.ApplyConfiguration(new EventoProcesadoConfiguration());
    }
}
