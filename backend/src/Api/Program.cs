using Azure.Monitor.OpenTelemetry.AspNetCore;
using HealthChecks.NpgSql;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Millet.Almacen.Infrastructure;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.Auth;
using Millet.Api.Auth.Provisioning;
using Millet.Api.Endpoints.Catalogos;
using Millet.Api.Endpoints.Compras;
using Millet.Api.Endpoints.Settings;
using Millet.Api.Hubs;
using Millet.Api.Web;
using Millet.CentrosCosto.Application;
using Millet.CentrosCosto.Infrastructure;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.Compras.Application;
using Millet.Compras.Application.CrearRequisicion;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.Stubs;
using Millet.CuentasPorCobrar.Application;
using Millet.CuentasPorCobrar.Infrastructure;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application;
using Millet.CuentasPorPagar.Infrastructure;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Tesoreria.Application;
using Millet.Tesoreria.Infrastructure;
using Millet.Tesoreria.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure;
using Millet.SharedKernel.Infrastructure.HealthChecks;
using Millet.SharedKernel.Infrastructure.Idempotency;
using Millet.SharedKernel.Infrastructure.Logging;
using Millet.SharedKernel.Infrastructure.Outbox;
// HealthCheckResponseWriter vive en Millet.Api.Web (necesita HttpContext)
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Infrastructure;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Fiscal.Infrastructure;
using Millet.SharedKernel.Infrastructure.Persistence.Interceptors;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// === Auth:Mode validation (fail-fast en arranque) ===
var authMode = builder.Configuration.GetValue<AuthMode?>("Auth:Mode") ?? AuthMode.EntraId;
AuthModeValidator.EnsureAllowedForEnvironment(authMode, builder.Environment.IsDevelopment());

// === Logging: Serilog estructurado + masker + OTel Distro export ===
//
// writeToProviders: true es CRÍTICO. Sin él, Serilog reemplaza
// completamente el pipeline de ILogger y los providers nativos de
// Microsoft.Extensions.Logging (incluido OpenTelemetry / Azure Monitor)
// nunca reciben los eventos. Resultado: los logs salen a Console pero
// NO llegan a Application Insights — los catch generales de los
// workers quedan invisibles en runtime productivo (bug detectado en
// la validación E2E de PR D — workers fallaban silenciosamente sin
// que apareciera nada en App Insights traces/exceptions).
//
// Con writeToProviders: true, Serilog escribe al sink Console
// (estructurado) Y reenvía cada LogEvent a los ILoggerProvider
// registrados — incluido el de OpenTelemetry agregado abajo, que
// el Azure Monitor Distro exporta a App Insights.
builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .ReadFrom.Services(services)
       .Enrich.FromLogContext()
       .Enrich.WithEnvironmentName()
       .Enrich.WithMachineName()
       .Enrich.WithProcessId()
       .Enrich.WithThreadId()
       .Enrich.With<MaskSensitivePropertiesEnricher>()
       .WriteTo.Console();
}, writeToProviders: true);

// Registra OpenTelemetry como ILoggerProvider de Microsoft.Extensions.Logging.
// Esto es el "sink" al que Serilog reenvía cuando writeToProviders=true.
// El Azure Monitor Distro (UseAzureMonitor más abajo) consume estos
// providers y exporta a App Insights traces/exceptions.
builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.ParseStateValues = true;
});

// Azure Monitor OpenTelemetry Distro: exporta logs (Microsoft.Extensions.Logging),
// traces (HttpClient, EF Core, Service Bus) y métricas a Application Insights.
// W3C Trace Context se propaga automáticamente.
// Solo wireamos UseAzureMonitor si la connection string está configurada —
// las versiones 1.3.0+ del Distro lanzan en arranque si no la encuentran.
// Dev local sin connection string → no se wirea, no se exporta. Logs y
// traces siguen yendo a la consola via Serilog.
var appInsightsConn = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ?? builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsightsConn))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor()
        .WithTracing(tracing => tracing
            .AddSource(Millet.Compras.Application.ComprasActivitySource.Name)
            // PR B — distributed tracing del módulo Integraciones.Aw.
            .AddSource(Millet.Integraciones.Aw.Application.IntegracionesAwActivitySource.Name))
        .WithMetrics(metrics => metrics
            .AddMeter(Millet.Api.Hubs.ComprasHubMeter.Name)
            // PR A — counters del módulo Identidad (auth.sp.*).
            .AddMeter(Millet.Identidad.Infrastructure.Telemetry.IdentidadMeter.Name)
            // PR C — métricas del módulo Integraciones.Aw (drop, correlation,
            // hybrid_connection.healthy gauge).
            .AddMeter(Millet.Integraciones.Aw.Application.IntegracionesAwMeter.Name));
}

// === Servicios transversales ===
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddScoped<ICurrentEmpresaContext, CurrentEmpresaContext>();
builder.Services.AddScoped<IAuditOriginContext, AuditOriginContext>();
builder.Services.AddScoped<IAuditCorrelationContext, AuditCorrelationContext>();
builder.Services.AddSingleton<IPermissionCache, InMemoryPermissionCache>();

// === IIntegrationEventPublisher (F6-PR1, ADR-0009): Outbox real ===
// El publisher encola al buffer scoped; el OutboxSaveChangesInterceptor
// drena el buffer en SavingChanges y crea filas en
// compras.integration_events_outbox dentro de la TX EF (atomicidad).
builder.Services.AddScoped<IIntegrationEventBuffer, InMemoryIntegrationEventBuffer>();
builder.Services.AddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();
builder.Services.AddScoped<OutboxSaveChangesInterceptor>();

// === Outbox publisher worker (F6-PR2 + PR B refactor) ===
// PR B introdujo named OutboxPublisherOptions keyed por nombre del
// DbContext (Opción D del análisis) para soportar múltiples workers
// (uno por módulo) cada uno publicando a su propio topic Service Bus.
//
// Cada módulo registra:
//   services.AddOptions<OutboxPublisherOptions>(nameof(MyDbContext))
//       .Bind(config.GetSection("MyModulo:Outbox"));
// y el worker resuelve sus options vía IOptionsMonitor.Get(nameof(TDbContext))
// con fail-fast en arranque si el nombre no está registrado.
//
// El sender (Service Bus o NoOp) es singleton compartido entre módulos —
// recibe el topic name como parámetro de SendAsync (S-A del análisis).
builder.Services
    .AddOptions<OutboxPublisherOptions>(nameof(ComprasDbContext))
    .Bind(builder.Configuration.GetSection("Compras:Outbox"));
builder.Services
    .AddOptions<OutboxPublisherOptions>(nameof(CompartidoDbContext))
    .Bind(builder.Configuration.GetSection("Compartido:Outbox"));
builder.Services
    .AddOptions<OutboxPublisherOptions>(nameof(Millet.Integraciones.Aw.Infrastructure.Persistence.IntegracionesAwDbContext))
    .Bind(builder.Configuration.GetSection("IntegracionesAw:Outbox"));
// F0-PR1 Almacén: outbox del módulo Almacén (topic
// `almacen-events` configurado en appsettings).
builder.Services
    .AddOptions<OutboxPublisherOptions>(nameof(AlmacenDbContext))
    .Bind(builder.Configuration.GetSection("Almacen:Outbox"));
// F0-PR1 CxP: outbox del módulo CuentasPorPagar (topic
// `cuentas-por-pagar-events` configurado en appsettings).
builder.Services
    .AddOptions<OutboxPublisherOptions>(nameof(CuentasPorPagarDbContext))
    .Bind(builder.Configuration.GetSection("CuentasPorPagar:Outbox"));
// F0-PR1 Facturación: outbox del módulo Facturación (topic
// `facturacion-events` configurado en appsettings).
builder.Services
    .AddOptions<OutboxPublisherOptions>(nameof(FacturacionDbContext))
    .Bind(builder.Configuration.GetSection("Facturacion:Outbox"));
// CXC-PR1: outbox del módulo CuentasPorCobrar (topic
// `cuentas-por-cobrar-events` configurado en appsettings).
builder.Services
    .AddOptions<OutboxPublisherOptions>(nameof(CuentasPorCobrarDbContext))
    .Bind(builder.Configuration.GetSection("CuentasPorCobrar:Outbox"));
// TES-PR4: outbox del módulo Tesorería (topic `tesoreria-events`
// configurado en appsettings) — publisher real de los 4 eventos espejo
// congelados que CxP ya consume (F9-PR1).
builder.Services
    .AddOptions<OutboxPublisherOptions>(nameof(TesoreriaDbContext))
    .Bind(builder.Configuration.GetSection("Tesoreria:Outbox"));

// Connection string: se lee de cualquier sección (todas las options del
// proyecto comparten el mismo Service Bus namespace) — tomamos la primera
// que la tenga poblada. App Service la inyecta vía KV ref como
// ServiceBus__ConnectionString (mismo secret usado por Compras).
var outboxConnString =
    builder.Configuration["Compras:Outbox:ServiceBusConnectionString"]
    ?? builder.Configuration["Compartido:Outbox:ServiceBusConnectionString"]
    ?? builder.Configuration["IntegracionesAw:Outbox:ServiceBusConnectionString"]
    ?? builder.Configuration["Almacen:Outbox:ServiceBusConnectionString"]
    ?? builder.Configuration["CuentasPorPagar:Outbox:ServiceBusConnectionString"]
    ?? builder.Configuration["Facturacion:Outbox:ServiceBusConnectionString"]
    ?? builder.Configuration["ServiceBus:ConnectionString"];

