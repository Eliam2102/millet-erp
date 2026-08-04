# ADR-0022: Background jobs con Hosted Services nativos + advisory locks + NCrontab

- **Estado**: Aceptada
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: backend, jobs, infraestructura, tier-2

## Contexto y problema

El ERP necesita ejecutar trabajo fuera del ciclo request/response:

- **Workers continuos**: el publisher de outbox (ADR-0009) que corre cada
  pocos segundos, procesadores de mensajes de Service Bus
- **Jobs nocturnos programados**: limpieza del outbox, archival del
  audit_log, fetch del DOF de Banxico (ADR-0014), partition rollover
  (ADR-0008), limpieza de idempotency keys (ADR-0020), limpieza de
  hard locks (ADR-0012)
- **Jobs disparados por usuario**: generar un reporte grande, exportar
  cartera, recalcular saldos, enviar batch de CFDIs
- **Jobs de larga duración**: importación de datos desde SAP (proyecto
  específico)

Sin convención clara, cada job se implementa diferente, sin patrón común
para retries, observabilidad, o garantías de "solo una instancia ejecuta".
Necesitamos definir qué herramienta usar, qué patrones aplicar, y qué
garantías ofrece cada categoría.

## Drivers de la decisión

- Cero infraestructura adicional si ASP.NET Core ya provee lo necesario
- Compatibilidad con DI uniforme: jobs consumen `DbContext`, `IClock`, `ILogger` igual que el resto del código
- Garantía de "solo una instancia ejecuta" para jobs singleton, sin importar cuántas instancias del App Service existan
- Observabilidad integrada (App Insights, ADR-0006) sin dashboards paralelos
- Testing simple: jobs son clases C# regulares
- Migración a procesos separados es una opción futura, no un requisito inicial

## Opciones consideradas

1. Hosted Services nativos + NCrontab + advisory locks de PostgreSQL
2. Hangfire (popular, dashboard incluido, persistencia propia)
3. Quartz.NET (enterprise, scheduling avanzado)
4. Azure Functions / Container Apps Jobs desde el inicio
5. Combinación: Hosted Services para algunos, Hangfire para otros

## Decisión

Se adopta la **opción 1**: `BackgroundService` (hosted services nativos
de .NET) como mecanismo único, NCrontab para scheduling de cron-style
jobs, advisory locks de PostgreSQL para garantizar singleton.

### Categorías de jobs

**A) Workers continuos de polling**

- Outbox publisher (ADR-0009)
- Procesadores de Service Bus (cuando lleguen mensajes)
- User jobs worker (categoría C)

Patrón: `BackgroundService` con loop infinito y `Task.Delay` entre iteraciones.
Seguro escalar a N instancias gracias a `SELECT FOR UPDATE SKIP LOCKED` o
locks por mensaje en Service Bus.

**B) Jobs programados singleton**

- `OutboxCleanupJob` (nocturno)
- `IdempotencyKeysCleanupJob` (cada hora, ADR-0020)
- `LockCleanupJob` (cada minuto, ADR-0012)
- `AuditArchiveJob` (mensual, ADR-0008)
- `AuditPartitionRolloverJob` (mensual, ADR-0008)
- `DofTipoCambioFetcherJob` (diario, ADR-0014)

Patrón: `BackgroundService` con cron expression que calcula próxima
ejecución; al disparar adquiere advisory lock; si lo obtiene, ejecuta;
si no, otra instancia lo está corriendo, no hace nada.

**C) Jobs disparados por usuario**

- Generar reporte grande
- Exportar cartera a Excel
- Recalcular saldos
- Enviar batch de CFDIs

Patrón: NO son hosted services dedicados. El endpoint API crea una fila
en `core.user_jobs` con status `pending`. Un worker continuo
`UserJobsWorker` (categoría A) consume jobs de esa tabla y los ejecuta.
Frontend hace polling de `GET /api/jobs/{jobId}` o se suscribe vía
SignalR (ADR-0001) para progreso en tiempo real.

**D) Jobs de larga duración**

- Importación inicial desde SAP (saldos, cartera, proveedores, catálogo de cuentas)
- Recálculos masivos por cambio de catálogo

Patrón: variante de C con persistencia robusta (checkpointing). Si falla,
reanuda desde el último checkpoint. Se implementa caso por caso cuando
aplique; NO es patrón genérico.

### Patrón A — Worker continuo

