using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Alertas;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Cobranza;
using Millet.CuentasPorCobrar.Domain.Eventos;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Infrastructure.Persistence.Configurations;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence;

/// <summary>
/// DbContext del módulo Cuentas por Cobrar (CXC-PR1). Schema:
/// <c>cuentas_por_cobrar</c> (ADR-0030 — un esquema por módulo).
///
/// <para>
/// CXC-PR1 arranca con <c>LineaCredito</c> + la tabla
/// <c>integration_events_outbox</c> (ADR-0009) que el worker
/// <see cref="OutboxPublisherWorker{TDbContext}"/> drena hacia Service
/// Bus. Los demás agregados (DecisionLiberacion, AutorizacionCredito,
/// PropuestaAplicacionPago, FacturaCartera, SeguimientoCobranza,
/// AlertaCartera) entran en CXC-PR3+.
/// </para>
///
/// <para>
/// Heredada la convención de la triada: la outbox se llama
/// <c>integration_events_outbox</c> (no "outbox") y vive dentro del
/// schema del módulo para aislamiento transaccional.
/// </para>
/// </summary>
public sealed class CuentasPorCobrarDbContext : BaseDbContext
{
    public const string SchemaName = "cuentas_por_cobrar";

    public CuentasPorCobrarDbContext(
        DbContextOptions<CuentasPorCobrarDbContext> options,
        ICurrentEmpresaContext empresaContext) : base(options, empresaContext) { }

    /// <summary>Líneas de crédito por cliente — master data del módulo (§3.1, CXC-PR1).</summary>
    public DbSet<LineaCredito> LineasCredito => Set<LineaCredito>();

    /// <summary>Proyección local de comprobantes cobrables — solo mutada por eventos (§3.1, CXC-PR3).</summary>
    public DbSet<FacturaCartera> FacturasCartera => Set<FacturaCartera>();

    /// <summary>Movimientos de pago/NC por factura, correlacionados al comprobante origen (CXC-PR3).</summary>
    public DbSet<MovimientoCartera> MovimientosCartera => Set<MovimientoCartera>();

    /// <summary>Marcas de idempotencia para integration events consumidos (CXC-PR3).</summary>
    public DbSet<EventoProcesado> EventosProcesados => Set<EventoProcesado>();

    /// <summary>Decisiones de liberación de pedidos — inmutables post-emisión (§3.1, CXC-PR4).</summary>
    public DbSet<DecisionLiberacion> DecisionesLiberacion => Set<DecisionLiberacion>();

    /// <summary>Catálogo de reglas por serie de folio A+W — seed provisional (§3.1, CXC-PR4).</summary>
    public DbSet<ReglaLiberacionSerie> ReglasLiberacionSerie => Set<ReglaLiberacionSerie>();

    /// <summary>Overrides de crédito consumibles — calco AutorizacionAperturaCaja (§3.1, CXC-PR4).</summary>
    public DbSet<AutorizacionCredito> AutorizacionesCredito => Set<AutorizacionCredito>();

    /// <summary>Gestiones de cobranza — historial auditable append-only (§3.1, CXC-PR5).</summary>
    public DbSet<SeguimientoCobranza> SeguimientosCobranza => Set<SeguimientoCobranza>();

    /// <summary>Propuestas de aplicación de pago — matching depósito↔facturas (§3.1, CXC-PR7).</summary>
    public DbSet<PropuestaAplicacionPago> PropuestasAplicacionPago => Set<PropuestaAplicacionPago>();

    /// <summary>Alertas de cartera generadas por el worker diario (§3.1, CXC-PR8).</summary>
    public DbSet<AlertaCartera> AlertasCartera => Set<AlertaCartera>();

    /// <summary>
    /// Outbox de eventos de integración del módulo (ADR-0009). Lo escribe
    /// <c>OutboxSaveChangesInterceptor</c> drenando el buffer scoped
    /// antes de SaveChanges; lo lee
    /// <c>OutboxPublisherWorker&lt;CuentasPorCobrarDbContext&gt;</c> para
    /// publicar al topic <c>cuentas-por-cobrar-events</c>.
    /// </summary>
    public DbSet<IntegrationEventOutboxEntry> OutboxEntries => Set<IntegrationEventOutboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new IntegrationEventOutboxEntryConfiguration());
        modelBuilder.ApplyConfiguration(new LineaCreditoConfiguration());
        modelBuilder.ApplyConfiguration(new FacturaCarteraConfiguration());
        modelBuilder.ApplyConfiguration(new MovimientoCarteraConfiguration());
        modelBuilder.ApplyConfiguration(new EventoProcesadoConfiguration());
        modelBuilder.ApplyConfiguration(new DecisionLiberacionConfiguration());
        modelBuilder.ApplyConfiguration(new ReglaLiberacionSerieConfiguration());
        modelBuilder.ApplyConfiguration(new AutorizacionCreditoConfiguration());
        modelBuilder.ApplyConfiguration(new SeguimientoCobranzaConfiguration());
        modelBuilder.ApplyConfiguration(new PropuestaAplicacionPagoConfiguration());
        modelBuilder.ApplyConfiguration(new PropuestaAplicacionFacturaConfiguration());
        modelBuilder.ApplyConfiguration(new AlertaCarteraConfiguration());
    }
}