if (!string.IsNullOrWhiteSpace(outboxConnString))
{
    builder.Services.AddSingleton(_ => new Azure.Messaging.ServiceBus.ServiceBusClient(outboxConnString));
    builder.Services.AddSingleton<IIntegrationEventBusSender, ServiceBusIntegrationEventBusSender>();

    // F3-PR1: listener Service Bus de eventos de CxP. Solo se wirea cuando
    // la conexión real existe — en dev local sin SB, los handlers se pueden
    // ejercitar directo desde tests (CxpEventListenerWorker.ProcessAsync es
    // internal a Almacen.IntegrationTests).
    builder.Services.AddHostedService<Millet.Almacen.Infrastructure.Workers.CxpEventListenerWorker>();

    // F6-PR3 CxP: listener Service Bus para eventos de Almacén
    // (recepciones + devoluciones). Cierra el ciclo bidireccional.
    builder.Services.AddHostedService<Millet.CuentasPorPagar.Infrastructure.Workers.AlmacenEventListenerWorker>();

    // F9-PR1 CxP: listener Service Bus para eventos de Tesorería
    // (pagos, reversas, REPP, cancelaciones).
    builder.Services.AddHostedService<Millet.CuentasPorPagar.Infrastructure.Workers.TesoreriaEventListenerWorker>();

    // Triada: listener Service Bus para eventos de Compras
    // (OC autorizada, OC cancelada). Cierra inbound Compras → CxP
    // — los handlers OcAutorizadaCommand y OcCanceladaCommand ya
    // existen en Application/EventListeners pero estaban huérfanos
    // hasta este wireup.
    builder.Services.AddHostedService<Millet.CuentasPorPagar.Infrastructure.Workers.ComprasEventListenerWorker>();

    // Triada (PR D): listener Service Bus para eventos de CxP que
    // Compras suscribe. Cierra outbound CxP → Compras — sin esto, el
    // listener FacturaProveedorRegistradaListener queda huérfano y el
    // sub-estado Facturación de OC nunca avanza tras captura de factura.
    builder.Services.AddHostedService<Millet.Compras.Infrastructure.Workers.CxpEventListenerWorker>();

    // Triada: listener Service Bus para eventos de Almacén que Compras
    // suscribe. Cierra inbound Almacén → Compras — sin esto, las
    // recepciones de OC no incrementan `LineaOrdenCompra.CantidadRecibida`
    // y las recepciones parciales siguen mostrando el pendiente original.
    // Subscription `compras-subscription-almacen` en topic
    // `almacen-events`.
    builder.Services.AddHostedService<Millet.Compras.Infrastructure.Workers.AlmacenEventListenerWorker>();

    // CXC-PR3: listener Service Bus de eventos de Facturación → proyección
    // factura_cartera (alta, pagos REPP/mostrador, NC, cancelaciones,
    // anticipos informativos). Subscription `cuentas-por-cobrar-subscription`
    // en topic `facturacion-events` (Bicep en el mismo PR).
    builder.Services.AddHostedService<Millet.CuentasPorCobrar.Infrastructure.Workers.FacturacionEventListenerWorker>();

    // TES-PR3: listener Service Bus de eventos de CxP → proyección
    // pasivo_pendiente_pago (bandeja de egresos de Tesorería). Subscription
    // `tesoreria-subscription` en topic `cuentas-por-pagar-events` (Bicep en
    // el mismo PR; deploy de infra manual con what-if).
    builder.Services.AddHostedService<Millet.Tesoreria.Infrastructure.Workers.CuentasPorPagarEventListenerWorker>();

    // TES-PR7: listeners del lado ingresos de Tesorería (§3.3, TES-9).
    // Propuestas de CxC → bandeja de depósitos por confirmar; cierres de
    // sesión de caja → expectativa de depósito (cierra
    // PLATFORM-TODO(<TesoreriaCajaSesion>)); REPP timbrado → repp_timbrado.
    // Subscriptions `tesoreria-subscription` en `cuentas-por-cobrar-events`
    // y `facturacion-events` (Bicep en el mismo PR; deploy de infra manual
    // con what-if).
    builder.Services.AddHostedService<Millet.Tesoreria.Infrastructure.Workers.CuentasPorCobrarEventListenerWorker>();
    builder.Services.AddHostedService<Millet.Tesoreria.Infrastructure.Workers.FacturacionEventListenerWorker>();

    // PR gemelo TES-PR7: listener Service Bus de pago-cliente.confirmado.v1
    // → EmitirReppCommand. Cierra PLATFORM-TODO(<PagoClienteConfirmado>);
    // el endpoint manual POST /facturacion/repp sigue vivo como fallback.
    // Subscription `facturacion-tesoreria-sub` en topic `tesoreria-events`
    // (Bicep en el mismo PR; deploy de infra manual con what-if).
    builder.Services.AddHostedService<Millet.Facturacion.Infrastructure.Workers.TesoreriaEventListenerWorker>();
}
else
{
    builder.Services.AddSingleton<IIntegrationEventBusSender, NoOpIntegrationEventBusSender>();
}

builder.Services.AddHostedService<OutboxPublisherWorker<ComprasDbContext>>();
builder.Services.AddHostedService<OutboxPublisherWorker<CompartidoDbContext>>();
builder.Services.AddHostedService<OutboxPublisherWorker<Millet.Integraciones.Aw.Infrastructure.Persistence.IntegracionesAwDbContext>>();
builder.Services.AddHostedService<OutboxPublisherWorker<AlmacenDbContext>>();
builder.Services.AddHostedService<OutboxPublisherWorker<CuentasPorPagarDbContext>>();
builder.Services.AddHostedService<OutboxPublisherWorker<FacturacionDbContext>>();
builder.Services.AddHostedService<OutboxPublisherWorker<CuentasPorCobrarDbContext>>();
builder.Services.AddHostedService<OutboxPublisherWorker<TesoreriaDbContext>>();

// F5-PR1: Worker SLA del vale (A14). Diario, notifica al día 1 al
// Coordinador y al día 2 al Jefe Almacén si el vale no se regulariza.
builder.Services.AddHostedService<Millet.Almacen.Infrastructure.Workers.RegularizacionValeSlaWorker>();

// ADR-0047 PR5.D: worker del motor de reorden. Deshabilitado por default
// (ReordenWorker:Disabled=true) — opt-in por ambiente cuando 5.E/5.F estén listos.
builder.Services
    .AddOptions<Millet.Almacen.Infrastructure.Workers.ReordenWorkerOptions>()
    .Bind(builder.Configuration.GetSection(Millet.Almacen.Infrastructure.Workers.ReordenWorkerOptions.SectionName));
builder.Services.AddHostedService<Millet.Almacen.Infrastructure.Workers.ReordenWorker>();

// === Catálogos compartidos: seed de prueba (F7-PR1, MVP) ===
// Hosted service que carga proveedores y artículos de prueba en
// dev/staging. En Production se autoexcluye — las tablas se popularán
// vía importer SAP cuando el cliente entregue el export (F7-PR2 deferred).
builder.Services.AddHostedService<Millet.Compartido.Infrastructure.Seed.CatalogosTestSeedHostedService>();

// === Almacén: seed inicial (F1-PR3, MVP) ===
// 4 almacenes + 12 sub-almacenes (3 por almacén: Insumos, MAT-DIR, MAT-REV).
// MAT-REV (MaterialEnRevision, A15) es destino del sub-flujo 8.A (devolución
// interna por daño). Idempotente — preserva ids deterministas para que
// los movimientos de inventario (F2) tengan referencias estables.
// Se autoexcluye en Production (catálogo real vía importer SAP, deferred).
builder.Services.AddHostedService<Millet.Almacen.Infrastructure.Seed.AlmacenSeedHostedService>();

// === Idempotencia HTTP (F8-PR1, ADR-0020) ===
// Bind opciones (sección Idempotency:) y registra el cleanup job que
// purga keys vencidas según política de retención.
builder.Services
    .AddOptions<IdempotencyOptions>()
    .Bind(builder.Configuration.GetSection(IdempotencyOptions.SectionName));
builder.Services.AddHostedService<IdempotencyKeysCleanupJob>();

// === Cajas de Facturación (CAJAS-PR3) ===
// Defaults en código (vigencia de autorización de apertura 30 min);
// sobrescribibles vía Facturacion:Cajas:* — ningún ambiente lo requiere.
builder.Services
    .AddOptions<Millet.Facturacion.Application.Cajas.Sesiones.CajasOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Facturacion.Application.Cajas.Sesiones.CajasOptions.SectionName));

// === REPP automático desde Tesorería (PR gemelo TES-PR7) ===
// Sucursal emisora de los REPP que dispara pago-cliente.confirmado.v1
// (setting Facturacion__ReppAutomatico__SucursalClave en appservice.bicep;
// sin config cae a la única sucursal activa si solo hay una).
builder.Services
    .AddOptions<Millet.Facturacion.Application.EventListeners.ReppAutomaticoOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Facturacion.Application.EventListeners.ReppAutomaticoOptions.SectionName));

// === Compras: stubs cross-module (F3-PR2) ===
// AddComprasStubs lee Compras:UseStubs y, si es true, registra las 5
// implementaciones InMemory* de los puertos (Almacén + OC). Falla
// ruidosamente en arranque si el flag está activo en Production.
builder.Services.AddComprasStubs(builder.Configuration, builder.Environment);

// === Compras IAlmacenEntregasReadPort → adapter real ===
// Permite al handler `ObtenerRequisicionPorIdHandler` enriquecer
// `LineaResponse` con `cantEntregadoDeAlmacen` y `cantPendienteEntregar`,
// que el FE de salidas usa para mostrar lo ya entregado y calcular
// pendiente. Sin este wireup el handler caería al NoOp que devuelve
// vacío (todas las líneas se ven como "nada entregado").
//
// Nota de terminología: "entregar" = salida del almacén al solicitante.
// NO confundir con "surtir" (recibir del proveedor vía OC + recepción).
builder.Services.AddScoped<
    Millet.Compras.Domain.Ports.Almacen.IAlmacenEntregasReadPort,
    Millet.Compras.Infrastructure.PublicAdapters.AlmacenEntregasReadAdapter>();

// === Compras: job one-shot de migración de históricas (ADR-0043 PR #4) ===
// Reconstruye CantidadEntregada de las RQ previas a la conmutación y reclasifica
// las Cerrada que no estaban realmente entregadas. Dormido por config
// (Enabled=false default); se prende en Azure para la corrida one-shot
// (DryRun=true reporta, DryRun=false aplica) y se vuelve a apagar tras aplicar.
builder.Services
    .AddOptions<Millet.Compras.Application.MigracionEntrega.MigracionEntregaHistoricaOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Compras.Application.MigracionEntrega.MigracionEntregaHistoricaOptions.SectionName));
builder.Services.AddHostedService<Millet.Compras.Infrastructure.Workers.MigracionEntregaHistoricaJob>();

// === Compras IConsultarStockPort → adapter real ===
// Reemplaza el InMemoryConsultarStockPort stub (config-driven que
// devolvía stock fake). Sin esto, las requisiciones autorizadas
// mandaban TODO el monto a CantidadDeCompra aunque el artículo ya
// estuviera recibido en almacén (bug detectado en validación E2E
// post-PR #293/#301). El adapter delega en el Open Host Service
// IAlmacenSaldoQueryPort (ADR-0047 PR2) — Compras ya NO lee
// almacen.saldos_inventario directo; Almacén resuelve el rollup por
// almacén (sus sub-almacenes/ubicaciones).
//
// Scoped porque IAlmacenSaldoQueryPort usa AlmacenDbContext (scoped).
// El stub era Singleton — el override gana por ser la última registración.
builder.Services.AddScoped<
    Millet.Compras.Domain.Ports.Almacen.IConsultarStockPort,
    Millet.Compras.Infrastructure.PublicAdapters.AlmacenStockReadAdapter>();

// === PR-A2: Compras valida cross-table sucursal/depto/almacén en CrearRequisicion ===
// ISucursalDepartamentoReadPort lee compartido.sucursal_departamentos (PR-A1 #333).
// IAlmacenReadPort lee almacen.almacenes para validar coherencia (sucursal, almacén).
// Ambos adapters viven en Compras.Infrastructure.PublicAdapters porque Almacen y
// Compartido no pueden referenciar Compras (regla del csproj — evita ciclos).
builder.Services.AddScoped<
    Millet.Compras.Domain.Ports.Administracion.ISucursalDepartamentoReadPort,
    Millet.Compras.Infrastructure.PublicAdapters.SucursalDepartamentoReadAdapter>();
builder.Services.AddScoped<
    Millet.Compras.Domain.Ports.Almacen.IAlmacenReadPort,
    Millet.Compras.Infrastructure.PublicAdapters.AlmacenReadAdapter>();

// === ADR-0042: enriquecer DTOs de requisiciones con nombres cross-módulo ===
// IUsuarioReadPort lee identidad.usuarios (nombre del requisitante);
// IDepartamentoReadPort lee compartido.departamentos (clave + nombre).
// Adapters en Compras.Infrastructure.PublicAdapters (mismo patrón que arriba).
builder.Services.AddScoped<
    Millet.Compras.Domain.Ports.Identidad.IUsuarioReadPort,
    Millet.Compras.Infrastructure.PublicAdapters.UsuarioReadAdapter>();
builder.Services.AddScoped<
    Millet.Compras.Domain.Ports.Administracion.IDepartamentoReadPort,
    Millet.Compras.Infrastructure.PublicAdapters.DepartamentoReadAdapter>();
// ADR-0042 (addendum): enriquecer el detalle de req/OC con clave/nombre de
// artículo (líneas) y proveedor (cabecera), resueltos server-side en batch.
// Adapters leen compartido.articulos/proveedores via CompartidoDbContext.
builder.Services.AddScoped<
    Millet.Compras.Domain.Ports.DatosMaestros.IArticuloReadPort,
    Millet.Compras.Infrastructure.PublicAdapters.ArticuloReadAdapter>();