```csharp
public class OutboxPublisherWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<OutboxPublisherWorker> _logger;

    public OutboxPublisherWorker(
        IServiceProvider services,
        ILogger<OutboxPublisherWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("OutboxPublisher iniciado");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var publisher = scope.ServiceProvider
                    .GetRequiredService<IOutboxPublisher>();

                var publishedCount = await publisher.PublishPendingAsync(
                    batchSize: 100, ct);

                if (publishedCount > 0)
                    _logger.LogInformation(
                        "OutboxPublisher procesó {Count} eventos", publishedCount);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "OutboxPublisher iteration failed; reintentando en 5s");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("OutboxPublisher detenido");
    }
}
```

**Características**:
- Loop continuo con `Task.Delay` entre iteraciones
- Manejo de excepciones: NUNCA propaga (matar el worker es peor que skipear una iteración); siempre loguea y reintenta en el siguiente ciclo
- Cada iteración crea su propio scope de DI (importante: `DbContext` es scoped, no se debe reusar entre iteraciones)
- Respeta `CancellationToken` para shutdown limpio
- Seguro escalar a N instancias: el repositorio interno del publisher usa `SELECT FOR UPDATE SKIP LOCKED` (ADR-0009)

### Patrón B — Job singleton programado

```csharp
public class DofTipoCambioFetcherJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IClock _clock;
    private readonly ILogger<DofTipoCambioFetcherJob> _logger;
    private readonly CrontabSchedule _schedule;
    private const string AdvisoryLockKey = "dof_tipo_cambio_fetcher_job";

    public DofTipoCambioFetcherJob(
        IServiceProvider services,
        IClock clock,
        IConfiguration config,
        ILogger<DofTipoCambioFetcherJob> logger)
    {
        _services = services;
        _clock = clock;
        _logger = logger;
        var cronExpr = config["BackgroundJobs:DofFetcher"] ?? "0 6 * * *";
        _schedule = CrontabSchedule.Parse(cronExpr);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = _clock.UtcNow;
            var nextRun = _schedule.GetNextOccurrence(now.UtcDateTime);
            var delay = nextRun - now.UtcDateTime;

            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation(
                    "DOF fetcher: próxima ejecución en {Delay}", delay);
                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { break; }
            }

            using var scope = _services.CreateScope();
            var lockManager = scope.ServiceProvider
                .GetRequiredService<IAdvisoryLockManager>();

            await using var lockHandle = await lockManager
                .TryAcquireAsync(AdvisoryLockKey, ct);

            if (!lockHandle.Acquired)
            {
                _logger.LogInformation(
                    "DOF fetcher: otra instancia adquirió el lock; saltando");
                continue;
            }

            try
            {
                var fetcher = scope.ServiceProvider
                    .GetRequiredService<IDofTipoCambioFetcher>();
                await fetcher.FetchAndSaveAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DOF fetcher falló");
            }
        }
    }
}
```

**Características**:
- Cron expression configurable desde `appsettings.json`
- Calcula próxima ejecución, espera, intenta adquirir advisory lock
- Si NO adquiere el lock, otra instancia ya está ejecutando: skipea la iteración (no es error, es esperado en escalado horizontal)
- Si adquiere el lock, ejecuta el job; el lock se libera al disposer del handle
- Excepciones del job no matan el worker; se loguean y se intenta en la próxima ejecución programada

**`IAdvisoryLockManager`**:

```csharp
public interface IAdvisoryLockManager
{
    Task<IAdvisoryLockHandle> TryAcquireAsync(string lockKey, CancellationToken ct);
}

public interface IAdvisoryLockHandle : IAsyncDisposable
{
    bool Acquired { get; }
}

// Implementación: hash del lockKey a long (PostgreSQL advisory locks usan bigint)
// SELECT pg_try_advisory_lock(:keyHash)
// El handle libera con SELECT pg_advisory_unlock(:keyHash) en DisposeAsync
```

### Patrón C — Jobs disparados por usuario

**Tabla `core.user_jobs`**:

```
user_jobs
├── id (uuid v7, PK)
├── empresa_id (uuid, FK)
├── usuario_id (uuid, FK)
├── tipo (text)                          -- 'reporte_cartera', 'export_cfdis', etc.
├── parametros (jsonb)                   -- input del job
├── status (text)                        -- 'pending'|'running'|'completed'|'failed'|'cancelled'
├── progreso_porcentaje (int, default 0)
├── progreso_mensaje (text, nullable)    -- "Procesando 12,000 de 50,000..."
├── resultado_url (text, nullable)       -- URL al Blob con resultado si aplica
├── error_mensaje (text, nullable)
├── created_at (timestamptz)
├── started_at (timestamptz, nullable)
├── completed_at (timestamptz, nullable)
└── correlation_id (uuid)
```

