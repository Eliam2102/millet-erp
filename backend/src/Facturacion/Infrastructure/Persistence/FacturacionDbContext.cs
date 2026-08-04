using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Activos;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.CartaPorte;
using Millet.Facturacion.Domain.Cce;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Envios;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence.Configurations;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Facturacion.Infrastructure.Persistence;

/// <summary>
/// DbContext del módulo Facturación (F0-PR1). Schema: <c>facturacion</c>
/// (ADR-0030 — un esquema por módulo, dueño único). Hereda de
/// <see cref="BaseDbContext"/> que aplica los interceptors transversales y los
/// global query filters (soft-delete + multi-tenancy).
///
/// <para>
/// F0-PR1 deja el modelo vacío salvo por la tabla
/// <c>integration_events_outbox</c> (ADR-0009) que el worker
/// <see cref="OutboxPublisherWorker{TDbContext}"/> drena hacia el topic
/// <c>facturacion-events</c>. Los agregados de negocio (Comprobante TPT,
/// FacturaVenta, FacturaAnticipo, NotaCredito, CartaPorte, ReciboPago,
/// Anticipo, PedidoFacturable, SolicitudCancelacion) entran en F1+.
/// </para>
///
/// <para>
/// Convención heredada de Compras/CxP/Almacén: la outbox se llama
/// <c>integration_events_outbox</c> y vive dentro del schema del módulo para
/// aislamiento transaccional (§2 del 04-cuidados-infra).
/// </para>
///
/// <para>
/// <b>Checklist DbContext nuevo</b> (memoria <c>feedback_dbcontext_nuevo_checklist</c>):
/// este contexto debe estar en <c>Program.cs</c>
/// (<c>AddDbContext</c> + <c>MigrationsHealthCheckOptions.ContextTypes</c>) Y en
/// el bucle de migraciones de <c>deploy-app-dev.yml</c> — ambos en F0-PR1.
/// </para>
/// </summary>
public sealed class FacturacionDbContext : BaseDbContext
{
    public const string SchemaName = "facturacion";

    public FacturacionDbContext(
        DbContextOptions<FacturacionDbContext> options,
        ICurrentEmpresaContext empresaContext) : base(options, empresaContext) { }

    /// <summary>
    /// Outbox de eventos de integración del módulo Facturación. Lo escribe
    /// <c>OutboxSaveChangesInterceptor</c> drenando el buffer scoped antes de
    /// SaveChanges; lo lee <c>OutboxPublisherWorker&lt;FacturacionDbContext&gt;</c>
    /// para publicar al topic <c>facturacion-events</c> (ADR-0009).
    /// </summary>
    public DbSet<IntegrationEventOutboxEntry> OutboxEntries => Set<IntegrationEventOutboxEntry>();

    /// <summary>
    /// Comprobantes (raíz TPT). Permite consultas polimórficas sobre todos los
    /// CFDIs emitidos; los subtipos concretos se exponen aparte (F1-PR1:
    /// <see cref="FacturasVenta"/>).
    /// </summary>
    public DbSet<Comprobante> Comprobantes => Set<Comprobante>();

    /// <summary>Facturas de venta (subtipo Ingreso, F1-PR1).</summary>
    public DbSet<FacturaVenta> FacturasVenta => Set<FacturaVenta>();

    /// <summary>Facturas de anticipo (subtipo Ingreso, serie FANT, F4-PR1).</summary>
    public DbSet<FacturaAnticipo> FacturasAnticipo => Set<FacturaAnticipo>();

    /// <summary>Saldos amortizables de anticipos — agregado aparte del CFDI (F4-PR1).</summary>
    public DbSet<Anticipo> Anticipos => Set<Anticipo>();

    /// <summary>Notas de crédito (subtipo Egreso, F4-PR2: amortización; F5: bonificación/devolución).</summary>
    public DbSet<NotaCredito> NotasCredito => Set<NotaCredito>();