builder.Services.AddScoped<
    Millet.Compras.Domain.Ports.DatosMaestros.IProveedorReadPort,
    Millet.Compras.Infrastructure.PublicAdapters.ProveedorReadAdapter>();

// === Compras OC: blob storage (F2-PR4 stub, F10-PR3 real con Azure) ===
// Si `Compras:Oc:BlobStorage:ConnectionString` está configurado (viene
// de Key Vault en QA/Prod), se usa Azure Blob real con BlobServiceClient.
// Si no, se cae al stub filesystem local (dev sin config).
builder.Services
    .AddOptions<Millet.Compras.Infrastructure.Oc.Stubs.LocalFilesystemBlobStubOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Compras.Infrastructure.Oc.Stubs.LocalFilesystemBlobStubOptions.SectionName));
builder.Services
    .AddOptions<Millet.Compras.Infrastructure.Oc.Blob.AzureBlobOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Compras.Infrastructure.Oc.Blob.AzureBlobOptions.SectionName));

var blobConnString = builder.Configuration.GetSection(
    Millet.Compras.Infrastructure.Oc.Blob.AzureBlobOptions.SectionName)
    .GetValue<string>(nameof(Millet.Compras.Infrastructure.Oc.Blob.AzureBlobOptions.ConnectionString));
if (!string.IsNullOrWhiteSpace(blobConnString))
{
    builder.Services.AddSingleton(_ => new Azure.Storage.Blobs.BlobServiceClient(blobConnString));
    builder.Services.AddSingleton<
        Millet.Compras.Domain.Ports.Blob.IAlmacenarBlobPort,
        Millet.Compras.Infrastructure.Oc.Blob.AzureBlobAlmacenarBlobPort>();
}
else
{
    builder.Services.AddSingleton<
        Millet.Compras.Domain.Ports.Blob.IAlmacenarBlobPort,
        Millet.Compras.Infrastructure.Oc.Stubs.LocalFilesystemBlobStub>();
}

// === Almacén: blob storage (F2-PR4) ===
// Cierra PLATFORM-TODO(<AlmacenPackingListBlob>) del 01-diseno §6.1.
// Modela el mismo patrón Compras: puerto en módulo, adapter Azure real
// (cuando hay connection string) o stub filesystem local (dev).
//
// Si Compras y Almacén comparten cuenta de Azure Storage (caso típico
// en prod single-tenant), la registración de `BlobServiceClient` ya
// ocurrió en el bloque de Compras arriba y aquí se reusa. Si Almacén
// tiene su propia connection string distinta, este `TryAddSingleton`
// la registra (último resort — el modelo de "primer registro gana"
// asume mismas credenciales; un futuro refactor a Keyed Services
// permitirá cuentas distintas por módulo).
builder.Services
    .AddOptions<Millet.Almacen.Infrastructure.Stubs.LocalFilesystemAlmacenBlobStubOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Almacen.Infrastructure.Stubs.LocalFilesystemAlmacenBlobStubOptions.SectionName));
builder.Services
    .AddOptions<Millet.Almacen.Infrastructure.Blob.AzureBlobOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Almacen.Infrastructure.Blob.AzureBlobOptions.SectionName));

var almacenBlobConnString = builder.Configuration.GetSection(
    Millet.Almacen.Infrastructure.Blob.AzureBlobOptions.SectionName)
    .GetValue<string>(nameof(Millet.Almacen.Infrastructure.Blob.AzureBlobOptions.ConnectionString));
if (!string.IsNullOrWhiteSpace(almacenBlobConnString))
{
    Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
        .TryAddSingleton(
            builder.Services,
            _ => new Azure.Storage.Blobs.BlobServiceClient(almacenBlobConnString));
    builder.Services.AddSingleton<
        Millet.Almacen.Domain.Ports.Blob.IAlmacenarBlobPort,
        Millet.Almacen.Infrastructure.Blob.AzureBlobAlmacenarBlobPort>();
}
else
{
    builder.Services.AddSingleton<
        Millet.Almacen.Domain.Ports.Blob.IAlmacenarBlobPort,
        Millet.Almacen.Infrastructure.Stubs.LocalFilesystemAlmacenBlobStub>();
}

// === Integraciones.Aw: blob storage (PDF de ofertas/pedidos de A+W) ===
// Mismo patrón Compras/Almacén: puerto en el módulo, adapter Azure real
// (cuando hay connection string) o stub filesystem local (dev). Si Compras
// o Almacén ya registraron `BlobServiceClient` (misma cuenta de Storage en
// prod single-tenant), el TryAddSingleton lo reusa; si Aw tiene connection
// string propia distinta, la registra (primer registro gana — asume mismas
// credenciales).
builder.Services
    .AddOptions<Millet.Integraciones.Aw.Infrastructure.Stubs.LocalFilesystemBlobStubOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Integraciones.Aw.Infrastructure.Stubs.LocalFilesystemBlobStubOptions.SectionName));
builder.Services
    .AddOptions<Millet.Integraciones.Aw.Infrastructure.Blob.AzureBlobOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Integraciones.Aw.Infrastructure.Blob.AzureBlobOptions.SectionName));

var awBlobConnString = builder.Configuration.GetSection(
    Millet.Integraciones.Aw.Infrastructure.Blob.AzureBlobOptions.SectionName)
    .GetValue<string>(nameof(Millet.Integraciones.Aw.Infrastructure.Blob.AzureBlobOptions.ConnectionString));
if (!string.IsNullOrWhiteSpace(awBlobConnString))
{
    Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
        .TryAddSingleton(
            builder.Services,
            _ => new Azure.Storage.Blobs.BlobServiceClient(awBlobConnString));
    builder.Services.AddSingleton<
        Millet.Integraciones.Aw.Domain.Ports.Blob.IAlmacenarBlobPort,
        Millet.Integraciones.Aw.Infrastructure.Blob.AzureBlobAlmacenarBlobPort>();
}
else
{
    builder.Services.AddSingleton<
        Millet.Integraciones.Aw.Domain.Ports.Blob.IAlmacenarBlobPort,
        Millet.Integraciones.Aw.Infrastructure.Stubs.LocalFilesystemBlobStub>();
}

// === Compras OC: PDF real con QuestPDF (F6-PR3) ===
// Reemplaza LocalPdfOrdenCompraStub por QuestPdfOrdenCompraGenerator con
// layout institucional. El stub se conserva en el assembly para fixtures
// de test rápidos, pero el wiring de producción usa QuestPDF.
builder.Services.AddSingleton<
    Millet.Compras.Domain.Ports.Pdf.IGenerarPdfOrdenCompraPort,
    Millet.Compras.Infrastructure.Oc.Pdf.QuestPdfOrdenCompraGenerator>();

// === Compras: evaluator de matriz multidimensional (F9-PR2) ===
// Implementación productiva con las 3 capas del §3.bis.2:
// (1) departamental, (2) monto vs umbral, (3) naturaleza fuerza N2.
// Reemplaza al v0 EvaluadorMontoVsUmbral. NaturalezaResolverService
// es scoped porque consume CompartidoDbContext (scoped).
builder.Services.AddScoped<Millet.Compras.Application.Matriz.NaturalezaResolverService>();

// ADR-0047 PR5.C: servicio de folios de RQ (extraído de CrearRequisicionHandler)
// reusado por el path humano y el path de sistema (motor de reorden).
builder.Services.AddScoped<
    Millet.Compras.Application.Folios.IFolioSecuenciaService,
    Millet.Compras.Infrastructure.Folios.FolioSecuenciaService>();

// === Compras — Trazabilidad cross-módulo (F7-PR2) ===
builder.Services.AddScoped<
    Millet.Compras.Domain.Trazabilidad.IObtenerArbolDocumentosService,
    Millet.Compras.Infrastructure.Trazabilidad.ObtenerArbolDocumentosService>();

// Providers cross-módulo que aportan nodos descendentes al árbol de
// trazabilidad. Registrados como IEnumerable<IProveedorNodosTrazabilidad>
// — el servicio los itera todos y agrega lo que cada uno conozca para
// el origen. Antes de este wireup el árbol de OC siempre devolvía
// Descendentes: [] (el doc-comment del servicio lo anunciaba como
// "V1 solo conoce RQ ↔ OC"). Cierra el feature: recepciones (Almacén)
// + facturas (CxP). Pagos quedan diferidos hasta que Tesorería exista.
builder.Services.AddScoped<
    Millet.Compras.Domain.Trazabilidad.IProveedorNodosTrazabilidad,
    Millet.Compras.Infrastructure.PublicAdapters.AlmacenRecepcionesTrazabilidadProvider>();
builder.Services.AddScoped<
    Millet.Compras.Domain.Trazabilidad.IProveedorNodosTrazabilidad,
    Millet.CuentasPorPagar.Infrastructure.PublicAdapters.CxpFacturasTrazabilidadProvider>();
builder.Services.AddScoped<
    Millet.Compras.Domain.Matriz.IRequiereNivelEvaluator,
    Millet.Compras.Application.Matriz.EvaluadorMultidimensional>();
builder.Services.AddScoped<
    Millet.Compras.Domain.Ports.Matriz.IResolverAutorizadorPort,
    Millet.Compras.Application.Matriz.ResolverAutorizadorService>();

// === Settings schema providers (F-Admin-PR1.1, ADR-0034) ===
// Cada módulo de negocio registra su ISettingsSchemaProvider; el endpoint
// genérico /api/v1/{modulo}/settings/* resuelve la instancia correcta
// por nombre de módulo.
builder.Services.AddScoped<
    Millet.SharedKernel.Application.Settings.ISettingsSchemaProvider,
    Millet.Compras.Application.Settings.ComprasSettingsSchemaProvider>();

// === CQRS / Validación / Mapping (F0-PR1, F1-PR2) ===
// AddMilletApplication wirea MediatR + FluentValidation + Mapster con
// los assemblies indicados. Cada módulo nuevo agrega su typeof(Algo).Assembly
// aquí.
// NOTA: Administracion.Application.Empresas.CrearEmpresaCommand vive
// físicamente en el assembly Millet.Compartido (mismo que DatosMaestros)
// — el typeof aquí queda como referencia explícita del módulo aunque
// AddMilletApplication ya recibe el assembly via DatosMaestros.
builder.Services.AddMilletApplication(
    typeof(Program).Assembly,
    typeof(CrearRequisicionCommand).Assembly,
    typeof(Millet.DatosMaestros.Application.Catalogos.ReclasificarNaturalezaArticulosCommand).Assembly,
    typeof(Millet.Administracion.Application.Empresas.CrearEmpresaCommand).Assembly,
    typeof(Millet.Identidad.Application.Roles.CrearRolCommand).Assembly,
    typeof(Millet.Integraciones.Aw.Application.Commands.RegistrarCotizacionEdi.RegistrarCotizacionEdiCommand).Assembly,
    CuentasPorPagarAssemblyMarker.Assembly,
    Millet.Almacen.Application.AssemblyMarker.Assembly,
    Millet.Facturacion.Application.AssemblyMarker.Assembly,
    CuentasPorCobrarAssemblyMarker.Assembly,
    TesoreriaAssemblyMarker.Assembly,
    CentrosCostoAssemblyMarker.Assembly);

// === Módulo Integraciones.Aw ===
// PR B foundation: IntegracionesAwOptions + repositorio + DbContext.
// PR C: HttpAwDropAdapter (named HttpClient "AwDropService"),
// HybridConnectionAwSqlReader (Microsoft.Data.SqlClient, reservado para
// integraciones futuras), AwDropWorker, healthcheck (singleton +
// IHostedService same instance — ver DependencyInjection del módulo).
// PR #199/#201: el flow per-EDI obtiene el outcome del drop service en
// la respuesta HTTP — sin polling SQL, sin AwCorrelationWorker.
builder.Services.AddIntegracionesAwModule(builder.Configuration);