**Flujo**:

1. Usuario invoca `POST /api/reportes/cartera/generar` con parámetros del reporte
2. Endpoint inserta fila en `user_jobs` con `status = 'pending'`, retorna `202 Accepted` con `jobId` y URL de polling
3. `UserJobsWorker` (hosted service categoría A) hace polling de la tabla con `SELECT FOR UPDATE SKIP LOCKED` y procesa
4. Mientras procesa, el worker actualiza `progreso_porcentaje` y publica eventos vía SignalR al usuario que pidió el job
5. Al completar: marca `status = 'completed'`, sube resultado a Blob Storage si aplica, expone `resultado_url` con SAS token
6. Frontend recibe notificación SignalR (o polling fallback) y ofrece descarga al usuario

**Retención de user_jobs**:
- `completed`: 24 horas (después archivar a Blob o borrar)
- `failed`: 7 días (para investigación)
- Resultados en Blob: misma política

### Programación con NCrontab

- Paquete: `NCrontab` (MIT, mantenido, ~50KB)
- Cron expressions estándar: `min hora día mes diaSemana`
- Ejemplos:
  - `0 6 * * *` — todos los días a las 06:00 UTC (= 00:00 CDMX)
  - `0 3 * * *` — todos los días a las 03:00 UTC (= 21:00 CDMX día anterior)
  - `0 4 1 * *` — el día 1 de cada mes a las 04:00 UTC
  - `0 0 23 * *` — el día 23 de cada mes a medianoche UTC

**Configuración centralizada** en `appsettings.json`:

```json
{
  "BackgroundJobs": {
    "OutboxPublisher": {
      "PollingIntervalSeconds": 5,
      "BatchSize": 100
    },
    "OutboxCleanup": "0 3 * * *",
    "IdempotencyKeysCleanup": "0 * * * *",
    "LockCleanup": "*/1 * * * *",
    "AuditArchive": "0 4 1 * *",
    "AuditPartitionRollover": "0 0 23 * *",
    "DofFetcher": "0 6 * * *",
    "UserJobsWorker": {
      "PollingIntervalSeconds": 2,
      "BatchSize": 5
    }
  }
}
```

### Caveats de despliegue

**Estado inicial: proceso único**. Todos los hosted services corren en el
mismo App Service que la API web. Compartiendo:
- DI container
- Connection pool a PostgreSQL
- Connection a Service Bus
- Connection a SignalR Service

**Implicaciones**:
- Una instancia del App Service tiene API + workers + jobs todos juntos
- CPU/memoria compartida; si un job pesado consume mucho, afecta latencia del API
- Para esta fase del proyecto (operación inicial, ~50 usuarios concurrentes), aceptable
- Métricas de App Insights monitoreadas para detectar degradación

**Cuando se escala horizontalmente** (N instancias del mismo App Service):
- Workers de polling: cada instancia corre el suyo. `SELECT FOR UPDATE SKIP LOCKED` evita procesamiento duplicado
- Jobs singleton: advisory lock en PostgreSQL garantiza que solo UNA instancia ejecuta. Las demás skipean la iteración

**Cuando un job se vuelve pesado** (CPU/memoria significativa, ej. archival de GBs):
- Se extrae a Container Apps Job (Azure) o Azure Functions con Timer Trigger
- Como `IHostedService` ya está estructurado, la extracción es mecánica: copy/paste a otro proyecto que solo hospede ese worker
- Decisión que se toma cuando se justifique métricamente, no preventivamente

### Observabilidad

**Logs estructurados** (Serilog + App Insights, ADR-0006):
- Cada iteración importante de un worker loguea con propiedades: `JobName`, `BatchSize`, `ProcessedCount`, `DurationMs`
- Errores con stack trace y `correlation_id`
- Logs separados de logs de request HTTP por la propiedad `Source = "BackgroundJob"`

**Métricas custom**:
- `background_job.iterations_total{job=...}` — contador
- `background_job.iteration_duration_ms{job=...}` — histograma
- `background_job.errors_total{job=...}` — contador
- `background_job.last_success_timestamp{job=...}` — gauge (alerta si quedó atrás)
- Para user_jobs: `user_jobs.queue_depth` (cuántos pending), `user_jobs.processing_time_p95`

**Alertas en App Insights**:
- Si un job singleton no completa en 24h (`last_success_timestamp` viejo): P2
- Si la cola de user_jobs supera 100 pending sostenido: P2
- Si un worker continuo está fallando >5 veces consecutivas: P1

### Testing

