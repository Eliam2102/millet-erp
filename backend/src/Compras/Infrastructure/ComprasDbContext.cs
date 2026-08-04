using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Idempotencia;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure.Configurations;
using Millet.Compras.Infrastructure.Oc.Configurations;
using Millet.Compras.Infrastructure.Stubs;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Compras.Infrastructure;

/// <summary>
/// DbContext del módulo Compras (no-producción). Schema: <c>compras</c>.
/// Tablas vivas: <c>requisiciones</c> (F1-PR1), <c>folio_secuencias</c>
/// (F1-PR1). Las entidades hijas (<c>requisicion_lineas</c>,
/// <c>requisicion_autorizaciones</c>) entran en F2.
/// Ver ADR-0005 (esquema por módulo) y ADR-0011 (multi-empresa).
/// </summary>
public sealed class ComprasDbContext : BaseDbContext
{
    public ComprasDbContext(
        DbContextOptions<ComprasDbContext> options,
        ICurrentEmpresaContext empresaContext) : base(options, empresaContext) { }

    public DbSet<Requisicion> Requisiciones => Set<Requisicion>();
    public DbSet<LineaRequisicion> LineaRequisiciones => Set<LineaRequisicion>();
    public DbSet<Autorizacion> Autorizaciones => Set<Autorizacion>();
    public DbSet<MotivoRechazo> MotivosRechazo => Set<MotivoRechazo>();
    public DbSet<UmbralAprobacionDepartamento> UmbralesAprobacionDepartamento => Set<UmbralAprobacionDepartamento>();
    public DbSet<AprobadorDepartamento> AprobadoresDepartamento => Set<AprobadorDepartamento>();
    public DbSet<FolioSecuencia> FolioSecuencias => Set<FolioSecuencia>();
    public DbSet<ComprasSettings> ComprasSettings => Set<ComprasSettings>();

    // --- Submódulo Órdenes de Compra (F1-PR1, F2-PR1, F2-PR4, F3-PR1, F3-PR2) ---
    public DbSet<OrdenCompra> OrdenesCompra => Set<OrdenCompra>();
    public DbSet<FolioSecuenciaOc> FolioSecuenciasOc => Set<FolioSecuenciaOc>();
    public DbSet<LineaOrdenCompra> LineasOrdenCompra => Set<LineaOrdenCompra>();
    public DbSet<TipoDocumentoOc> TiposDocumentoOc => Set<TipoDocumentoOc>();
    public DbSet<AdjuntoOC> AdjuntosOc => Set<AdjuntoOC>();
    public DbSet<AutorizacionOC> AutorizacionesOc => Set<AutorizacionOC>();
    public DbSet<RegimenFiscalArticulo> RegimenesFiscalesArticulo => Set<RegimenFiscalArticulo>();
    public DbSet<OrdenCompraPdf> OrdenCompraPdfs => Set<OrdenCompraPdf>();

    /// <summary>
    /// Tabla provisional usada por <c>InMemoryGenerarSolicitudCompraPort</c>
    /// (F3-PR2). PLATFORM-TODO(<![CDATA[<StubsTeardown>]]>): se elimina
    /// junto con los stubs cuando exista OC real.
    /// </summary>
    public DbSet<OcBorradorStub> OcBorradorStubs => Set<OcBorradorStub>();

    /// <summary>
    /// Outbox de eventos de integración del módulo Compras (F6-PR1,
    /// ADR-0009). Lo escribe <c>OutboxSaveChangesInterceptor</c>
    /// drenando el buffer scoped antes de SaveChanges; lo lee el
    /// worker publisher (F6-PR2) para enviar a Service Bus.
    /// </summary>
    public DbSet<IntegrationEventOutboxEntry> OutboxEntries => Set<IntegrationEventOutboxEntry>();

    /// <summary>
    /// Marcas de idempotencia para listeners cross-módulo (CxP → Compras
    /// vía <c>CxpEventListenerWorker</c>). Mismo patrón que
    /// <c>almacen.eventos_procesados</c> / <c>cuentas_por_pagar.eventos_procesados</c>.
    /// </summary>
    public DbSet<EventoProcesado> EventosProcesados => Set<EventoProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("compras");
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new RequisicionConfiguration());
        modelBuilder.ApplyConfiguration(new LineaRequisicionConfiguration());
        modelBuilder.ApplyConfiguration(new AutorizacionConfiguration());
        modelBuilder.ApplyConfiguration(new MotivoRechazoConfiguration());
        modelBuilder.ApplyConfiguration(new UmbralAprobacionDepartamentoConfiguration());
        modelBuilder.ApplyConfiguration(new AprobadorDepartamentoConfiguration());
        modelBuilder.ApplyConfiguration(new FolioSecuenciaConfiguration());
        modelBuilder.ApplyConfiguration(new ComprasSettingsConfiguration());
        modelBuilder.ApplyConfiguration(new OcBorradorStubConfiguration());
        modelBuilder.ApplyConfiguration(new IntegrationEventOutboxEntryConfiguration());
        modelBuilder.ApplyConfiguration(new EventoProcesadoConfiguration());

        // Submódulo Órdenes de Compra (F1-PR1, F2-PR1, F2-PR4, F3-PR1, F3-PR2).
        modelBuilder.ApplyConfiguration(new OrdenCompraConfiguration());
        modelBuilder.ApplyConfiguration(new FolioSecuenciaOcConfiguration());
        modelBuilder.ApplyConfiguration(new LineaOrdenCompraConfiguration());
        modelBuilder.ApplyConfiguration(new TipoDocumentoOcConfiguration());
        modelBuilder.ApplyConfiguration(new AdjuntoOcConfiguration());
        modelBuilder.ApplyConfiguration(new AutorizacionOcConfiguration());
        modelBuilder.ApplyConfiguration(new RegimenFiscalArticuloConfiguration());
        modelBuilder.ApplyConfiguration(new OrdenCompraPdfConfiguration());
    }
}