// === Módulo Integraciones.Fiscal (PR-4) ===
// Registra MediatR handlers + FluentValidation validators + cliente HTTP
// del PAC + workers de descarga (deshabilitados por default hasta el
// cutover de PR-7). FiscalSecretCipher (PR-3) ya está registrado arriba
// como parte del wiring DataProtection.
builder.Services.AddIntegracionesFiscalModule(builder.Configuration);

// Adapter CxP del puerto inverso IFiscalCfdiReceiver (PR-12/PR-14): el
// DescargaPollerWorker (Fiscal) entrega cada CFDI cosechado (metadata +
// XML bytes pareados por UUID) a CxP, que parsea con IXmlCfdiParser,
// sube el XML a blob y crea un CfdiRecibido en estado PorProcesar.
// Sobreescribe el NoOpFiscalCfdiReceiver default del módulo Fiscal.
builder.Services.AddScoped<
    Millet.Integraciones.Fiscal.Domain.Ports.IFiscalCfdiReceiver,
    Millet.CuentasPorPagar.Infrastructure.PublicAdapters.FiscalCfdiReceiverAdapter>();

// === Módulo Almacén (F0-PR1) ===
// Foundation: registra los 9 puertos cross-module (Compras OC + Compras RQ +
// Artículo + Proveedor + Sucursal + Empleado + TipoCambio + ConceptoContable
// + PeriodoContable) con stubs NoOp* — cada uno con PLATFORM-TODO buscable.
// Las entidades del dominio (Almacen, MovimientoInventario, Saldo, Conteo,
// Reserva) entran en F1/F2/F3/F7. DbContext + outbox interceptor abajo.
builder.Services.AddAlmacenModule();

// === Almacén cross-module ports (read-side síncrono) → adapters reales ===
// Reemplaza los NoOpComprasOcReadPort / NoOpComprasRequisicionReadPort que
// registró AddAlmacenModule() por los adapters productivos que consultan
// ComprasDbContext directamente. Almacén no referencia Compras (bounded
// context limpio), pero Compras sí referencia Almacén — los adapters viven
// en Compras.Infrastructure.PublicAdapters y los wireamos aquí donde
// ambos módulos están disponibles.
//
// PLATFORM-TODO cerrados por este wireup:
// - <ComprasOcReadAdapter>: handlers de recepción ahora validan que la OC
//   esté Autorizada y resuelven costo unitario desde la línea de OC
//   (convertido a MXN si la OC es en moneda extranjera).
// - <ComprasRqReadAdapter>: handlers de salida ahora validan que la RQ
//   esté Autorizada / EnSurtido y leen articulo + cantidad solicitada por
//   línea para verificar match.
builder.Services.AddScoped<
    Millet.Almacen.Domain.Ports.IComprasOcReadPort,
    Millet.Compras.Infrastructure.PublicAdapters.ComprasOcReadAdapter>();
builder.Services.AddScoped<
    Millet.Almacen.Domain.Ports.IComprasRequisicionReadPort,
    Millet.Compras.Infrastructure.PublicAdapters.ComprasRequisicionReadAdapter>();
// ADR-0047 PR5.B: "vivo de origen sistema" agregado por (articulo, almacén) para el
// motor de reorden. Reemplaza NoOpComprasPedidoVivoReadPort de Almacén.
builder.Services.AddScoped<
    Millet.Almacen.Domain.Ports.IComprasPedidoVivoReadPort,
    Millet.Compras.Infrastructure.PublicAdapters.ComprasPedidoVivoReadAdapter>();
// ADR-0047 PR5.C: write-port que crea la RQ de sistema (motor de reorden). Reemplaza
// NoOpComprasCrearRqSistemaPort de Almacén.
builder.Services.AddScoped<
    Millet.Almacen.Domain.Ports.IComprasCrearRqSistemaPort,
    Millet.Compras.Infrastructure.PublicAdapters.ComprasCrearRqSistemaAdapter>();
// Fase E PR5: display del CC-Máquina en salidas. El adapter delega en
// CentrosCosto.IDim3ReadPort (Almacén no puede referenciar CentrosCosto —
// ciclo vía Compartido). Reemplaza NoOpCentroCostoReadPort de Almacén.
builder.Services.AddScoped<
    Millet.Almacen.Domain.Ports.ICentroCostoReadPort,
    Millet.Compras.Infrastructure.PublicAdapters.AlmacenCentroCostoReadAdapter>();

// Adapters DatosMaestros + Administracion → hosteados en Compartido (que ya
// referencia ambos módulos + Almacén). Reemplaza los stubs NoOp de Almacén
// para que los handlers validen contra el catálogo real.
//
// PLATFORM-TODO cerrados por este wireup:
// - <ArticuloReadAdapter>: handlers de recepción/salida leen UM canónica del
//   artículo desde compartido.articulos (lookup directo, sin copia local).
// - <ProveedorReadAdapter>: handler de devolución a proveedor (sub-flujo 8.B)
//   valida que el proveedor exista y esté activo antes de emitir el evento.
// - <SucursalReadAdapter>: validación de la jerarquía Sucursal → Almacén →
//   Sub-almacén al crear el catálogo de sub-almacenes (F1-PR1).
//
// - <EmpleadoReadAdapter>: catálogo compartido.empleados (ADM-PR2, doc
//   10-catalogo-puestos-empleados). Cierra el PLATFORM-TODO; Almacén ya no
//   registra NoOp para este puerto.
builder.Services.AddScoped<
    Millet.Almacen.Domain.Ports.IArticuloReadPort,
    Millet.Compartido.Infrastructure.PublicAdapters.ArticuloReadAdapter>();
builder.Services.AddScoped<
    Millet.Almacen.Domain.Ports.IEmpleadoReadPort,
    Millet.Compartido.Infrastructure.PublicAdapters.EmpleadoReadAdapter>();
builder.Services.AddScoped<
    Millet.Almacen.Domain.Ports.IProveedorReadPort,
    Millet.Compartido.Infrastructure.PublicAdapters.ProveedorReadAdapter>();

// Adapter CxP → hosteado en CuentasPorPagar.Infrastructure.PublicAdapters
// (CxP referencia Almacén vía Compras; Almacén no referencia CxP — bounded
// context limpio, mismo racional que los adapters de Compras arriba).
// Primer puerto de lectura Almacén → CxP: resuelve folios de factura/NC y
// UUID fiscal del CFDI para presentación (ADR-0042). Solo etiquetas — la
// conciliación de la triada sigue viajando exclusivamente por eventos.
builder.Services.AddScoped<
    Millet.Almacen.Domain.Ports.ICxpDocumentosReadPort,
    Millet.CuentasPorPagar.Infrastructure.PublicAdapters.CxpDocumentosReadAdapter>();

// ADR-0046 Etapa 2: validación de decimales por unidad. Puerto compartido
// (resuelve articuloId → decimales de su unidad, JOIN articulos⨝unidades_medida)
// + guard agnóstico de módulo que invocan los handlers de captura de cantidad.
// FK NULL → skip. Lo replicará Almacén en 2b reusando el mismo guard.
builder.Services.AddScoped<
    Millet.SharedKernel.Application.UnidadesMedida.IUnidadMedidaReadPort,
    Millet.Compartido.Infrastructure.PublicAdapters.UnidadMedidaReadAdapter>();
builder.Services.AddScoped<
    Millet.SharedKernel.Application.UnidadesMedida.IDecimalesUnidadGuard,
    Millet.SharedKernel.Application.UnidadesMedida.DecimalesUnidadGuard>();
builder.Services.AddScoped<
    Millet.Almacen.Domain.Ports.ISucursalReadPort,
    Millet.Compartido.Infrastructure.PublicAdapters.SucursalReadAdapter>();

// IUsuarioReadPort de Almacén → adapter hospedado en Identidad (ADR-0042).
// Reemplaza el NoOpUsuarioReadPort que registró AddAlmacenModule(). El adapter
// NO vive en Almacén ni en Compartido: ambos cerrarían ciclo con Identidad,
// que ya los referencia (seed de permisos canónicos). Lo hospeda el owner del
// dato (Identidad ya referencia Almacen.Domain, así que implementar su puerto
// no agrega acoplamiento). Resuelve el nombre de la persona destinataria de
// una salida desde identidad.usuarios.
builder.Services.AddScoped<
    Millet.Almacen.Domain.Ports.IUsuarioReadPort,
    Millet.Identidad.Infrastructure.PublicAdapters.UsuarioReadAdapter>();

// ADR-0047 PR5.C: resuelve el usuario de servicio del reorden (creador/requisitante +
// empresa de las RQ automáticas). Adapter hospedado en Identidad (dueño del dato).
builder.Services.AddScoped<
    Millet.Almacen.Domain.Ports.IUsuarioServicioReadPort,
    Millet.Identidad.Infrastructure.PublicAdapters.UsuarioServicioReadAdapter>();

// IUsuarioSucursalReadPort (F1-ADM-01 Fase 2): guard de pertenencia a
// sucursal (SucursalScopeGuard) para los handlers "listar X de una
// sucursal" de Compartido/Administracion. El adapter vive en Identidad
// (owner de UsuarioSucursal) porque Compartido no puede referenciar
// Identidad (cerraría ciclo).
builder.Services.AddScoped<
    Millet.Administracion.Application.Abstractions.IUsuarioSucursalReadPort,
    Millet.Identidad.Infrastructure.PublicAdapters.UsuarioSucursalReadAdapter>();

// IRolReadPort (F1-ADM-01.4): lectura cross-módulo de roles para validación
// de RolSugeridoId en Puesto. Adapter hospedado en Identidad.
builder.Services.AddScoped<
    Millet.Administracion.Application.Abstractions.IRolReadPort,
    Millet.Identidad.Infrastructure.PublicAdapters.RolReadAdapter>();

// === Módulo Cuentas por Pagar (F0-PR1) ===
// Foundation: registra los 10 puertos cross-module con stubs NoOp* + DbContext
// (más abajo, junto a los demás contextos). Workers de ingestión (FiscalAPI,
// mailbox, SLA) entran en F2/F4.
builder.Services.AddCuentasPorPagarModule(builder.Configuration);

// === Módulo Facturación (F0-PR1) ===
// Foundation: registra los puertos de lectura del 01-diseño §6.1 —
// ICatalogosSatReadPort con adapter real sobre CompartidoDbContext, el
// timbrado real ICfdiTimbradoPort → FiscalApiTimbradoAdapter (incondicional
// desde F12-PR3; el flag Facturacion:Timbrado:UsarPacReal se eliminó) y el
// resto con adapter real o stub NoOp*/Stub* (PLATFORM-TODO buscable).
// DbContext + outbox interceptor abajo. Agregados de negocio (Comprobante
// TPT, FacturaVenta, anticipos, NC, Carta Porte, REPP) entran en F1+.
builder.Services.AddFacturacionModule(builder.Configuration);

// === Módulo Cuentas por Cobrar (CXC-PR1) ===
// Foundation: LineaCredito + DbContext (abajo, junto a los demás contextos)
// + outbox. Sin ports todavía — IClienteReadPort/IFacturacionAnticiposReadPort
// entran con sus consumidores (CXC-PR3+). Ver 01-diseño §6.1.
builder.Services.AddCuentasPorCobrarModule(builder.Configuration);

// === Módulo Tesorería (TES-PR1) ===
// Foundation: esquema `tesoreria` + DbContext (abajo, junto a los demás
// contextos) + outbox table. Sin ports ni workers todavía —
// IProveedorBancoReadPort entra en TES-PR3, el OutboxPublisherWorker y
// el publisher de los 4 eventos espejo congelados en TES-PR4. Ver
// docs/modulos/tesoreria/01-diseno.md §6.
builder.Services.AddTesoreriaModule(builder.Configuration);