    /// <summary>Solicitudes de cancelación SAT 4.0 de comprobantes (F5-PR2).</summary>
    public DbSet<SolicitudCancelacion> SolicitudesCancelacion => Set<SolicitudCancelacion>();

    /// <summary>Recibos electrónicos de pago — REPP, subtipo Pago (F6).</summary>
    public DbSet<ReciboPago> RecibosPago => Set<ReciboPago>();

    /// <summary>Cartas Porte 3.1 — subtipo Traslado/Ingreso (F8).</summary>
    public DbSet<CartaPorte> CartasPorte => Set<CartaPorte>();

    /// <summary>Catálogo de vehículos para Carta Porte (F8).</summary>
    public DbSet<Vehiculo> Vehiculos => Set<Vehiculo>();

    /// <summary>Catálogo de operadores para Carta Porte (F8).</summary>
    public DbSet<Operador> Operadores => Set<Operador>();

    /// <summary>Autorizaciones de venta de activos fijos del Contador General (F9).</summary>
    public DbSet<AutorizacionVentaActivo> AutorizacionesVentaActivo => Set<AutorizacionVentaActivo>();

    /// <summary>Pedidos facturables (origen Aw/PlantaPintura/Manual, F1-PR2).</summary>
    public DbSet<PedidoFacturable> PedidosFacturables => Set<PedidoFacturable>();

    /// <summary>Bitácora de envíos de CFDI al correo del cliente (F2-PR2).</summary>
    public DbSet<BitacoraEnvioCorreo> BitacorasEnvioCorreo => Set<BitacoraEnvioCorreo>();

    /// <summary>Bitácora de intentos de timbrado — una fila por llamada al PAC ([Decisión 01-G] G4).</summary>
    public DbSet<BitacoraIntentoTimbrado> BitacorasIntentoTimbrado => Set<BitacoraIntentoTimbrado>();

    /// <summary>Control de idempotencia de la ingesta — fuente de verdad (F3-PR1).</summary>
    public DbSet<IngestaControl> IngestaControles => Set<IngestaControl>();

    /// <summary>Snapshots crudos de pedidos de orígenes externos (F3-PR1).</summary>
    public DbSet<PedidoFacturableSnapshot> PedidosFacturablesSnapshot => Set<PedidoFacturableSnapshot>();

    /// <summary>Bandeja de excepciones de importación (F3-PR1).</summary>
    public DbSet<ExcepcionImportacion> ExcepcionesImportacion => Set<ExcepcionImportacion>();

    /// <summary>Cajas del módulo — alcance de datos + sesión de efectivo (CAJAS-PR1, 12-cajas.md).</summary>
    public DbSet<Caja> Cajas => Set<Caja>();

    /// <summary>Concesiones de alcance para usuarios sin caja (CAJAS-PR1, [Decisión 12-6]).</summary>
    public DbSet<UsuarioAlcance> UsuariosAlcance => Set<UsuarioAlcance>();

    /// <summary>Sesiones de efectivo — Capa B de Cajas (CAJAS-PR3, 12-cajas.md §5).</summary>
    public DbSet<CajaSesion> CajaSesiones => Set<CajaSesion>();

    /// <summary>Movimientos de las sesiones de caja (CAJAS-PR3, [Decisión 12-5]).</summary>
    public DbSet<CajaMovimiento> CajaMovimientos => Set<CajaMovimiento>();

    /// <summary>Autorizaciones consumibles de apertura de caja ajena (CAJAS-PR3, [Decisión 12-1]).</summary>
    public DbSet<AutorizacionAperturaCaja> AutorizacionesAperturaCaja => Set<AutorizacionAperturaCaja>();

    /// <summary>Ajustes pendientes drenados en la próxima apertura (CAJAS-PR3, [Decisión 12-C]).</summary>
    public DbSet<CajaAjustePendiente> CajaAjustesPendientes => Set<CajaAjustePendiente>();