**Unit tests** de jobs:
- Mock de `IServiceProvider` y dependencias
- `TestClock` para controlar tiempo (ADR-0013)
- Validar comportamiento ante errores, cancelación, advisory lock no adquirido

**Integration tests**:
- Job real corriendo contra Testcontainers PostgreSQL
- Validar que el advisory lock funciona (lanzar dos jobs en paralelo, solo uno ejecuta)
- Validar que las migraciones de `core.user_jobs` están correctas

## Consecuencias

**Positivas**
- Cero dependencia adicional: solo `NCrontab` (50KB) que es trivial
- Misma DI, mismo patrón de testing que el resto del código
- Compatible con escalado horizontal vía advisory locks (ADR-0009)
- Observabilidad uniforme en App Insights, sin dashboards paralelos
- Patrones explícitos por categoría: el dev nuevo sabe qué patrón aplicar según el tipo de job
- Migración a procesos separados es opción futura abierta, no costo hundido ahora

**Negativas**
- No hay dashboard visual built-in (Hangfire trae uno). Mitigado: App Insights cumple para observación, y para operación se construye una pantalla custom en el ERP que muestre `core.user_jobs` cuando se necesite
- Cron en código C# (NCrontab) es menos flexible que Quartz para schedules muy complejos. En la práctica los schedules del ERP son simples (diarios, mensuales, intervalos cortos)
- Workers comparten proceso con la API: una iteración pesada puede impactar latencia del API. Aceptable inicialmente; mitigable extrayendo a procesos separados cuando se justifique
- Disciplina de manejo de errores: cada worker DEBE atrapar excepciones para no morir. Mitigable con clase base `BackgroundJobBase` que provee el patrón

## Descartadas

**Hangfire**. Maduro y popular, pero:
- Agrega tablas propias en BD (`HangFire.*`) distintas de las nuestras
- Dashboard separado (la observabilidad ya está en App Insights)
- Hangfire Pro requiere licencia para varios features (batches, performance counters)
- Su modelo de "fire-and-forget" jobs duplica algo que con Service Bus + outbox ya tenemos para eventos
- Para jobs con cron, no aporta sobre lo que NCrontab + hosted service hacen

**Quartz.NET**. Más enterprise que Hangfire pero:
- Más complejidad inicial (configuración pesada)
- Scheduling más avanzado del que necesitamos
- Comunidad más pequeña en .NET moderno
- No justifica la fricción

**Azure Functions / Container Apps Jobs desde el inicio**. Excelentes para
jobs muy pesados o eventos externos (ej. webhook de PAC), pero:
- Multiplican infraestructura (más recursos en Bicep, más cosas que monitorear)
- Cold starts de Functions afectan latencia
- Para los jobs actuales (todos relativamente livianos), no se justifica

**Combinación Hosted Services + Hangfire**. Tener dos sistemas paralelos
para algo que cumple uno solo es overhead conceptual sin beneficio claro.

## Notas de implementación

**Backend**

- Agregar `NCrontab` a `Directory.Packages.points` (versión última estable)
- Implementar `IAdvisoryLockManager` con `pg_try_advisory_lock` de PostgreSQL en `Shared/Infrastructure/Locking/`
- Crear clase base `ScheduledBackgroundJobBase : BackgroundService` que encapsula el patrón B (cron + advisory lock + manejo de errores). Cada job concreto solo implementa `ExecuteJobAsync(IServiceProvider scope, CancellationToken ct)`
- Registrar todos los hosted services en `Program.cs` con `AddHostedService<...>()`
- Crear tabla `core.user_jobs` con migración inicial cuando se requiera (probablemente Fase 3 o 4, no Fase 1)

**Health checks**

- Agregar a `/health/ready` (ADR-0019) un check `background_jobs` que verifica:
  - El último éxito de cada job programado fue en su ventana esperada
  - Si algún job no ha tenido éxito en 2x su intervalo: degradado
  - Si en 5x su intervalo: unhealthy

**Documentación en `CLAUDE.md`**

- Cuándo usar cada patrón (A continuo, B singleton programado, C user-triggered, D long-running)
- Cómo agregar un job nuevo (heredar de la clase base apropiada)
- Convenciones de naming: `*Worker` para A, `*Job` para B
- Cómo configurar cron expressions
- Cómo testear un job

**ADRs hijo posibles**

- Cuando un job específico se justifique extraer a Container Apps / Functions
- Política de archival/retención de `user_jobs`
- Estrategia de UI para mostrar estado de jobs al usuario
- Estrategia de jobs distribuidos si se necesita orquestación más compleja (Saga pattern, workflows)