// === Módulo Centros de Costo (CECO-A1) ===
// Cimiento: catálogo jerárquico Sucursal→Departamento→Equipo + dimensiones
// Grupo/Subgrupo + adapter de ISucursalReadPort (validación del vínculo al
// catálogo general). DbContext abajo, junto a los demás contextos. Sin
// endpoints ni outbox todavía — CRUD/jerarquía entran en A2/A3.
builder.Services.AddCentrosCostoModule();

// === Flujo 2 de Integraciones.Aw: ingesta de pedidos en firme (ADR-0048) ===
// Adapters reales del reader/write-back sobre MILLET_INTEGRACION (tabla-puente
// + vistas, docs/integration/04). DEBE ir DESPUÉS de AddFacturacionModule
// (last-registration-wins pisa los stubs de F3). Toggle por presencia de la
// connection string — sin ella (dev local sin HC) quedan los stubs y el
// worker de ingesta sigue inerte (candados: EmpresaId vacío + Disabled).
// Nota: si el secreto KV no existe, App Service pasa la ref SIN resolver como
// string literal "@Microsoft.KeyVault(...)" — se trata como ausente.
var awIntegracionDb = builder.Configuration.GetConnectionString("AwIntegracionDb");
if (!string.IsNullOrWhiteSpace(awIntegracionDb)
    && !awIntegracionDb.StartsWith("@Microsoft.KeyVault", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddIntegracionesAwPedidosAdapters(builder.Configuration);
}

// F2-PR2: worker de envío de CFDI por correo (drena bitacora_envio_correo,
// genera PDF + adjunta XML, entrega vía INotificacionService [stub]).
builder.Services
    .AddOptions<Millet.Facturacion.Infrastructure.Workers.EnvioCfdiCorreoOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Facturacion.Infrastructure.Workers.EnvioCfdiCorreoOptions.SectionName));
builder.Services.AddHostedService<Millet.Facturacion.Infrastructure.Workers.EnvioCfdiCorreoWorker>();

// F3-PR1b: worker de ingesta A+W (consume la cola aw_solicitud_pedido vía
// IAwSolicitudesReader [stub en dev] y aplica la matriz operación×estado).
builder.Services
    .AddOptions<Millet.Facturacion.Infrastructure.Workers.AwSolicitudesOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Facturacion.Infrastructure.Workers.AwSolicitudesOptions.SectionName));
builder.Services.AddHostedService<Millet.Facturacion.Infrastructure.Workers.AwSolicitudesWorker>();

// PR5 (ADR-0048 D3): worker que drena los write-backs pendientes de
// ingesta_control hacia la tabla-puente (uuid+estado al timbrar/cancelar y
// reintentos del write-back síncrono que falló). Inerte con los stubs (sin
// ingesta real no hay pendientes); real con el toggle AwIntegracionDb.
builder.Services
    .AddOptions<Millet.Facturacion.Infrastructure.Workers.AwWriteBackOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Facturacion.Infrastructure.Workers.AwWriteBackOptions.SectionName));
builder.Services.AddHostedService<Millet.Facturacion.Infrastructure.Workers.WriteBackResultadoWorker>();

// F5-PR2: poller de cancelaciones SAT (resuelve las SolicitudCancelacion
// EnProceso consultando el estatus del CFDI; stub reporta vigente en dev).
builder.Services
    .AddOptions<Millet.Facturacion.Infrastructure.Workers.CancelacionSatPollerOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Facturacion.Infrastructure.Workers.CancelacionSatPollerOptions.SectionName));
builder.Services.AddHostedService<Millet.Facturacion.Infrastructure.Workers.CancelacionSatPollerWorker>();

// F12-PR2: worker de timbres pendientes (resuelve comprobantes atascados en
// TimbradoEnProceso — resultado ambiguo del PAC real — como TimbradoFallido
// PAC_TIMEOUT, corregible; NUNCA re-timbra automático).
builder.Services
    .AddOptions<Millet.Facturacion.Infrastructure.Workers.TimbradoPendienteOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Facturacion.Infrastructure.Workers.TimbradoPendienteOptions.SectionName));
builder.Services.AddHostedService<Millet.Facturacion.Infrastructure.Workers.TimbradoPendienteWorker>();

// F7-PR2: worker de pedimentos del Sistema de Salidas (empareja Hoja de Salida ↔
// pedimento vía ISalidasPedimentosReader [stub en dev] y aplica a las facturas
// retenidas en PendientePedimento).
builder.Services
    .AddOptions<Millet.Facturacion.Infrastructure.Workers.PedimentoSalidasOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Facturacion.Infrastructure.Workers.PedimentoSalidasOptions.SectionName));
builder.Services.AddHostedService<Millet.Facturacion.Infrastructure.Workers.PedimentoSalidasWorker>();

// F10-PR2: worker de ingesta de Planta Pintura (pull de órdenes vía
// IPlantaPinturaPedidosReader [stub en dev]; exige master preexistente).
builder.Services
    .AddOptions<Millet.Facturacion.Infrastructure.Workers.PlantaPinturaImportOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Facturacion.Infrastructure.Workers.PlantaPinturaImportOptions.SectionName));
builder.Services.AddHostedService<Millet.Facturacion.Infrastructure.Workers.PlantaPinturaImportWorker>();

// IntegracionesAwHubNotifier vive en Api/ (depende de IHubContext) — el
// puerto IIntegracionesAwNotifier vive en Aw.Application. Los workers
// dependen del puerto, evitando un ciclo de proyectos.
builder.Services.AddSingleton<
    Millet.Integraciones.Aw.Application.Ports.IIntegracionesAwNotifier,
    Millet.Api.Hubs.IntegracionesAwHubNotifier>();

// === Identidad — EntraId resolver port (F-Admin-PR4.1) ===
// Stub NoOp por defecto: retorna null y deja al handler de CrearUsuario
// caer al placeholder dev-{email} (ADR-0015). Wiring real al Microsoft
// Graph API es post-MVP — ver PLATFORM-TODO(<EntraIdResolver>) en
// LocalEntraIdResolverNoOp.cs.
builder.Services.AddScoped<
    Millet.Identidad.Application.Ports.IEntraIdResolverPort,
    Millet.Identidad.Infrastructure.Stubs.LocalEntraIdResolverNoOp>();

// === Identidad — directorio Entra para el alta unificada (plan 15, F2) ===
// Simulación por defecto. Graph es opt-in explícito y exige configuración
// completa; nunca crea usuarios de prueba en un tenant por accidente.
builder.Services
    .AddOptions<Millet.Identidad.Application.DirectorioEntra.EntraDirectorioOptions>()
    .Bind(builder.Configuration.GetSection(
        Millet.Identidad.Application.DirectorioEntra.EntraDirectorioOptions.SectionName));
// Transacción compartida Identidad + Compartido del alta de colaborador
// (plan 15, F3; validada en el spike F0).
builder.Services.AddScoped<Millet.Identidad.Infrastructure.TransaccionColaborador>();
// Camino B "Cuenta Microsoft nueva" (plan 15, F4): el sandbox de Development
// captura el correo solo en loopback; fuera de él el worker sigue apagado por
// defecto hasta configurar adaptadores reales.
var proveedorColaboradores = builder.Configuration["Entra:Proveedor"] ?? "Simulado";
if (proveedorColaboradores.Equals("Graph", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddGraphColaboradores(builder.Configuration);
else if (proveedorColaboradores.Equals("Simulado", StringComparison.OrdinalIgnoreCase))
{
    var provisionSimuladaActiva = !builder.Configuration.GetValue<bool>("Entra:Provision:Disabled");
    if (provisionSimuladaActiva)
    {
        var sandbox = builder.Configuration.GetSection("Entra:Simulacion:CorreoSandbox")
            .Get<Millet.Identidad.Application.DirectorioEntra.CorreoSandboxOptions>() ?? new();
        var esLoopback = sandbox.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || sandbox.Host == "127.0.0.1" || sandbox.Host == "::1";
        if (!builder.Environment.IsDevelopment() || authMode != AuthMode.FakeForLocalDev
            || !esLoopback || sandbox.Puerto is < 1 or > 65535)
            throw new InvalidOperationException(
                "La provisión simulada solo se permite en Development, con FakeForLocalDev y SMTP de captura en loopback.");
        builder.Services.AddSingleton<Millet.Identidad.Application.Ports.ICorreoSalientePort,
            Millet.Api.Auth.Provisioning.CorreoSandboxLocal>();
    }
    else
    {
        builder.Services.AddSingleton<Millet.Identidad.Application.Ports.ICorreoSalientePort,
            Millet.Identidad.Infrastructure.Stubs.CorreoSalienteSimulado>();
    }
    builder.Services.AddSingleton<
        Millet.Identidad.Application.Ports.IEntraDirectorioPort,
        Millet.Identidad.Infrastructure.Stubs.DirectorioEntraSimulado>();
}
else throw new InvalidOperationException("Entra:Proveedor debe ser Simulado o Graph.");
builder.Services.AddHostedService<Millet.Identidad.Infrastructure.Workers.ProvisionCuentaEntraWorker>();

// === OpenAPI / Scalar (ADR-0017, F0-PR2) ===
// AddOpenApi("v1") registra el generador de Microsoft.AspNetCore.OpenApi
// (sucesor de Swashbuckle en .NET 9). El doc transformer fija Title y
// Version del documento; convenciones por endpoint (Tags,
// ProducesResponseType, XML doc comments) se aplican cuando los módulos
// las introduzcan.
//
// La spec se monta en el pipeline solo en Development/Staging
// (ver bloque MapOpenApi/MapScalarApiReference más abajo).
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Millet ERP API";
        document.Info.Version = "v1";
        return Task.CompletedTask;
    });
});

// === Auth wiring (ADR-0003, ADR-0007, ADR-0015) ===
// Registra options bindeadas (Auth:Jwt, Auth:EntraId), JwtBearer scheme,
// IHttpContextAccessor (para que CurrentUserContext lea claims),
// IJwtTokenService, IEntraTokenValidator, LoginOrchestrator.
builder.Services.AddMilletAuth(builder.Configuration);

// === PR-A2 Lote B: ComprasTestSeed (folio_secuencias adelantada) ===
// Se registra DESPUÉS de AddMilletAuth porque ese registra
// BootstrapSuperAdminHostedService — que crea la empresa-bootstrap a la
// que este seed apunta. ASP.NET arranca hosted services en orden de
// registro y de forma secuencial, así que cuando este seed corre la
// empresa ya existe. Auto-excluido en Production.
builder.Services.AddHostedService<
    Millet.Compras.Infrastructure.Seed.ComprasTestSeedHostedService>();

// === SignalR + Azure SignalR backplane (CollaborationHub, ADR-0001 + ADR-0012 Capa 2) ===
// En QA/Prod la connection string viene de Key Vault (App Setting
// SignalR__ConnectionString). En dev local sin la setting cae a in-process
// backplane — funciona con un solo nodo de App Service y permite probar el
// flujo sin provisionar Azure SignalR Service.
var signalrConn = builder.Configuration["SignalR:ConnectionString"];
var signalrBuilder = builder.Services.AddSignalR();
if (!string.IsNullOrWhiteSpace(signalrConn))
{
    signalrBuilder.AddAzureSignalR(signalrConn);
}

