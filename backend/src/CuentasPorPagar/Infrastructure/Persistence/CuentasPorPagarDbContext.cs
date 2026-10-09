using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Almacen;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor;
using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.ComprobacionGastos;
using Millet.CuentasPorPagar.Domain.Eventos;
using Millet.CuentasPorPagar.Domain.Evidencias;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.CuentasPorPagar.Domain.Viaticos;
using Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence;

/// <summary>
/// DbContext del módulo Cuentas por Pagar (F0-PR1). Schema:
/// <c>cuentas_por_pagar</c> (ADR-0030 — un esquema por módulo).
///
/// <para>
/// F0-PR1 deja el modelo vacío salvo por la tabla <c>integration_events_outbox</c>
/// (ADR-0009) que el worker
/// <see cref="OutboxPublisherWorker{TDbContext}"/> drena hacia Service
/// Bus. Las entidades de negocio (CfdiRecibido, FacturaProveedor,
/// NotaCreditoProveedor, AnticipoProveedor, NotaCargo, ComprobacionGastos,
/// MovimientoTarjetaCredito, EstadoCuentaTC, EvidenciaAutorizacion) entran
/// en F1+.
/// </para>
///
/// <para>
/// Heredada la convención de Compras/Integraciones.Aw: la outbox se
/// llama <c>integration_events_outbox</c> (no "outbox") y vive dentro
/// del schema del módulo para aislamiento transaccional (§2.1 del
/// 04-cuidados-infra).
/// </para>
/// </summary>
public sealed class CuentasPorPagarDbContext : BaseDbContext
{
    public const string SchemaName = "cuentas_por_pagar";

    public CuentasPorPagarDbContext(
        DbContextOptions<CuentasPorPagarDbContext> options,
        ICurrentEmpresaContext empresaContext,
        Domain.Ports.Contabilidad.IPeriodoContablePort periodos,
        IClock clock, IIntegrationEventPublisher? publisher = null) : base(options, empresaContext)
    { _periodos = new(periodos); _clock = clock; _publisher = publisher; }

    private readonly IIntegrationEventPublisher? _publisher;
    public DbSet<ConfiguracionAnticipoProveedor> ConfiguracionesAnticipoProveedor => Set<ConfiguracionAnticipoProveedor>();

    private readonly Application.Periodos.PeriodoCerradoValidator _periodos;
    private readonly IClock _clock;
    public DbSet<PagoProveedorLocal> PagosProveedorLocal => Set<PagoProveedorLocal>();
    public DbSet<MovimientoPasivo> MovimientosPasivo => Set<MovimientoPasivo>();
    public DbSet<Domain.Catalogos.RetencionConcepto> RetencionesConcepto => Set<Domain.Catalogos.RetencionConcepto>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => SaveChangesAsync(true, cancellationToken);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        var fechas = FechasContables().Distinct().ToArray();
        var nuevosUuid = ChangeTracker.Entries().Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity switch { FacturaProveedor f => f.UuidCfdi, NotaCreditoProveedor n => n.UuidCfdi, AnticipoProveedor a => a.UuidCfdi, _ => null })
            .Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u!.Trim().ToUpperInvariant()).ToArray();
        // P3 libera el CFDI al rechazar por tolerancia: su recaptura como factura es un reintento,
        // no un consumo nuevo entre tipos. Una NC o un anticipo nunca obtiene esta excepción.
        var cfdisRetomados = ChangeTracker.Entries<FacturaProveedor>().Where(e => e.State == EntityState.Added && e.Entity.CfdiRecibidoId != null)
            .Select(e => e.Entity.CfdiRecibidoId!.Value).ToArray();
        // Mismo candado de ADR-0059: cierre y persistencia se serializan hasta commit.
        const long lockPeriodos = 0x0D03_0001;
        await using var tx = Database.IsRelational() && Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(cancellationToken) : null;
        if (Database.IsRelational())
            await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockPeriodos})", cancellationToken);
        if (nuevosUuid.Length > 0)
        {
            if (Database.IsRelational())
                await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({0x503455554944L})", cancellationToken);
            // PostgreSQL traduce ToUpper() a upper(); los UUID solo contienen caracteres ASCII.
#pragma warning disable CA1311, CA1304
            if (nuevosUuid.Distinct().Count() != nuevosUuid.Length ||
                await FacturasProveedor.IgnoreQueryFilters().AnyAsync(f => nuevosUuid.Contains(f.UuidCfdi!.ToUpper()) &&
                    !(f.Estado == EstadoPasivo.Cancelada && f.MotivoDeCancelacion == MotivoCancelacion.RechazadaPorTolerancia && f.CfdiRecibidoId != null && cfdisRetomados.Contains(f.CfdiRecibidoId.Value)), cancellationToken) ||
                await NotasCreditoProveedor.IgnoreQueryFilters().AnyAsync(n => nuevosUuid.Contains(n.UuidCfdi.ToUpper()), cancellationToken) ||
                await AnticiposProveedor.IgnoreQueryFilters().AnyAsync(a => nuevosUuid.Contains(a.UuidCfdi.ToUpper()), cancellationToken))
                throw new Millet.SharedKernel.Application.Exceptions.BusinessRuleException("CXP_UUID_DUPLICADO", "El UUID ya está consumido por una factura, una NC o un anticipo. No puede capturarse otra vez.");
        }