    /// <summary>Cobros de mostrador — desacoplan cobro de emisión (CAJAS-PR4, [Decisión 12-3]).</summary>
    public DbSet<CobroMostrador> CobrosMostrador => Set<CobroMostrador>();

    /// <summary>Marcas de idempotencia de integration events consumidos (PR gemelo TES-PR7).</summary>
    public DbSet<Domain.Eventos.EventoProcesado> EventosProcesados => Set<Domain.Eventos.EventoProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new IntegrationEventOutboxEntryConfiguration());
        modelBuilder.ApplyConfiguration(new ComprobanteConfiguration());
        modelBuilder.ApplyConfiguration(new FacturaVentaConfiguration());
        modelBuilder.ApplyConfiguration(new FacturaVentaLineaConfiguration());
        modelBuilder.ApplyConfiguration(new FacturaAnticipoConfiguration());
        modelBuilder.ApplyConfiguration(new AnticipoConfiguration());
        modelBuilder.ApplyConfiguration(new AnticipoVinculacionConfiguration());
        modelBuilder.ApplyConfiguration(new NotaCreditoConfiguration());
        modelBuilder.ApplyConfiguration(new RelacionCfdiConfiguration());
        modelBuilder.ApplyConfiguration(new SolicitudCancelacionConfiguration());
        modelBuilder.ApplyConfiguration(new ReciboPagoConfiguration());
        modelBuilder.ApplyConfiguration(new ReciboPagoFacturaConfiguration());
        modelBuilder.ApplyConfiguration(new ReciboPagoFacturaImpuestoConfiguration());
        modelBuilder.ApplyConfiguration(new ComplementoCceConfiguration());
        modelBuilder.ApplyConfiguration(new ComplementoCceLineaConfiguration());
        modelBuilder.ApplyConfiguration(new VehiculoConfiguration());
        modelBuilder.ApplyConfiguration(new OperadorConfiguration());
        modelBuilder.ApplyConfiguration(new CartaPorteConfiguration());
        modelBuilder.ApplyConfiguration(new CartaPorteMercanciaConfiguration());
        modelBuilder.ApplyConfiguration(new AutorizacionVentaActivoConfiguration());
        modelBuilder.ApplyConfiguration(new PedidoFacturableConfiguration());
        modelBuilder.ApplyConfiguration(new PedidoFacturableLineaConfiguration());
        modelBuilder.ApplyConfiguration(new BitacoraEnvioCorreoConfiguration());
        modelBuilder.ApplyConfiguration(new BitacoraIntentoTimbradoConfiguration());
        modelBuilder.ApplyConfiguration(new IngestaControlConfiguration());
        modelBuilder.ApplyConfiguration(new PedidoFacturableSnapshotConfiguration());
        modelBuilder.ApplyConfiguration(new ExcepcionImportacionConfiguration());
        modelBuilder.ApplyConfiguration(new CajaConfiguration());
        modelBuilder.ApplyConfiguration(new CajaSucursalConfiguration());
        modelBuilder.ApplyConfiguration(new CajaCanalConfiguration());
        modelBuilder.ApplyConfiguration(new CajaUsuarioConfiguration());
        modelBuilder.ApplyConfiguration(new UsuarioAlcanceConfiguration());
        modelBuilder.ApplyConfiguration(new CajaSesionConfiguration());
        modelBuilder.ApplyConfiguration(new CajaSesionCorteConfiguration());
        modelBuilder.ApplyConfiguration(new CajaMovimientoConfiguration());
        modelBuilder.ApplyConfiguration(new AutorizacionAperturaCajaConfiguration());
        modelBuilder.ApplyConfiguration(new CajaAjustePendienteConfiguration());
        modelBuilder.ApplyConfiguration(new CobroMostradorConfiguration());
        modelBuilder.ApplyConfiguration(new CobroMostradorFormaPagoConfiguration());
        modelBuilder.ApplyConfiguration(new EventoProcesadoConfiguration());
    }
}