// === Soft locks de Capa 2 (CollaborationHub Sprint 2) ===
// El manager singleton mantiene el mapa connectionId → SoftLockEntry en
// memoria del proceso del hub. El expiration worker barre cada N segundos
// las entries con LastSeen vencido (default 90s) y publica userPresence
// al grupo de la empresa para que el FE retire al usuario.
//
// ComprasHubMeter (Sprint 3): Meter singleton que emite métricas custom
// del hub (softlock.tracked/released/expired, hub.connections.active).
// Las exporta el AzureMonitor OTel Distro a App Insights, vía AddMeter
// en el bloque WithMetrics del bootstrap OpenTelemetry abajo.
builder.Services
    .AddOptions<Millet.Api.Hubs.SoftLockOptions>()
    .Bind(builder.Configuration.GetSection(Millet.Api.Hubs.SoftLockOptions.SectionName));
builder.Services.AddSingleton<Millet.Api.Hubs.ComprasHubMeter>();
builder.Services.AddSingleton<Millet.Api.Hubs.ISoftLockManager, Millet.Api.Hubs.SoftLockManager>();
builder.Services.AddHostedService<Millet.Api.Hubs.SoftLockExpirationWorker>();

// === CORS ===
// El frontend SPA vive en otro hostname (Static Web Apps). Cross-origin
// requests requieren preflight OPTIONS exitoso. Origins permitidos vienen
// de Cors:AllowedOrigins:N (Bicep los puebla con el hostname del SWA).
// En dev local: appsettings.Development.json incluye http://localhost:5173.
const string MilletCorsPolicy = "MilletFrontend";
var corsAllowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy(MilletCorsPolicy, policy =>
    {
        if (corsAllowedOrigins.Length > 0)
        {
            policy.WithOrigins(corsAllowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials()
                  // Sin esto el browser NO puede leer ETag cross-origin
                  // (Access-Control-Expose-Headers) y el If-Match de las
                  // mutaciones nunca se envía (ADR-0012).
                  .WithExposedHeaders(Microsoft.Net.Http.Headers.HeaderNames.ETag);
        }
        // Si AllowedOrigins está vacío, la policy queda sin orígenes
        // permitidos — preflight siempre falla. Comportamiento secure-by-default.
    });
});

// === DataProtection (ADR-0037) ===
// Cifra credenciales operativas admin-configurables (ej. ApiKey de
// FiscalAPI en integraciones_fiscal.configuracion_pac) que viven en
// columnas Postgres normales como bytea. La DEK vive en Key Vault; el
// ring de keys que rota cada 90d vive en Azure Blob Storage.
//
// Dev local: si DataProtection:KeyIdentifier o :BlobConnString están
// vacíos (no configurados en appsettings.Development.json), DataProtection
// cae a filesystem en %LOCALAPPDATA%\ASP.NET\DataProtection-Keys
// automáticamente. Cero fricción para desarrolladores sin Azure.
//
// QA/Prod: ambos valores vienen de Bicep como app settings:
//   DataProtection__BlobConnString → KV ref storage-connection-string
//   DataProtection__KeyIdentifier  → URI versionado de
//     https://kv-millet-{env}-mxc-01.vault.azure.net/keys/dataprotection-master-key/{version}
// Managed Identity del App Service tiene "Key Vault Crypto User" sobre la
// key + "Storage Blob Data Contributor" sobre el container.
{
    var dpBuilder = builder.Services.AddDataProtection()
        .SetApplicationName("Millet.ERP")
        .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

    var dpBlobConnString = builder.Configuration["DataProtection:BlobConnString"];
    var dpKeyIdentifier = builder.Configuration["DataProtection:KeyIdentifier"];

    if (!string.IsNullOrWhiteSpace(dpBlobConnString)
        && !string.IsNullOrWhiteSpace(dpKeyIdentifier))
    {
        dpBuilder
            .PersistKeysToAzureBlobStorage(
                dpBlobConnString,
                containerName: "dataprotection-keys",
                blobName: "millet-erp.xml")
            .ProtectKeysWithAzureKeyVault(
                new Uri(dpKeyIdentifier),
                new Azure.Identity.DefaultAzureCredential());
    }
    // else: fallback a filesystem (DataProtection lo hace por default).
}

// Cipher del ApiKey de FiscalAPI (purpose "Integraciones.Fiscal.ApiKey.v1").
// Singleton — IDataProtector es thread-safe; la única dependencia mutable
// es el ring de keys que ASP.NET gestiona internamente.
builder.Services.AddSingleton<
    Millet.Integraciones.Fiscal.Infrastructure.Cifrado.FiscalSecretCipher>();

// === Interceptors EF Core ===
builder.Services.AddScoped<MetadataSaveChangesInterceptor>();
builder.Services.AddScoped<EmpresaContextSaveChangesInterceptor>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();

// === DbContexts ===
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Falta la cadena de conexión 'Postgres' en configuración.");

void ConfigureMilletDbContext(DbContextOptionsBuilder opts, IServiceProvider sp)
{
    opts.UseNpgsql(connectionString);
    opts.UseSnakeCaseNamingConvention();
    opts.AddInterceptors(
        sp.GetRequiredService<MetadataSaveChangesInterceptor>(),
        sp.GetRequiredService<EmpresaContextSaveChangesInterceptor>(),
        sp.GetRequiredService<AuditSaveChangesInterceptor>());
}

builder.Services.AddDbContext<CompartidoDbContext>((sp, opts) =>
{
    ConfigureMilletDbContext(opts, sp);
    opts.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
});
builder.Services.AddDbContext<CoreDbContext>((sp, opts) => ConfigureMilletDbContext(opts, sp));
builder.Services.AddDbContext<IdentidadDbContext>((sp, opts) => ConfigureMilletDbContext(opts, sp));

// Compras + Integraciones.Aw consumen IIntegrationEventPublisher
// (F6-PR1, PR B). Cada uno necesita el OutboxSaveChangesInterceptor
// adicional para que los integration events del scoped buffer se
// drenen a la tabla integration_events_outbox de su propio schema
// dentro de la misma TX que SaveChanges (entrega atómica).
builder.Services.AddDbContext<ComprasDbContext>((sp, opts) =>
{
    ConfigureMilletDbContext(opts, sp);
    opts.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
});

builder.Services.AddDbContext<Millet.Integraciones.Aw.Infrastructure.Persistence.IntegracionesAwDbContext>(
    (sp, opts) =>
    {
        ConfigureMilletDbContext(opts, sp);
        opts.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
    });

// Integraciones.Fiscal (PR-2 foundation): DbContext + outbox interceptor
// para que IIntegrationEventPublisher escriba a
// integraciones_fiscal.integration_events_outbox dentro de la TX EF
// (ADR-0009). Endpoints + workers entran en PRs siguientes.
builder.Services.AddDbContext<Millet.Integraciones.Fiscal.Infrastructure.Persistence.IntegracionesFiscalDbContext>(
    (sp, opts) =>
    {
        ConfigureMilletDbContext(opts, sp);
        opts.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
    });

// Almacén (F0-PR1): DbContext + outbox interceptor para que
// IIntegrationEventPublisher escriba a almacen.integration_events_outbox
// dentro de la TX EF (atomicidad — ADR-0009).
builder.Services.AddDbContext<AlmacenDbContext>((sp, opts) =>
{
    ConfigureMilletDbContext(opts, sp);
    opts.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
});

// CuentasPorPagar (F0-PR1): mismo patrón — DbContext + outbox interceptor
// para que IIntegrationEventPublisher escriba a
// cuentas_por_pagar.integration_events_outbox dentro de la TX EF.
builder.Services.AddDbContext<CuentasPorPagarDbContext>((sp, opts) =>
{
    ConfigureMilletDbContext(opts, sp);
    opts.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
});

// Facturación (F0-PR1): mismo patrón — DbContext + outbox interceptor para
// que IIntegrationEventPublisher escriba a
// facturacion.integration_events_outbox dentro de la TX EF (ADR-0009).
builder.Services.AddDbContext<FacturacionDbContext>((sp, opts) =>
{
    ConfigureMilletDbContext(opts, sp);
    opts.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
});

// Cuentas por Cobrar (CXC-PR1): mismo patrón — DbContext + outbox
// interceptor sobre cuentas_por_cobrar.integration_events_outbox (ADR-0009).
builder.Services.AddDbContext<CuentasPorCobrarDbContext>((sp, opts) =>
{
    ConfigureMilletDbContext(opts, sp);
    opts.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
});

// Tesorería (TES-PR1): mismo patrón — DbContext + outbox interceptor
// sobre tesoreria.integration_events_outbox (ADR-0009). El worker que
// drena hacia el topic `tesoreria-events` se registra en TES-PR4.
builder.Services.AddDbContext<TesoreriaDbContext>((sp, opts) =>
{
    ConfigureMilletDbContext(opts, sp);
    opts.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
});

// Centros de Costo (CECO-A1): sin outbox interceptor — el módulo no emite
// eventos de integración todavía (se agrega cuando Contabilidad consuma el
// catálogo, ADR-0009).
builder.Services.AddDbContext<CentrosCostoDbContext>((sp, opts) =>
    ConfigureMilletDbContext(opts, sp));

// === Manejo de errores: Problem Details vía IExceptionHandler ===
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// === Health checks ===
// Postgres + migrations applied + SignalR hub wiring (CollaborationHub
// Sprint Buffer). Service Bus + Key Vault + App Insights checks pendientes
// para PRs separados. Ver ADR-0019.
//
// La lista de DbContexts a chequear vive aquí (no en SharedKernel) porque
// SharedKernel no conoce los módulos. Cada módulo nuevo se agrega a esta
// lista cuando entre.
builder.Services.AddSingleton(new MigrationsHealthCheckOptions
{
    ContextTypes =
    {
        typeof(CompartidoDbContext),
        typeof(CoreDbContext),
        typeof(IdentidadDbContext),
        typeof(ComprasDbContext),
        typeof(Millet.Integraciones.Aw.Infrastructure.Persistence.IntegracionesAwDbContext),
        typeof(Millet.Integraciones.Fiscal.Infrastructure.Persistence.IntegracionesFiscalDbContext),
        typeof(AlmacenDbContext),
        typeof(CuentasPorPagarDbContext),
        typeof(FacturacionDbContext),
        typeof(CuentasPorCobrarDbContext),
        typeof(TesoreriaDbContext),

        typeof(CentrosCostoDbContext),
    },
});

builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString,
        name: "postgres",
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(2))
    .AddCheck<MigrationsAppliedHealthCheck>(
        "migrations_applied",
        tags: ["ready", "startup"])
    .AddCheck<Millet.Api.Hubs.SignalRHealthCheck>(
        "signalr_hub",
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(1));

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseExceptionHandler();

// CORS DEBE correr antes de Authentication: los preflight OPTIONS son
// anonymous y requieren response sin auth challenge. Si Authentication
// fuera primero, el browser recibiría 401 en el preflight y nunca enviaría
// la request real.
app.UseCors(MilletCorsPolicy);

// Middleware de autenticación + autorización. DEBE correr antes de los
// endpoints (el routing se inserta automáticamente entre estos y el
// MapXxx). El JwtBearer middleware valida el JWT del API y popula
// HttpContext.User para que CurrentUserContext lea claims.
app.UseAuthentication();
app.UseAuthorization();

// === RequestContextLoggingMiddleware (F8-PR2, ADR-0006) ===
// Pushea EmpresaId / UsuarioId al LogContext de Serilog para que todos
// los logs subsiguientes (idempotency, handlers, EF) los incluyan como
// custom dimensions.
app.UseMiddleware<RequestContextLoggingMiddleware>();

// === Idempotency middleware (F8-PR1, ADR-0020) ===
// Después de Authentication+Authorization para tener empresa+usuario en
// HttpContext. Antes de los endpoints para que el atributo
// [RequireIdempotencyKey] del endpoint resuelto sea visible.
app.UseMiddleware<IdempotencyMiddleware>();

app.MapGet("/", () => "Hello World!");