#pragma warning restore CA1311, CA1304
        await _periodos.ValidarAsync(fechas, cancellationToken);
        if (_publisher is not null)
            foreach (var e in ChangeTracker.Entries<FacturaProveedor>().Where(e => e.State == EntityState.Modified &&
                e.Property(f => f.Estado).OriginalValue == EstadoPasivo.Autorizada &&
                e.Entity.Estado is EstadoPasivo.EnRevision or EstadoPasivo.Cancelada))
                await _publisher.PublishAsync(new Application.Integration.PasivoRetiradoDePagoIntegrationEvent(
                    e.Entity.EmpresaId, _clock.UtcNow, e.Entity.Id, e.Entity.ProveedorId, e.Entity.Moneda,
                    e.Entity.Estado == EstadoPasivo.Cancelada ? "Factura cancelada en CxP" : "Factura enviada a revisión en CxP"), cancellationToken);
        foreach (var entry in ChangeTracker.Entries<FacturaProveedor>().Where(e =>
            e.State == EntityState.Added || e.Property(f => f.ConceptoRetencion).IsModified))
        {
            var f = entry.Entity;
            var reglas = await RetencionesConcepto.AsNoTracking().Where(r => r.Activa && r.Concepto == f.ConceptoRetencion).ToListAsync(cancellationToken);
            f.AsignarAlertaRetenciones(Domain.Catalogos.ComparadorRetenciones.Alerta(
                f.ConceptoRetencion, f.Subtotal - f.Descuentos, f.Retenciones, f.RetencionesDetalle, reglas));
        }
        var resultado = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (tx is not null) await tx.CommitAsync(cancellationToken);
        return resultado;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ChangeTracker.DetectChanges();
        if (FechasContables().Any())
            throw new InvalidOperationException("Los movimientos de CxP requieren SaveChangesAsync para verificar el periodo contable.");
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private IEnumerable<DateOnly> FechasContables()
    {
        var hoy = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        foreach (var e in ChangeTracker.Entries<FacturaProveedor>())
        {
            if (e.State == EntityState.Added || e.Property(f => f.FechaContabilizacion).IsModified ||
                e.Property(f => f.Obra).IsModified || e.Property(f => f.ConceptoRetencion).IsModified ||
                (e.Property(f => f.Estado).IsModified && e.Entity.Estado is EstadoPasivo.Autorizada or EstadoPasivo.Cancelada))
            {
                yield return DateOnly.FromDateTime(e.Entity.FechaContabilizacion.UtcDateTime);
                if (e.State != EntityState.Added && e.Property(f => f.FechaContabilizacion).IsModified)
                    yield return DateOnly.FromDateTime(e.Property(f => f.FechaContabilizacion).OriginalValue.UtcDateTime);
            }
        }
        foreach (var e in ChangeTracker.Entries<MovimientoPasivo>().Where(e => e.State == EntityState.Added))
            yield return e.Entity.Fecha;
        foreach (var e in ChangeTracker.Entries<AnticipoProveedor>())
            if (e.State == EntityState.Added) yield return DateOnly.FromDateTime(e.Entity.FechaCfdi.UtcDateTime);
            else if (e.Property(a => a.Estado).IsModified || e.Property(a => a.MontoAmortizado).IsModified) yield return hoy;
        foreach (var e in ChangeTracker.Entries<NotaCreditoProveedor>())
            if (e.State == EntityState.Added) yield return DateOnly.FromDateTime(e.Entity.FechaCfdi.UtcDateTime);
            else if (e.Property(n => n.Estado).IsModified || e.Property(n => n.MontoAplicado).IsModified) yield return hoy;
        foreach (var e in ChangeTracker.Entries<NotaCargo>())
            if (e.State == EntityState.Added) yield return DateOnly.FromDateTime(e.Entity.FechaCreacion.UtcDateTime);
            else if (e.Property(n => n.Estado).IsModified) yield return hoy;
    }

    /// <summary>CFDIs recibidos en el ERP por cualquier canal (§4.1 del 00-levantamiento, F1-PR1).</summary>
    public DbSet<CfdiRecibido> CfdisRecibidos => Set<CfdiRecibido>();

    /// <summary>Facturas de proveedor — agregado central del módulo (§4.2, F3-PR1).</summary>
    public DbSet<FacturaProveedor> FacturasProveedor => Set<FacturaProveedor>();

    /// <summary>Bitácora de transiciones de estado de las facturas (F3-PR1).</summary>
    public DbSet<BitacoraEstadoFactura> BitacoraEstadoFactura => Set<BitacoraEstadoFactura>();

    /// <summary>Catálogo de motivos de revisión (§5.3, F4-PR1).</summary>
    public DbSet<MotivoRevision> MotivosRevision => Set<MotivoRevision>();

    /// <summary>Evidencias polimórficas de autorización (§4.10, F4-PR2).</summary>
    public DbSet<EvidenciaAutorizacion> EvidenciasAutorizacion => Set<EvidenciaAutorizacion>();

    /// <summary>Notas de crédito de proveedor (§4.4, F6-PR1).</summary>
    public DbSet<NotaCreditoProveedor> NotasCreditoProveedor => Set<NotaCreditoProveedor>();

    /// <summary>Marcas de idempotencia para integration events cross-módulo (F5-PR1).</summary>
    public DbSet<EventoProcesado> EventosProcesados => Set<EventoProcesado>();

    /// <summary>Proyección local de OcRecepcionRegistradaEvent (F5-PR1).</summary>
    public DbSet<RecepcionOcLocal> RecepcionesOcLocal => Set<RecepcionOcLocal>();

    /// <summary>Anticipos a proveedores (§4.5, F6-PR2).</summary>
    public DbSet<AnticipoProveedor> AnticiposProveedor => Set<AnticipoProveedor>();

    /// <summary>Notas de cargo internas (§4.6, F6-PR2).</summary>
    public DbSet<NotaCargo> NotasCargo => Set<NotaCargo>();

    /// <summary>Secuencia atómica de folios NCG por (empresa, año) (F6-PR2).</summary>
    public DbSet<FolioSecuenciaNotaCargo> FolioSecuenciasNotaCargo => Set<FolioSecuenciaNotaCargo>();

    /// <summary>Comprobaciones de gastos — agrupador de Caja Chica/Viáticos/TC/Otros (§4.10, F7-PR1).</summary>
    public DbSet<ComprobacionGastos> ComprobacionesGastos => Set<ComprobacionGastos>();

    /// <summary>Reposiciones agregadas de caja chica — pasivo interno hacia Tesorería (doc 12 §D2, GI-PR1).</summary>
    public DbSet<ReposicionCajaChica> ReposicionesCajaChica => Set<ReposicionCajaChica>();

    /// <summary>Monto mínimo de reposición por sucursal (doc 12 §Q4, GI-PR1).</summary>
    public DbSet<ConfiguracionReposicionCaja> ConfiguracionesReposicionCaja => Set<ConfiguracionReposicionCaja>();

    /// <summary>Catálogo de aprobadores con límite por tipo de gasto (§5.5, F7-PR3).</summary>
    public DbSet<AprobadorLimite> AprobadoresLimites => Set<AprobadorLimite>();

    /// <summary>Catálogo de políticas de viáticos por (puesto, destino) (§7.4.2, F7-PR3).</summary>
    public DbSet<PoliticaViaticos> PoliticasViaticos => Set<PoliticaViaticos>();

    /// <summary>Solicitudes de anticipo de viáticos (§7.4.2, F7-PR3).</summary>
    public DbSet<SolicitudViaticos> SolicitudesViaticos => Set<SolicitudViaticos>();

    /// <summary>Master local de tarjetas corporativas (§3 anexo TC, F7-PR4).</summary>
    public DbSet<Tarjeta> TarjetasCredito => Set<Tarjeta>();

    /// <summary>Movimientos individuales de tarjetas corporativas (F7-PR4).</summary>
    public DbSet<MovimientoTarjetaCredito> MovimientosTarjetaCredito => Set<MovimientoTarjetaCredito>();

    /// <summary>Estados de cuenta periódicos de TC (§3, §4 anexo TC, F7-PR5).</summary>
    public DbSet<EstadoCuentaTc> EstadosCuentaTc => Set<EstadoCuentaTc>();

    /// <summary>Líneas crudas del archivo del banco (F7-PR5).</summary>
    public DbSet<LineaBancoTc> LineasBancoTc => Set<LineaBancoTc>();

    /// <summary>Perfiles de parser de bancos — seed AMEX_MX (F7-PR5).</summary>
    public DbSet<PerfilParserBanco> PerfilesParserBanco => Set<PerfilParserBanco>();

    /// <summary>
    /// Outbox de eventos de integración del módulo (ADR-0009). Lo escribe
    /// <c>OutboxSaveChangesInterceptor</c> drenando el buffer scoped
    /// antes de SaveChanges; lo lee
    /// <c>OutboxPublisherWorker&lt;CuentasPorPagarDbContext&gt;</c> para
    /// publicar al topic <c>cuentas-por-pagar-events</c>.
    /// </summary>
    public DbSet<IntegrationEventOutboxEntry> OutboxEntries => Set<IntegrationEventOutboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new IntegrationEventOutboxEntryConfiguration());
        modelBuilder.ApplyConfiguration(new CfdiRecibidoConfiguration());
        modelBuilder.ApplyConfiguration(new FacturaProveedorConfiguration());
        modelBuilder.Entity<PagoProveedorLocal>(b =>
        {
            b.ToTable("pagos_proveedor_local"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Importe).HasPrecision(18, 4); b.Property(x => x.CubiertoRepp).HasPrecision(18, 4);
            b.HasIndex(x => new { x.EmpresaId, x.PagoId }).IsUnique(); b.HasIndex(x => x.FacturaProveedorId);
        });
        modelBuilder.ApplyConfiguration(new RetencionConceptoConfiguration());
        modelBuilder.Entity<MovimientoPasivo>(b =>
        {
            b.ToTable("movimientos_pasivo"); b.HasKey(m => m.Id); b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.Monto).HasPrecision(18, 4); b.HasIndex(m => new { m.FacturaProveedorId, m.Fecha });
        });
        modelBuilder.ApplyConfiguration(new LineaFacturaProveedorConfiguration());
        modelBuilder.ApplyConfiguration(new BitacoraEstadoFacturaConfiguration());
        modelBuilder.ApplyConfiguration(new MotivoRevisionConfiguration());
        modelBuilder.ApplyConfiguration(new EvidenciaAutorizacionConfiguration());
        modelBuilder.ApplyConfiguration(new NotaCreditoProveedorConfiguration());
        modelBuilder.Entity<NotaCreditoProveedor>().Property(n => n.MotivoExcepcionRelacion).HasMaxLength(400);
        modelBuilder.ApplyConfiguration(new EventoProcesadoConfiguration());
        modelBuilder.ApplyConfiguration(new RecepcionOcLocalConfiguration());
        modelBuilder.ApplyConfiguration(new AnticipoProveedorConfiguration());
        modelBuilder.Entity<ConfiguracionAnticipoProveedor>(b =>
        {
            b.ToTable("configuraciones_anticipo_proveedor"); b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Serie).HasMaxLength(25);
            b.HasIndex(x => new { x.EmpresaId, x.ProveedorId }).IsUnique();
        });
        modelBuilder.ApplyConfiguration(new NotaCargoConfiguration());
        modelBuilder.ApplyConfiguration(new FolioSecuenciaNotaCargoConfiguration());
        modelBuilder.ApplyConfiguration(new ComprobacionGastosConfiguration());
        modelBuilder.ApplyConfiguration(new LineaComprobacionGastosConfiguration());
        modelBuilder.ApplyConfiguration(new ReposicionCajaChicaConfiguration());
        modelBuilder.ApplyConfiguration(new ConfiguracionReposicionCajaConfiguration());
        modelBuilder.ApplyConfiguration(new AprobadorLimiteConfiguration());
        modelBuilder.ApplyConfiguration(new PoliticaViaticosConfiguration());
        modelBuilder.ApplyConfiguration(new SolicitudViaticosConfiguration());
        modelBuilder.ApplyConfiguration(new LineaComprobacionViaticosConfiguration());
        modelBuilder.ApplyConfiguration(new TarjetaConfiguration());
        modelBuilder.ApplyConfiguration(new TarjetaUsuarioAutorizadoConfiguration());
        modelBuilder.ApplyConfiguration(new MovimientoTarjetaCreditoConfiguration());
        modelBuilder.ApplyConfiguration(new EstadoCuentaTcConfiguration());
        modelBuilder.ApplyConfiguration(new LineaBancoTcConfiguration());
        modelBuilder.ApplyConfiguration(new PerfilParserBancoConfiguration());
    }
}