// === Auth endpoints (ADR-0003, ADR-0007) ===
app.MapAuthEndpoints();
#if DEBUG
// Compilación condicional: en Release este código no existe (ADR-0015).
app.MapDevAuthEndpoints();
// Solo el endpoint auxiliar de idempotencia permanece limitado a Debug.
app.MapDevIdempotencyEndpoints();
#endif

// === Compras endpoints (F1-PR2, F2-PR2) ===
app.MapRequisicionesEndpoints();
app.MapLineasEndpoints();
app.MapAprobadoresEndpoints();
app.MapComprasSettingsEndpoints();

// === Settings genéricos /api/v1/{modulo}/settings (F-Admin-PR1.1, ADR-0034) ===
app.MapSettingsEndpoints();

// === Administración — smoke del andamio (F-Admin-PR1.2) ===
Millet.Api.Endpoints.Administracion.SmokeEndpoint.MapAdminSmokeEndpoint(app);

// === Almacén — smoke de la fundación (F0-PR1) ===
Millet.Api.Endpoints.Almacen.SmokeEndpoint.MapAlmacenSmokeEndpoint(app);

// === Almacén — CRUD catálogo Almacén/SubAlmacén (F1-PR1) ===
Millet.Api.Endpoints.Almacen.Catalogo.AlmacenCatalogoEndpoints.MapAlmacenCatalogoEndpoints(app);

// === Centros de Costo — CRUD del catálogo (CECO-PR2) ===
Millet.Api.Endpoints.CentrosCosto.CentrosCostoCatalogoEndpoints.MapCentrosCostoCatalogoEndpoints(app);

// === Centros de Costo — jerarquía lazy + búsqueda del selector (CECO-PR3) ===
Millet.Api.Endpoints.CentrosCosto.CentrosCostoJerarquiaEndpoints.MapCentrosCostoJerarquiaEndpoints(app);

// === Centros de Costo — alcance usuario→máquinas (CECO-PR6) ===
Millet.Api.Endpoints.CentrosCosto.CentrosCostoAsignacionesEndpoints.MapCentrosCostoAsignacionesEndpoints(app);

// === Almacén — CRUD asignación artículo→ubicación (OITW, ADR-0047 PR3) ===
Millet.Api.Endpoints.Almacen.Asignaciones.AlmacenAsignacionesEndpoints.MapAlmacenAsignacionesEndpoints(app);

// === Almacén — CRUD configuración de reorden N1/N2 (ADR-0047 PR5.A) ===
Millet.Api.Endpoints.Almacen.Reorden.AlmacenReordenEndpoints.MapAlmacenReordenEndpoints(app);

// === Almacén — Configuración del módulo (interruptor de reabasto automático) ===
Millet.Api.Endpoints.Almacen.AlmacenSettingsEndpoints.MapAlmacenSettingsEndpoints(app);

// === Almacén — Recepciones Variante A (F2-PR2) + Saldos materializados ===
Millet.Api.Endpoints.Almacen.Recepciones.RecepcionesEndpoints.MapRecepcionesEndpoints(app);
Millet.Api.Endpoints.Almacen.Saldos.SaldosEndpoints.MapSaldosEndpoints(app);

// === Almacén — Upload de packing list (F2-PR4, dependency de Variante B) ===
Millet.Api.Endpoints.Almacen.Recepciones.PackingListBlobEndpoints.MapPackingListBlobEndpoints(app);

// === Almacén — Salidas (F4-PR1) ===
Millet.Api.Endpoints.Almacen.Salidas.SalidasEndpoints.MapSalidasEndpoints(app);
// Fase E PR5: selector abierto de CC-Máquina para la línea de vale (proxy, gateado por-vale).
Millet.Api.Endpoints.Almacen.Salidas.Dim3BuscarAbiertoSalidaEndpoint.MapDim3BuscarAbiertoSalidaEndpoint(app);

// === Almacén — Vales urgentes (F5-PR1) ===
Millet.Api.Endpoints.Almacen.Vales.ValesEndpoints.MapValesEndpoints(app);
// Upload del archivo del vale (dependency de Variante B de salidas).
Millet.Api.Endpoints.Almacen.Vales.ValeBlobEndpoints.MapValeBlobEndpoints(app);

// === Almacén — Devoluciones internas 8.A + MAT-REV (F5-PR1) ===
Millet.Api.Endpoints.Almacen.DevolucionesInternas.DevolucionesInternasEndpoints
    .MapDevolucionesInternasEndpoints(app);

// === Almacén — Devoluciones a proveedor 8.B (F6-PR1) ===
Millet.Api.Endpoints.Almacen.DevolucionesProveedor.DevolucionesProveedorEndpoints
    .MapDevolucionesProveedorEndpoints(app);
// Upload de evidencias (dependency del flujo de Autorización 8.B).
Millet.Api.Endpoints.Almacen.DevolucionesProveedor.EvidenciaBlobEndpoints
    .MapEvidenciaBlobEndpoints(app);

// === Almacén — Inventario físico / conteos (F7-PR1 + F7-PR2) ===
Millet.Api.Endpoints.Almacen.Conteos.ConteosEndpoints.MapConteosEndpoints(app);
Millet.Api.Endpoints.Almacen.Conteos.AprobacionEndpoints.MapAprobacionConteoEndpoints(app);

// === Almacén — Reportes operativos (F8-PR1) ===
Millet.Api.Endpoints.Almacen.Reportes.ReportesEndpoints.MapReportesEndpoints(app);

// === Almacén — Cierre de mes (F8-PR2) ===
Millet.Api.Endpoints.Almacen.Cierre.CierreEndpoints.MapCierreEndpoints(app);

// === Administración — CRUD Empresas + Sucursales + Departamentos (F-Admin-PR2.3) ===
Millet.Api.Endpoints.Administracion.EmpresasEndpoints.MapEmpresasEndpoints(app);
Millet.Api.Endpoints.Administracion.DepartamentosEndpoints.MapDepartamentosEndpoints(app);
// ADM-PR1 (doc 10-catalogo-puestos-empleados): master de puestos y empleados.
Millet.Api.Endpoints.Administracion.PuestosEndpoints.MapPuestosEndpoints(app);
Millet.Api.Endpoints.Administracion.EmpleadosEndpoints.MapEmpleadosEndpoints(app);
Millet.Api.Endpoints.Administracion.ColaboradoresEndpoints.MapColaboradoresEndpoints(app);
// FAC-ING-PR2: catálogo administrable de canales de venta (mismo permiso
// que sucursales).
Millet.Api.Endpoints.Administracion.CanalesVentaEndpoints.MapCanalesVentaEndpoints(app);
// PR-A1: asignación N:M Sucursal ↔ Departamento.
Millet.Api.Endpoints.Administracion.SucursalDepartamentosEndpoints
    .MapSucursalDepartamentosEndpoints(app);
// F1-ADM-01 Fase 2: asignación N:M Sucursal ↔ Puesto y Usuario ↔ Sucursal
// (scoping de catálogos y usuarios por sucursal).
Millet.Api.Endpoints.Administracion.SucursalPuestosEndpoints
    .MapSucursalPuestosEndpoints(app);
Millet.Api.Endpoints.Administracion.SucursalUsuariosEndpoints
    .MapSucursalUsuariosEndpoints(app);

// === Administración — Series y Folios (F-Admin-PR6.1) ===
Millet.Api.Endpoints.Administracion.SeriesEndpoints.MapSeriesEndpoints(app);

// === Administración — Parámetros globales + Auditoría UI (F-Admin-PR7) ===
Millet.Api.Endpoints.Administracion.ParametrosEndpoints.MapParametrosEndpoints(app);
Millet.Api.Endpoints.Administracion.AuditoriaEndpoints.MapAuditoriaEndpoints(app);

// === Compras — Órdenes de Compra (OC F1-PR2) ===
Millet.Api.Endpoints.Compras.Oc.OrdenesCompraEndpoints.MapOrdenesCompraEndpoints(app);
Millet.Api.Endpoints.Compras.Oc.Dim3BuscarAbiertoEndpoint.MapDim3BuscarAbiertoEndpoint(app);

// === Compras — Trazabilidad cross-módulo (F7-PR2) ===
Millet.Api.Endpoints.Compras.Trazabilidad.ArbolDocumentosEndpoint.MapArbolDocumentosEndpoint(app);

// === Compras — Historial de compras por artículo (F7-PR3) ===
Millet.Api.Endpoints.Compras.Articulos.HistorialComprasMaterialEndpoint.MapHistorialComprasMaterialEndpoint(app);

// === Catálogos compartidos (F7-PR1, B.1) ===
app.MapCatalogosEndpoints();
app.MapOrganizacionEndpoints();

// === Catálogos OC (F9-PR1) ===
Millet.Api.Endpoints.Catalogos.CatalogosOcEndpoints.MapCatalogosOcEndpoints(app);

// === Catálogos administrables (F-Admin-PR5.1/5.2/5.3) ===
// Monedas + TiposCambio (PR5.1)
Millet.Api.Endpoints.Catalogos.MonedasEndpoints.MapMonedasEndpoints(app);
// CRUD de catálogos editables (PR5.2): CondicionesPago, Incoterms, Transportistas, UsosPrincipales
Millet.Api.Endpoints.Catalogos.CatalogosEditablesEndpoints.MapCatalogosEditablesEndpoints(app);
// Catálogos SAT read-only (PR5.3): FormasPago, UsosCfdi
Millet.Api.Endpoints.Catalogos.CatalogosSatEndpoints.MapCatalogosSatEndpoints(app);
// Catálogos SAT en vivo vía FiscalAPI (FAC-DET-PR1): ClaveProdServ, ClaveUnidad, ObjetoImp
Millet.Api.Endpoints.Catalogos.CatalogosSatFiscalApiEndpoints.MapCatalogosSatFiscalApiEndpoints(app);

// === Datos Maestros — queries enriquecidas (F-Admin-PR4.5) ===
Millet.Api.Endpoints.DatosMaestros.DatosMaestrosEndpoints.MapDatosMaestrosEndpoints(app);

// === Catálogo de tipos de documento OC (UF3-PR2 — Compras-specific) ===
Millet.Api.Endpoints.Compras.Oc.TiposDocumentoOcEndpoint.MapTiposDocumentoOcEndpoint(app);

// === Identidad: usuarios catalog para selectores de UI (B.1) ===
Millet.Api.Endpoints.Identidad.UsuariosEndpoints.MapUsuariosEndpoints(app);
Millet.Api.Endpoints.Identidad.DirectorioEntraEndpoints.MapDirectorioEntraEndpoints(app);

// === Identidad: CRUD Roles + matriz de permisos + grupos Entra ID (F-Admin-PR3.2) ===
Millet.Api.Endpoints.Identidad.RolesEndpoints.MapRolesEndpoints(app);
Millet.Api.Endpoints.Identidad.PermisosEndpoints.MapPermisosEndpoints(app);

// === Cuentas por Pagar — CFDIs (F1-PR1) ===
Millet.Api.Endpoints.CuentasPorPagar.CfdisEndpoints.MapCfdisEndpoints(app);

// === Cuentas por Pagar — Facturas con OC (F3-PR1 + revisión F4-PR1) ===
Millet.Api.Endpoints.CuentasPorPagar.FacturasEndpoints.MapFacturasEndpoints(app);

// === Cuentas por Pagar — Catálogos read-only (F4-PR1) ===
Millet.Api.Endpoints.CuentasPorPagar.CatalogosCxpEndpoints.MapCatalogosCxpEndpoints(app);

// === Cuentas por Pagar — Evidencias polimórficas (F4-PR2) ===
Millet.Api.Endpoints.CuentasPorPagar.EvidenciasEndpoints.MapEvidenciasEndpoints(app);

// === Cuentas por Pagar — Notas de crédito de proveedor (F6-PR1) ===
Millet.Api.Endpoints.CuentasPorPagar.NotasCreditoEndpoints.MapNotasCreditoEndpoints(app);

// === Cuentas por Pagar — Anticipos a proveedores (F6-PR2) ===
Millet.Api.Endpoints.CuentasPorPagar.AnticiposEndpoints.MapAnticiposEndpoints(app);

// === Cuentas por Pagar — Notas de cargo internas (F6-PR2) ===
Millet.Api.Endpoints.CuentasPorPagar.NotasCargoEndpoints.MapNotasCargoEndpoints(app);

// === Cuentas por Pagar — Comprobaciones de gastos / Caja Chica (F7-PR1) ===
Millet.Api.Endpoints.CuentasPorPagar.ComprobacionesEndpoints.MapComprobacionesEndpoints(app);
Millet.Api.Endpoints.CuentasPorPagar.ReposicionesCajaEndpoints.MapReposicionesCajaEndpoints(app);

// === Cuentas por Pagar — Viáticos electrónicos (F7-PR3) ===
Millet.Api.Endpoints.CuentasPorPagar.ViaticosEndpoints.MapViaticosEndpoints(app);

// === Cuentas por Pagar — Catálogos administrativos (aprobadores, políticas viáticos) (F7-PR3) ===
Millet.Api.Endpoints.CuentasPorPagar.CatalogosCxpAdminEndpoints.MapCatalogosCxpAdminEndpoints(app);

// === Cuentas por Pagar — Tarjetas de crédito empresariales (F7-PR4) ===
Millet.Api.Endpoints.CuentasPorPagar.TarjetasCreditoEndpoints.MapTarjetasCreditoEndpoints(app);

// === Cuentas por Pagar — Estados de cuenta TC + conciliación (F7-PR5) ===
Millet.Api.Endpoints.CuentasPorPagar.EstadosCuentaTcEndpoints.MapEstadosCuentaTcEndpoints(app);

// === Cuentas por Pagar — Reportes operativos (F8-PR1, ADR-0036) ===
Millet.Api.Endpoints.CuentasPorPagar.ReportesEndpoints.MapReportesEndpoints(app);

// === Integraciones AW: cotizaciones REST API (PR D) ===
// 6 endpoints bajo /api/v1/integraciones/aw/cotizaciones. Auth via
// PermisosCanonicos.IntegracionesAwCotizaciones{Crear,Consultar,Reintentar}.
// Idempotency-Key requerido en todos los POST.
Millet.Api.Endpoints.IntegracionesAw.IntegracionesAwEndpoints.MapIntegracionesAwEndpoints(app);

// === Endpoints del módulo Integraciones.Fiscal (PR-4) ===
// 6 endpoints bajo /api/v1/integraciones/fiscal/* (GET/PUT configuración
// + CRUD RFCs receptores). Auth via PermisosCanonicos.IntegracionesFiscal*.
// El POST /configuracion/{id}/test queda diferido a PR-5 (necesita el
// FiscalApiHttpClient real).
Millet.Api.Endpoints.IntegracionesFiscal.IntegracionesFiscalEndpoints.MapIntegracionesFiscalEndpoints(app);

// === Facturación — smoke de la fundación (F0-PR1) ===
Millet.Api.Endpoints.Facturacion.SmokeEndpoint.MapFacturacionSmokeEndpoint(app);

// === Facturación — Facturas de venta (F1-PR1: POST; F1-PR2: bandeja + detalle) ===
Millet.Api.Endpoints.Facturacion.FacturasEndpoints.MapFacturacionFacturasEndpoints(app);

// === Facturación — Pedidos facturables: captura manual + bandeja (F1-PR2) ===
Millet.Api.Endpoints.Facturacion.PedidosFacturablesEndpoints.MapFacturacionPedidosEndpoints(app);

// === Facturación — Anticipos: emisión FANT + vinculación + Control (F4-PR1) ===
Millet.Api.Endpoints.Facturacion.AnticiposEndpoints.MapFacturacionAnticiposEndpoints(app);

// === Facturación — Notas de crédito: bonificación (F5-PR1) ===
Millet.Api.Endpoints.Facturacion.NotasCreditoEndpoints.MapFacturacionNotasCreditoEndpoints(app);

// === Facturación — Comprobantes: cancelación SAT (F5-PR2) ===
Millet.Api.Endpoints.Facturacion.ComprobantesEndpoints.MapFacturacionComprobantesEndpoints(app);

// === Facturación — REPP: complemento de pago (F6) ===
Millet.Api.Endpoints.Facturacion.ReppEndpoints.MapFacturacionReppEndpoints(app);

// === Facturación — Carta Porte 3.1: emisión + siguiente tramo + catálogos (F8) ===
Millet.Api.Endpoints.Facturacion.CartaPorteEndpoints.MapFacturacionCartaPorteEndpoints(app);

// === Facturación — Activos fijos: autorización Contador General (F9) ===
Millet.Api.Endpoints.Facturacion.ActivosEndpoints.MapFacturacionActivosEndpoints(app);

// === Facturación — Reportes: liquidación caja + anticipos + CFDIs por obra (F11) ===
Millet.Api.Endpoints.Facturacion.ReportesEndpoints.MapFacturacionReportesEndpoints(app);

// === Facturación — Emisor-defaults + lookups de catálogo para emisión (FAC-UX-PR1) ===
Millet.Api.Endpoints.Facturacion.FacturacionCatalogosEndpoints.MapFacturacionCatalogosEndpoints(app);

// === Facturación — Cajas: CRUD + alcances (CAJAS-PR1, 12-cajas.md) ===
Millet.Api.Endpoints.Facturacion.CajasEndpoints.MapFacturacionCajasEndpoints(app);

// === Facturación — Cobros de mostrador (CAJAS-PR4, 12-cajas.md §6) ===
Millet.Api.Endpoints.Facturacion.CobrosEndpoints.MapFacturacionCobrosEndpoints(app);

// === Cuentas por Cobrar — Líneas de crédito (CXC-PR1) ===
Millet.Api.Endpoints.CuentasPorCobrar.LineasCreditoEndpoints.MapLineasCreditoEndpoints(app);

// === Cuentas por Cobrar — Crédito disponible (CXC-PR2) ===
Millet.Api.Endpoints.CuentasPorCobrar.CreditoDisponibleEndpoints.MapCreditoDisponibleEndpoints(app);

// === Cuentas por Cobrar — Lookup de clientes para el FE (CXC-FE-PR2) ===
Millet.Api.Endpoints.CuentasPorCobrar.ClientesLookupEndpoints.MapClientesLookupCxcEndpoints(app);

// === Cuentas por Cobrar — Cartera: saldo neto 13-K + anticipos (CXC-PR3) ===
Millet.Api.Endpoints.CuentasPorCobrar.CarteraEndpoints.MapCarteraEndpoints(app);

// === Cuentas por Cobrar — Liberación de pedidos + overrides (CXC-PR4) ===
Millet.Api.Endpoints.CuentasPorCobrar.LiberacionesEndpoints.MapLiberacionesEndpoints(app);

// === Cuentas por Cobrar — Seguimiento de cobranza (CXC-PR5) ===
Millet.Api.Endpoints.CuentasPorCobrar.CobranzaEndpoints.MapCobranzaEndpoints(app);

// === Cuentas por Cobrar — Propuestas de aplicación de pago (CXC-PR7) ===
Millet.Api.Endpoints.CuentasPorCobrar.PropuestasAplicacionEndpoints.MapPropuestasAplicacionEndpoints(app);

// === Cuentas por Cobrar — Alertas de cartera (CXC-PR8) ===
Millet.Api.Endpoints.CuentasPorCobrar.AlertasEndpoints.MapAlertasEndpoints(app);

// === Tesorería — Cuentas con saldo + libro de movimientos (TES-PR2) ===
Millet.Api.Endpoints.Tesoreria.CuentasEndpoints.MapTesoreriaCuentasEndpoints(app);
Millet.Api.Endpoints.Tesoreria.MovimientosEndpoints.MapTesoreriaMovimientosEndpoints(app);

// === Tesorería — Bandeja de pasivos pendientes de pago (TES-PR3) ===
Millet.Api.Endpoints.Tesoreria.PasivosEndpoints.MapTesoreriaPasivosEndpoints(app);

// === Tesorería — Pago a proveedor + reversa (TES-PR4, contrato congelado) ===
Millet.Api.Endpoints.Tesoreria.PagosEndpoints.MapTesoreriaPagosEndpoints(app);

// === Tesorería — Pago a cuenta: gate RN-2 + liga tardía (TES-PR6) ===
Millet.Api.Endpoints.Tesoreria.PagosACuentaEndpoints.MapTesoreriaPagosACuentaEndpoints(app);

// === Tesorería — Confirmación de depósitos de cliente (TES-PR7, TES-9) ===
Millet.Api.Endpoints.Tesoreria.DepositosEndpoints.MapTesoreriaDepositosEndpoints(app);

// === Tesorería — REPP recibido de proveedor: pendientes SLA + registro (TES-PR8, TES-4) ===
Millet.Api.Endpoints.Tesoreria.ReppEndpoints.MapTesoreriaReppEndpoints(app);

// === Tesorería — Reportes: flujo de efectivo + auxiliar de bancos (TES-PR10, ADR-0036) ===
Millet.Api.Endpoints.Tesoreria.ReportesEndpoints.MapTesoreriaReportesEndpoints(app);

// === SignalR hubs (CollaborationHub Sprint 1, ADR-0001 + ADR-0012 Capa 2) ===
// /hubs/compras: canal de colaboración del módulo. Auth requerida — el
// JwtBearer event OnMessageReceived (AuthExtensions) levanta el JWT del
// query param `access_token` para WebSockets, donde el navegador no
// permite headers custom.
app.MapHub<ComprasHub>("/hubs/compras").RequireAuthorization();

// /hubs/integraciones-aw: notificaciones del AwDropWorker (PR C +
// PR #201). Auth via permission policy
// integraciones.aw.cotizaciones.consultar (declarada en el [Authorize]
// de IntegracionesAwHub). El RequireAuthorization() se omite porque el
// hub ya lleva su propia policy attribute más específica.
app.MapHub<Millet.Api.Hubs.IntegracionesAwHub>("/hubs/integraciones-aw");

// === Health endpoints (ADR-0019) ===
//
// /health/live  : liveness probe — solo verifica que el proceso responde HTTP.
//                 Anonymous: usado por orquestadores externos sin claims.
// /health/ready : readiness probe — agregado de checks 'ready' (Postgres,
//                 migraciones). 503 saca a la instancia del pool de tráfico.
//                 Anonymous: el App Service la usa como healthCheckPath.
// /health       : detalle JSON con estado por dependencia. Protegido con
//                 RBAC (ADR-0007); el cliente debe tener el permiso
//                 'infra.health.leer' en la empresa actual del JWT.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
    AllowCachingResponses = false,
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("ready"),
    AllowCachingResponses = false,
    ResultStatusCodes = new Dictionary<HealthStatus, int>
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    },
});

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true,
    AllowCachingResponses = false,
    ResponseWriter = HealthCheckResponseWriter.WriteDetailedAsync,
})
.RequireAuthorization(PermissionPolicyProvider.Prefix + Millet.Identidad.Domain.PermisosCanonicos.InfraHealthLeer);

// === OpenAPI spec + Scalar UI (ADR-0017, F0-PR2) ===
// /openapi/v1.json : spec OpenAPI 3.x generada desde minimal API endpoints.
// /scalar/v1       : UI interactiva (Scalar) para explorar la spec.
//
// Solo se montan en Development/Staging. En Production las rutas no
// existen → 404, sin spec ni UI expuestas (ADR-0017 §"Seguridad de la
// spec en producción").
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.Run();

// Marker para que WebApplicationFactory<Program> pueda usarse en tests de integración.
public partial class Program;
