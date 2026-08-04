# 02 — EDI Correlation: Flujo asíncrono completo

> **Doc espejo.** Mantener copia idéntica en ambos repos:
> - `Project_Millet_ERP/docs/integration/02-edi-correlation.md`
> - `glass-agent/docs/integration/02-edi-correlation.md`
>
> **Versión:** 1.0.0
> **Última actualización:** 2026-05-15
> **Source of truth:** repo `Project_Millet_ERP`.
> **Depende de:** `00-system-overview.md`, `01-api-contract.md` (lectura previa obligatoria).

---

## 1. Propósito

Este documento describe el **flujo asíncrono completo** desde que el ERP recibe un POST de cotización hasta que se confirma la correlación con A+W. Cubre:

- Modelo de datos del esquema `integraciones_aw`.
- Mecánica del Outbox pattern (alineado a ADR-0009).
- Especificación del topic Service Bus y sus subscripciones.
- Lógica detallada de `AwDropWorker` y `AwCorrelationWorker`.
- Políticas de retry, timeout, dead letter.
- Customización que A+W requiere implementar para que la correlación funcione.
- Modos de falla y procedimientos de recuperación.

Si quieres entender qué hace cada endpoint, lee `01-api-contract.md`. Si quieres entender qué hace cada componente, lee `00-system-overview.md`. Si quieres entender **cómo cooperan en el tiempo y qué pasa cuando algo falla**, este es tu documento.

---

## 2. Modelo conceptual

El flujo tiene **tres etapas** secuenciales, cada una con su propia garantía:

```
┌────────────────────────────────────────────────────────────────────┐
│ ETAPA 1 — SUBMIT (síncrona, <100ms)                                │
│ Glass Agent POST → Millet.Api valida → persiste → emite Outbox     │
│ Garantía: pedido queda registrado en BD; ack inmediato al cliente  │
└────────────────────────────────────────────────────────────────────┘
                              │
                              ▼ (vía OutboxPublisherWorker → Service Bus)
┌────────────────────────────────────────────────────────────────────┐
│ ETAPA 2 — DROP (asíncrona, <30s normalmente)                       │
│ AwDropWorker consume evento → POST al drop service on-prem         │
│ → drop service escribe archivo a carpeta A+W                       │
│ Garantía: si HCM/drop están vivos, archivo llega en segundos       │
│ Retry: 5 intentos con backoff (10s, 30s, 2min, 10min, 30min)       │
└────────────────────────────────────────────────────────────────────┘
                              │
                              ▼ (latencia: A+W batch cada ~2min)
┌────────────────────────────────────────────────────────────────────┐
│ ETAPA 3 — CORRELATE (polling cada 30s, latencia total ~1-3min)     │
│ AwCorrelationWorker hace SELECT a pool_auftrag                     │
│ → si encuentra quote_reference → actualiza correlacion_aw          │
│ → emite SignalR + Outbox event                                     │
│ Garantía: con A+W procesando, correlación llega en <3min máximo    │
│ Timeout: si >15min sin match → marcar failed_correlation + alerta  │
└────────────────────────────────────────────────────────────────────┘
```

**Por qué tres etapas, no una.** El flujo síncrono "todo en el handler del POST" tendría una latencia de 1-3 minutos por el batch de A+W — inaceptable para el vendedor en una llamada con el cliente. Separar permite responder en milisegundos, fallar transitoriamente sin perder datos, y observar cada paso independientemente.

---

## 3. Modelo de datos (esquema `integraciones_aw`)

Cuatro tablas principales. Todas en el nuevo `IntegracionesAwDbContext`.

### 3.1 `integraciones_aw.entidad_externa`

Una fila por **cosa sincronizada con A+W** (cotización, pedido, cliente, artículo). Discriminada por `tipo_entidad`.

```sql
CREATE TABLE integraciones_aw.entidad_externa (
    id                      UUID PRIMARY KEY,                  -- UUID v7 (ordenable por tiempo)
    tipo_entidad            VARCHAR(40) NOT NULL,              -- 'cotizacion','pedido','cliente','articulo','inventario'
    referencia_externa      VARCHAR(40) NOT NULL,              -- 'Q-2026-00451' para cotización
    empresa_id              UUID NOT NULL REFERENCES compartido.empresa(id),
    payload_original        JSONB NOT NULL,                    -- request completo recibido
    payload_blob_id         UUID NULL,                         -- si el payload es grande (>1MB), referencia a blob storage
    edi_content             TEXT NULL,                         -- contenido EDI cuando aplica (cotización, pedido)
    estado                  VARCHAR(40) NOT NULL,              -- ver §3.5
    submitted_at            TIMESTAMPTZ NOT NULL,
    delivered_to_aw_at      TIMESTAMPTZ NULL,
    correlated_at           TIMESTAMPTZ NULL,
    aw_doc_id               BIGINT NULL,                       -- auftragsnummer cuando es cotización/pedido
    aw_doc_id_secondary     VARCHAR(80) NULL,                  -- para entidades sin id numérico simple
    submitted_by_spn_id     UUID NULL REFERENCES identidad.usuario_servicio(id),
    idempotency_key         UUID NOT NULL,                     -- propaga del header del POST
    last_error              TEXT NULL,
    retry_count             SMALLINT NOT NULL DEFAULT 0,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    CONSTRAINT uq_tipo_referencia UNIQUE (tipo_entidad, referencia_externa, empresa_id)
);

CREATE INDEX ix_entidad_externa_pendientes 
    ON integraciones_aw.entidad_externa (estado, submitted_at)
    WHERE estado IN ('submitted', 'awaiting_correlation', 'failed_drop');

CREATE INDEX ix_entidad_externa_correlation
    ON integraciones_aw.entidad_externa (tipo_entidad, referencia_externa)
    WHERE aw_doc_id IS NULL;

CREATE INDEX ix_entidad_externa_aw_doc
    ON integraciones_aw.entidad_externa (tipo_entidad, aw_doc_id)
    WHERE aw_doc_id IS NOT NULL;
```

**Decisión de `edi_content` inline**: para fase 1 se guarda en la tabla. Los EDIs son típicamente <50KB. Si en el futuro empiezan a crecer (>1MB) se migra a `payload_blob_id` apuntando a Azure Blob Storage con retención escalonada.

### 3.2 `integraciones_aw.envio`

Una fila por **cada intento de drop al on-prem**. Múltiples filas por entidad si hay reintentos.

```sql
CREATE TABLE integraciones_aw.envio (
    id                      UUID PRIMARY KEY,
    entidad_externa_id      UUID NOT NULL REFERENCES integraciones_aw.entidad_externa(id),
    attempt_number          SMALLINT NOT NULL,                 -- 1, 2, 3...
    started_at              TIMESTAMPTZ NOT NULL,
    finished_at             TIMESTAMPTZ NULL,
    status                  VARCHAR(20) NOT NULL,              -- 'started','success','failed','timeout'
    drop_service_url        VARCHAR(200) NOT NULL,             -- 'http://localhost-via-hybrid:5000/drop-edi'
    bytes_sent              INTEGER NULL,
    http_status_code        SMALLINT NULL,
    error_message           TEXT NULL,
    error_kind              VARCHAR(40) NULL,                  -- 'timeout','connection_refused','5xx','auth_failed','validation_failed'
    duration_ms             INTEGER NULL
);

CREATE INDEX ix_envio_entidad ON integraciones_aw.envio (entidad_externa_id, attempt_number);
CREATE INDEX ix_envio_failed_recent ON integraciones_aw.envio (started_at) WHERE status = 'failed';
```

### 3.3 `integraciones_aw.correlacion`

Una fila por **correlación exitosa con A+W**. Tiene 1:1 con `entidad_externa` cuando termina bien.

```sql
CREATE TABLE integraciones_aw.correlacion (
    id                      UUID PRIMARY KEY,
    entidad_externa_id      UUID NOT NULL UNIQUE REFERENCES integraciones_aw.entidad_externa(id),
    aw_doc_id               BIGINT NOT NULL,
    aw_doc_id_secondary     VARCHAR(80) NULL,
    polling_cycle_number    INTEGER NOT NULL,                  -- en qué ciclo de polling se encontró (debug)
    polling_query_duration_ms INTEGER NULL,
    correlated_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    aw_record_snapshot      JSONB NULL                         -- snapshot del row de pool_auftrag al momento del match (debug)
);
```

### 3.4 `integraciones_aw.outbox` (integration events)

Sigue el patrón Outbox del ERP (ADR-0009). Esquema idéntico al de `compras.integration_events_outbox`.

```sql
CREATE TABLE integraciones_aw.outbox (
    id                      UUID PRIMARY KEY,
    event_id                UUID NOT NULL UNIQUE,
    event_type              VARCHAR(80) NOT NULL,
    event_version           SMALLINT NOT NULL DEFAULT 1,
    aggregate_type          VARCHAR(40) NOT NULL,
    aggregate_id            UUID NOT NULL,
    payload                 JSONB NOT NULL,
    headers                 JSONB NULL,                        -- correlation_id, request_id, etc.
    occurred_at             TIMESTAMPTZ NOT NULL,
    published_at            TIMESTAMPTZ NULL,
    publish_attempts        SMALLINT NOT NULL DEFAULT 0,
    last_publish_error      TEXT NULL
);

CREATE INDEX ix_outbox_pending ON integraciones_aw.outbox (occurred_at) WHERE published_at IS NULL;
```

### 3.5 Diagrama de estados de `entidad_externa.estado`

```
              ┌─────────────┐
              │  submitted  │ (inserción inicial por POST)
              └──────┬──────┘
                     │
        ┌────────────┴────────────┐
        │                         │
        ▼ (AwDropWorker exitoso)  ▼ (AwDropWorker falla tras N reintentos)
┌──────────────────────┐  ┌──────────────────┐
│ awaiting_correlation │  │   failed_drop    │
└──────────┬───────────┘  └────┬─────────────┘
           │                   │ (operador reintenta manualmente)
           │                   ▼
           │             ┌─────────────┐
           │             │  submitted  │ ← vuelve al ciclo
           │             └─────────────┘
           │
   ┌───────┴──────┐
   │              │
   ▼ (match)      ▼ (timeout 15min)
┌──────────┐  ┌──────────────────────┐
│correlated│  │ failed_correlation   │
└──────────┘  └──────────┬───────────┘
                         │ (operador investiga, reintenta o cierra)
                         ▼
                  ┌──────────────────────┐
                  │ manually_resolved    │
                  └──────────────────────┘
```

Estados finales (no transicionan más): `correlated`, `manually_resolved`.
Estados activos (siguen siendo procesados): `submitted`, `awaiting_correlation`, `failed_drop`, `failed_correlation`.

---

## 4. Flujo detallado de submit

### 4.1 Handler del endpoint (síncrono, transaccional)

```csharp
// backend/src/Api/Endpoints/Integraciones/Aw/CotizacionesEndpoints.cs

app.MapPost("/api/v1/integraciones/aw/cotizaciones", async (
    SubmitCotizacionRequest request,
    [FromHeader(Name = "Idempotency-Key")] Guid idempotencyKey,
    IMediator mediator,
    CancellationToken ct) =>
{
    var command = new RegistrarCotizacionEdiCommand(request, idempotencyKey);
    var result = await mediator.Send(command, ct);
    return Results.Accepted($"/api/v1/integraciones/aw/cotizaciones/{result.QuoteReference}", result);
})
.RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesAw.CotizacionesCrear)
.WithMetadata(new RequireIdempotencyKeyAttribute());
```

Handler MediatR (transacción única):

```csharp
public class RegistrarCotizacionEdiHandler : IRequestHandler<RegistrarCotizacionEdiCommand, SubmitCotizacionResponse>
{
    private readonly IntegracionesAwDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentEmpresaContext _empresaCtx;
    private readonly ICurrentServicePrincipal _spnCtx;
    
    public async Task<SubmitCotizacionResponse> Handle(
        RegistrarCotizacionEdiCommand cmd, CancellationToken ct)
    {
        // 1. Validar duplicado por idempotency
        var existing = await _db.EntidadExterna
            .FirstOrDefaultAsync(e => e.IdempotencyKey == cmd.IdempotencyKey, ct);
        if (existing != null)
        {
            return SubmitCotizacionResponse.FromExisting(existing);
        }
        
        // 2. Validar duplicado por referencia
        var conflict = await _db.EntidadExterna
            .AnyAsync(e => e.TipoEntidad == "cotizacion" 
                        && e.ReferenciaExterna == cmd.Request.QuoteReference
                        && e.EmpresaId == _empresaCtx.Current, ct);
        if (conflict)
        {
            throw new DomainException("aw/quote_reference_duplicada", ...);
        }
        
        // 3. Persistir entidad
        var entidad = new EntidadExterna {
            Id = UuidV7.NewUuid(),
            TipoEntidad = "cotizacion",
            ReferenciaExterna = cmd.Request.QuoteReference,
            EmpresaId = _empresaCtx.Current,
            PayloadOriginal = cmd.Request,
            EdiContent = cmd.Request.EdiContent,
            Estado = "submitted",
            SubmittedAt = _clock.UtcNow,
            SubmittedBySpnId = _spnCtx.Id,
            IdempotencyKey = cmd.IdempotencyKey
        };
        _db.EntidadExterna.Add(entidad);
        
        // 4. Emitir Outbox event
        var evt = new AwCotizacionRecibidaEvent {
            EventId = Guid.NewGuid(),
            EventType = nameof(AwCotizacionRecibidaEvent),
            EventVersion = 1,
            AggregateType = "EntidadExterna",
            AggregateId = entidad.Id,
            QuoteReference = entidad.ReferenciaExterna,
            FilenameSuggestion = $"cot_{entidad.ReferenciaExterna}.edi",
            EmpresaId = entidad.EmpresaId,
            OccurredAt = _clock.UtcNow,
        };
        _db.Outbox.Add(OutboxEntry.From(evt));
        
        // 5. Una sola SaveChanges → atómico (interceptor del Outbox garantiza orden)
        await _db.SaveChangesAsync(ct);
        
        return SubmitCotizacionResponse.From(entidad);
    }
}
```

**Garantía clave**: el `SaveChanges` mete tanto `EntidadExterna` como `Outbox` en la **misma transacción**. Si la transacción falla, ambos rollback. Si tiene éxito, ambos persisten. **No hay forma de tener una cotización registrada sin su evento outbox correspondiente.**

### 4.2 OutboxPublisherWorker → Service Bus

Reusa el `OutboxPublisherWorker<TContext>` existente del ERP (parametrizable). Se instancia para el nuevo DbContext:

```csharp
// backend/src/Api/Program.cs (extensión)
services.AddHostedService<OutboxPublisherWorker<IntegracionesAwDbContext>>();
```

Comportamiento del worker (ya existe, lo describo para referencia):

1. Cada ~5s consulta `integraciones_aw.outbox WHERE published_at IS NULL ORDER BY occurred_at LIMIT 100`.
2. Por cada fila, construye un `ServiceBusMessage` y publica al topic.
3. Actualiza `published_at = NOW(), publish_attempts++`.
4. Si falla, deja `published_at = NULL` y registra el error; reintenta en próximo ciclo.
5. Si `publish_attempts > 10`, log de error crítico y alerta (no DLQ del lado outbox; el reintento es infinito hasta intervención manual).

### 4.3 Topic Service Bus `integraciones-aw-events`

Configurado en `infra/modules/servicebus.bicep`:

```bicep
resource topic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBusNamespace
  name: 'integraciones-aw-events'
  properties: {
    defaultMessageTimeToLive: 'P1D'           // 1 día TTL
    maxSizeInMegabytes: 1024
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    enableBatchedOperations: true
  }
}

resource dropSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic
  name: 'aw-drop-worker'
  properties: {
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    maxDeliveryCount: 5
    lockDuration: 'PT5M'                       // 5 min lock para el worker
    defaultMessageTimeToLive: 'P1D'
  }
}

resource dropRule 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: dropSubscription
  name: 'OnlyCotizacionRecibida'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: "EventType = 'AwCotizacionRecibidaEvent'"
    }
  }
}
```

**Schema del mensaje en Service Bus:**

```json
{
  "messageId": "<event_id, GUID>",
  "correlationId": "<quote_reference>",
  "subject": "AwCotizacionRecibidaEvent",
  "applicationProperties": {
    "EventType": "AwCotizacionRecibidaEvent",
    "EventVersion": "1",
    "EmpresaId": "<uuid>"
  },
  "body": {
    "event_id": "...",
    "event_type": "AwCotizacionRecibidaEvent",
    "event_version": 1,
    "occurred_at": "2026-05-15T18:32:14Z",
    "aggregate_type": "EntidadExterna",
    "aggregate_id": "<uuid de entidad_externa.id>",
    "quote_reference": "Q-2026-00451",
    "filename_suggestion": "cot_Q-2026-00451.edi",
    "empresa_id": "<uuid>"
  }
}
```

**Importante**: El `body` NO incluye `edi_content`. El worker, al recibir el evento, lee el contenido de `entidad_externa.edi_content` con el `aggregate_id`. Esto mantiene los mensajes pequeños (siempre <2KB) y compatible con Service Bus Standard (limit 256KB).

---

## 5. AwDropWorker

### 5.1 Estructura del HostedService

```csharp
// backend/src/Integraciones.Aw/Application/Workers/AwDropWorker.cs

public class AwDropWorker : BackgroundService
{
    private readonly ServiceBusClient _sbClient;
    private readonly IntegracionesAwDbContext _db;
    private readonly IAwDropAdapter _dropAdapter;
    private readonly IClock _clock;
    private readonly ILogger<AwDropWorker> _logger;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processor = _sbClient.CreateProcessor(
            topicName: "integraciones-aw-events",
            subscriptionName: "aw-drop-worker",
            options: new ServiceBusProcessorOptions {
                MaxConcurrentCalls = 1,                    // Procesamiento serial inicialmente
                AutoCompleteMessages = false,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10)
            });
        
        processor.ProcessMessageAsync += HandleMessageAsync;
        processor.ProcessErrorAsync += HandleErrorAsync;
        
        await processor.StartProcessingAsync(stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
        await processor.StopProcessingAsync();
    }
    
    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var evt = JsonSerializer.Deserialize<AwCotizacionRecibidaEvent>(
            args.Message.Body.ToArray());
        
        // 1. Cargar entidad desde BD (incluyendo edi_content)
        var entidad = await _db.EntidadExterna
            .FirstAsync(e => e.Id == evt.AggregateId, args.CancellationToken);
        
        // 2. Idempotencia interna: si ya está delivered, hacer ack y skip
        if (entidad.Estado != "submitted" && entidad.Estado != "failed_drop")
        {
            _logger.LogInformation("Entidad {Id} ya está en estado {Estado}, saltando drop",
                entidad.Id, entidad.Estado);
            await args.CompleteMessageAsync(args.Message);
            return;
        }
        
        // 3. Persistir intento
        var envio = new Envio {
            Id = Guid.NewGuid(),
            EntidadExternaId = entidad.Id,
            AttemptNumber = entidad.RetryCount + 1,
            StartedAt = _clock.UtcNow,
            Status = "started",
            DropServiceUrl = _dropAdapter.Endpoint
        };
        _db.Envio.Add(envio);
        await _db.SaveChangesAsync(args.CancellationToken);
        
        try {
            // 4. Llamada al drop service via Hybrid Connection
            var result = await _dropAdapter.SendEdiAsync(
                filename: evt.FilenameSuggestion,
                content: entidad.EdiContent,
                args.CancellationToken);
            
            // 5. Éxito: actualizar entidad + envio + emitir evento outbox
            envio.Status = "success";
            envio.FinishedAt = _clock.UtcNow;
            envio.BytesSent = result.BytesWritten;
            envio.HttpStatusCode = 200;
            envio.DurationMs = (int)(envio.FinishedAt.Value - envio.StartedAt).TotalMilliseconds;
            
            entidad.Estado = "awaiting_correlation";
            entidad.DeliveredToAwAt = _clock.UtcNow;
            entidad.UpdatedAt = _clock.UtcNow;
            
            _db.Outbox.Add(OutboxEntry.From(new AwEdiEntregadoEvent {
                AggregateId = entidad.Id,
                QuoteReference = entidad.ReferenciaExterna,
                BytesWritten = result.BytesWritten,
                Filename = evt.FilenameSuggestion
            }));
            
            await _db.SaveChangesAsync(args.CancellationToken);
            await args.CompleteMessageAsync(args.Message);
        }
        catch (AwDropException ex) when (ex.IsTransient) {
            // 6a. Falla transitoria: persistir intento, dejar que Service Bus reintente
            envio.Status = "failed";
            envio.FinishedAt = _clock.UtcNow;
            envio.ErrorMessage = ex.Message;
            envio.ErrorKind = ex.Kind;
            
            entidad.RetryCount++;
            entidad.LastError = ex.Message;
            entidad.UpdatedAt = _clock.UtcNow;
            
            await _db.SaveChangesAsync(args.CancellationToken);
            await args.AbandonMessageAsync(args.Message);  // ← vuelve a la cola
        }
        catch (AwDropException ex) {
            // 6b. Falla permanente (validation, auth): dead letter inmediato
            envio.Status = "failed";
            envio.FinishedAt = _clock.UtcNow;
            envio.ErrorMessage = ex.Message;
            envio.ErrorKind = ex.Kind;
            
            entidad.Estado = "failed_drop";
            entidad.LastError = ex.Message;
            entidad.UpdatedAt = _clock.UtcNow;
            
            _db.Outbox.Add(OutboxEntry.From(new AwEdiEntregaFallidaEvent { ... }));
            await _db.SaveChangesAsync(args.CancellationToken);
            await args.DeadLetterMessageAsync(args.Message,
                reason: ex.Kind,
                errorDescription: ex.Message);
        }
    }
}
```

### 5.2 Política de retry

Service Bus maneja el retry automáticamente con backoff exponencial cuando se hace `AbandonMessage`:

- Intento 1: inmediato
- Intento 2: ~10s después
- Intento 3: ~30s después
- Intento 4: ~2min después
- Intento 5: ~10min después
- Si los 5 fallan → mensaje a DLQ automático (config `maxDeliveryCount: 5`)

Errores **transitorios** (que justifican reintento):
- `timeout` (HTTP timeout al drop service)
- `connection_refused` (HCM no está activo)
- `5xx` del drop service
- `503` (mantenimiento)

Errores **permanentes** (DLQ inmediato sin esperar reintentos):
- `validation_failed` (drop service rechaza el contenido por formato)
- `auth_failed` (API key incorrecta — bug de config)
- `404` (Hybrid Connection no apunta a donde debe — bug de infra)

### 5.3 Dead Letter Queue handling

Cuando un mensaje termina en DLQ:

1. Application Insights emite alerta (configurada en `monitoring-alerts.bicep`).
2. La `entidad_externa.estado = 'failed_drop'` queda visible en el endpoint admin `GET /cotizaciones?status=failed_drop`.
3. Operador investiga vía endpoint `GET /cotizaciones/{ref}/historial`.
4. Si decide reintentar: `POST /cotizaciones/{ref}/reintentar` → handler recoloca el evento en outbox → vuelve al ciclo.
5. Si decide cerrar manualmente (ej. operador cargó el archivo a A+W a mano): endpoint admin `POST /cotizaciones/{ref}/marcar-resuelto`.

### 5.4 Concurrencia

`MaxConcurrentCalls = 1` en fase 1: procesamiento serial, simple de razonar. Si la carga lo justifica, subir a 5-10 después de observar latencia real.

**Importante**: aunque sea concurrente, NO hay riesgo de drop doble del mismo EDI porque la idempotencia interna (paso 2 del handler) verifica el estado antes de enviar.

---

## 6. AwCorrelationWorker

### 6.1 Cadencia y trigger

A diferencia de `AwDropWorker`, este no escucha Service Bus — hace **polling activo** a A+W vía Hybrid Connection.

```csharp
// backend/src/Integraciones.Aw/Application/Workers/AwCorrelationWorker.cs

public class AwCorrelationWorker : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _correlationTimeout = TimeSpan.FromMinutes(15);
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try {
                await PollPendingCorrelations(stoppingToken);
                await ExpireOldPending(stoppingToken);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Polling cycle failed; will retry next interval");
            }
            
            await Task.Delay(_interval, stoppingToken);
        }
    }
}
```

**Por qué 30s con batch de A+W cada 2min:**

Tiempo total esperado por el vendedor desde POST hasta ver `aw_doc_id`:

```
Drop a A+W:              ~5-15s
Espera al batch A+W:     ~0-120s (en promedio 60s, peor caso 120s)
Polling encuentra match: ~0-30s (en promedio 15s)
─────────────────────────────────
Total:                   ~20-165s (mediana ~80s)
```

Bajar el interval a 10s acortaría solo ~10s en la mediana — no vale la carga extra sobre A+W SQL Server. Subir a 60s subiría la mediana 15s — innecesariamente lento.

### 6.2 Query a A+W

```sql
-- Ejecutada vía SqlConnection sobre Hybrid Connection
SELECT 
    aw.<campo_correlacion> AS quote_reference,
    aw.auftragsnummer AS aw_doc_id,
    aw.erfassungsdatum AS created_at_aw
FROM pool_auftrag aw
WHERE aw.<campo_correlacion> IN (
    -- Lista de pendientes desde Postgres
    'Q-2026-00451', 'Q-2026-00452', 'Q-2026-00453', ...
)
AND aw.auftragsnummer IS NOT NULL
AND aw.<campo_correlacion> LIKE 'Q-%'  -- defensivo: solo nuestros refs
```

**TODO crítico**: `<campo_correlacion>` está pendiente de confirmar con el equipo A+W de Millet (ver §10 del overview). Candidatos:
- `auftragsnummer_kunde` (numero de orden del cliente)
- `bestellnummer_kunde` (numero de orden de compra del cliente)
- `referenz_extern` (referencia externa)
- Campo custom agregado por la customización

Esto se resuelve **antes** de codear el query. Hasta tanto, el código tiene `PLATFORM-TODO(<AwCorrelationField>)`.

### 6.3 Lógica de matching y publicación

```csharp
private async Task PollPendingCorrelations(CancellationToken ct)
{
    // 1. Obtener pendientes desde Postgres (max 500 a la vez)
    var pendientes = await _db.EntidadExterna
        .Where(e => e.Estado == "awaiting_correlation"
                 && e.TipoEntidad == "cotizacion"
                 && e.AwDocId == null
                 && e.DeliveredToAwAt > _clock.UtcNow.AddMinutes(-_correlationTimeout.TotalMinutes))
        .OrderBy(e => e.DeliveredToAwAt)
        .Take(500)
        .Select(e => new { e.Id, e.ReferenciaExterna })
        .ToListAsync(ct);
    
    if (pendientes.Count == 0) return;
    
    var pendingRefs = pendientes.Select(p => p.ReferenciaExterna).ToList();
    
    // 2. Query a A+W
    var startTime = _clock.UtcNow;
    var matches = await _awSqlReader.GetCorrelacionesAsync(pendingRefs, ct);
    var queryDurationMs = (int)(_clock.UtcNow - startTime).TotalMilliseconds;
    
    if (matches.Count == 0) return;
    
    // 3. Para cada match, actualizar entidad + crear correlacion + emitir evento
    foreach (var match in matches)
    {
        var entidad = await _db.EntidadExterna
            .FirstAsync(e => e.ReferenciaExterna == match.QuoteReference, ct);
        
        // Skip si alguien más lo procesó concurrentemente
        if (entidad.AwDocId != null) continue;
        
        entidad.AwDocId = match.AwDocId;
        entidad.CorrelatedAt = _clock.UtcNow;
        entidad.Estado = "correlated";
        entidad.UpdatedAt = _clock.UtcNow;
        
        _db.Correlacion.Add(new Correlacion {
            Id = Guid.NewGuid(),
            EntidadExternaId = entidad.Id,
            AwDocId = match.AwDocId,
            PollingCycleNumber = _currentCycleNumber,
            PollingQueryDurationMs = queryDurationMs,
            CorrelatedAt = _clock.UtcNow,
            AwRecordSnapshot = JsonSerializer.SerializeToElement(match)
        });
        
        _db.Outbox.Add(OutboxEntry.From(new AwPedidoCorrelacionadoEvent {
            AggregateId = entidad.Id,
            QuoteReference = entidad.ReferenciaExterna,
            AwDocId = match.AwDocId,
            CorrelatedAt = _clock.UtcNow
        }));
    }
    
    await _db.SaveChangesAsync(ct);
    _currentCycleNumber++;
}

private async Task ExpireOldPending(CancellationToken ct)
{
    var cutoff = _clock.UtcNow.Subtract(_correlationTimeout);
    var expired = await _db.EntidadExterna
        .Where(e => e.Estado == "awaiting_correlation"
                 && e.DeliveredToAwAt < cutoff)
        .ToListAsync(ct);
    
    foreach (var e in expired)
    {
        e.Estado = "failed_correlation";
        e.LastError = $"No se encontró correlación en A+W después de {_correlationTimeout.TotalMinutes} min";
        e.UpdatedAt = _clock.UtcNow;
        
        _db.Outbox.Add(OutboxEntry.From(new AwCorrelacionExpiradaEvent {
            AggregateId = e.Id,
            QuoteReference = e.ReferenciaExterna,
            SubmittedAt = e.SubmittedAt,
            ExpiredAt = _clock.UtcNow
        }));
    }
    
    if (expired.Count > 0) await _db.SaveChangesAsync(ct);
}
```

### 6.4 Notificación SignalR

El `AwPedidoCorrelacionadoEvent` lo emite el worker al Outbox. El `OutboxPublisherWorker` lo publica al Service Bus. Pero para SignalR **no necesitamos pasar por Service Bus** — el `AwCorrelationWorker` mismo, después del `SaveChangesAsync`, puede llamar directamente al hub:

```csharp
// Inmediato, sin pasar por Service Bus
await _hubContext.Clients.All.SendAsync(
    "CotizacionEstadoActualizado",
    new {
        quote_reference = e.ReferenciaExterna,
        previous_status = "awaiting_correlation",
        new_status = "correlated",
        aw_doc_id = match.AwDocId,
        timestamp = _clock.UtcNow
    },
    ct);
```

Esto da notificación sub-segundo al frontend / Glass Agent. El evento de Outbox + Service Bus queda como mecanismo de durabilidad y para suscriptores futuros (otros módulos del ERP que quieran reaccionar a correlaciones).

---

## 7. Customización requerida en A+W

**Esta es la pieza más sensible** del lado humano. La customización de A+W tiene que hacer una cosa simple pero que probablemente no hace hoy: **al procesar un EDI importado, persistir el campo de referencia externa que viene en el record FH en una columna consultable de `pool_auftrag`**.

### 7.1 Qué hace A+W hoy (suposición)

Lee EDI → parsea records → crea `pool_auftrag` con `auftragsnummer` autogenerado. Probablemente captura algunos campos del FH como dirección, fechas, etc. **Probablemente NO captura nuestro `quote_reference`** porque no es un campo estándar — lo metimos nosotros como referencia externa.

### 7.2 Qué tiene que hacer

Después de crear el pedido en `pool_auftrag`, actualizar un campo (existente o nuevo) con el contenido de la columna correspondiente del FH (donde el Glass Agent inyecta el `quote_reference`).

Pseudocódigo en AlcimBasic (lenguaje de customización A+W):

```basic
' Después de createOrder() en el flujo de import
Dim externalRef As String
externalRef = EdiHeader.GetField("FH", positionDelQuoteRef)  ' posición exacta TBD

If Not IsEmpty(externalRef) And Left(externalRef, 2) = "Q-" Then
    SqlExec "UPDATE pool_auftrag SET <campo_correlacion> = '" & externalRef & _
            "' WHERE auftragsnummer = " & nuevoAuftragsnummer
End If
```

### 7.3 Coordinación con equipo A+W

Esto requiere conversación específica con quien administre A+W en Millet. Preguntas a resolver con ellos:

1. ¿La customización del import actual está documentada / scripteable?
2. ¿Qué campo de `pool_auftrag` quieren usar para este propósito? (sugerencia: si existe `auftragsnummer_kunde` libre, usarlo)
3. ¿Quién implementa el cambio — alguien de Millet, A+W Software vendor, o tú?
4. ¿Cuál es la posición exacta del campo de referencia en el record FH del EDI que genera el Glass Agent? (consultar `AWEdiGenerator` para confirmar)

**Plan B si no se puede modificar A+W:**

Si por alguna razón A+W no permite esta customización, hay alternativas degradadas (todas peores):

- **Plan B1**: el `AwCorrelationWorker` busca por `dispatch_date` y heurísticas en `pool_auftrag.purchase_text1/2` — frágil, no recomendado.
- **Plan B2**: usar el filename del EDI como llave (A+W puede persistir el filename del archivo procesado). Requiere cambio en A+W también, equivalente.
- **Plan B3**: añadir un servicio on-prem que escuche cambios en `pool_auftrag` (CDC o trigger) y notifique al ERP via la Hybrid Connection inversa.

Idealmente plan A funciona.

---

## 8. Modos de falla y recuperación

### 8.1 HCM caído

**Síntoma:** AwDropWorker recibe timeouts/connection_refused al llamar al drop service. AwCorrelationWorker recibe los mismos errores al consultar SQL.

**Detección:**
- Application Insights: spike de `dependency.failed` con `target = "SER-DATA:5000"` o `"SER-DATA:1433"`
- Métrica custom `aw_hybrid_connection_down` (a definir)

**Comportamiento automático:**
- Drop: Service Bus reintenta con backoff (10s, 30s, 2min, 10min, 30min). Si HCM vuelve dentro de ese tiempo, drop se completa eventualmente sin intervención.
- Correlation: el polling sigue, pero todos los queries fallan. No se marca nada como expirado durante esta ventana (el ciclo de "expire" usa `_clock.UtcNow`, así que después de 15min sin HCM, todos los pendientes se marcarían failed_correlation).

**Mitigación recomendada:**
- Si HCM lleva >5 min caído, **pausar el ciclo de expiración** (toggle vía Compras settings o flag en KV).
- Cuando HCM vuelva, el ciclo de polling automáticamente correlaciona los pedidos pendientes.

**Recuperación manual:**
1. Reiniciar el servicio HCM en `SER-DATA`: `Restart-Service HybridConnectionManagerService`
2. Verificar `Get-HybridConnection` que las dos conexiones estén `Connected`
3. Si llevaban tiempo caídos, hacer `POST /admin/integraciones/aw/expire-pause` para evitar falsos timeouts mientras se procesa el backlog (TBD: endpoint a definir si vale la pena)

### 8.2 A+W batch parado

**Síntoma:** AwCorrelationWorker no encuentra matches en sus polls aunque sí están llegando archivos al folder de import.

**Detección:**
- Más de X pedidos en estado `awaiting_correlation` con `delivered_to_aw_at > 5 min` y ninguna correlación nueva.

**Comportamiento automático:**
- A los 15 min de espera, cada pedido pasa a `failed_correlation` y se alerta.

**Recuperación:**
- Comunicar al equipo A+W de Millet que el batch parece parado.
- Una vez procesado, los `failed_correlation` se pueden reintentar manualmente vía `POST /cotizaciones/{ref}/reintentar` — esto no re-envía el EDI (ya fue procesado por A+W), sino que **lo regresa a `awaiting_correlation`** para que el polling lo encuentre y correlacione.

### 8.3 ERP reiniciado mid-flow

**Síntoma:** Restart del App Service o deploy nuevo durante drops/polling.

**Comportamiento automático:**
- Service Bus mensajes en flight: si fueron `Lock`-eados pero no `Complete`-ados, el lock expira (5 min) y se reentregaen.
- Polling: pierde el ciclo actual; el siguiente arranca normal.
- Outbox: eventos publicados en flight se reintentan en el siguiente ciclo del OutboxPublisherWorker.

**Sin pérdida de datos** porque:
- Toda transición de estado es transaccional (SaveChanges atómico)
- Service Bus garantiza at-least-once delivery
- Idempotencia interna garantiza que dobles entregas no causen drops duplicados

### 8.4 Service Bus down

**Síntoma:** `OutboxPublisherWorker` no puede publicar; `AwDropWorker` no recibe mensajes.

**Comportamiento automático:**
- Outbox: acumulación en `integraciones_aw.outbox` con `published_at IS NULL`. Reintento infinito.
- Drop: el worker no consume nada, pero entidades quedan en `submitted` con sus EDIs persistidos.
- Endpoint POST: **sigue respondiendo 202** porque la operación local (BD) sigue funcionando — solo se acumula trabajo asíncrono.

**Recuperación:** cuando Service Bus vuelve, el OutboxPublisher publica todos los eventos acumulados y AwDropWorker los consume. Sin intervención manual.

### 8.5 Postgres down

**Síntoma:** todo el ERP falla. Endpoint POST devuelve 503.

**Comportamiento automático:** nada, el ERP no puede operar.

**Recuperación:** Azure Database for PostgreSQL Flexible Server tiene HA en producción. En dev puede haber downtime. Los reintentos del Glass Agent (en `glass_edi_outbox` local) acumulan trabajo durante el downtime y se procesan al volver.

---

## 9. Drop service (.NET 8 on-prem)

### 9.1 Endpoint

```
POST http://localhost:5000/drop-edi
Headers:
  X-API-Key: <key, configurado en config local y en KV>
  X-Filename: cot_Q-2026-00451.edi
  Content-Type: text/plain
Body: <contenido EDI bruto>

Response 200:
{
  "filename": "cot_Q-2026-00451.edi",
  "path": "C:\\AW\\Import\\cot_Q-2026-00451.edi",
  "bytes_written": 3598,
  "written_at": "2026-05-15T18:32:18Z"
}

Response 400: contenido vacío o filename inválido
Response 401: API key faltante o incorrecta
Response 422: validación de contenido falló (ej. no termina con #END#)
Response 500: error escribiendo al disco
```

### 9.2 Implementación de referencia

```csharp
// drop-service/Program.cs (~150 líneas)

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "MilletAwDropService");
builder.Logging.AddEventLog();

var app = builder.Build();

var apiKey = builder.Configuration["ApiKey"]
    ?? throw new InvalidOperationException("ApiKey no configurada");
var importPath = builder.Configuration["AwImportPath"]
    ?? @"C:\AW\Import";  // <-- TODO confirmar con Millet

app.MapPost("/drop-edi", async (HttpRequest req, ILogger<Program> log) =>
{
    // 1. Auth
    if (req.Headers["X-API-Key"] != apiKey) {
        log.LogWarning("Drop request con API key inválida desde {Ip}",
            req.HttpContext.Connection.RemoteIpAddress);
        return Results.Unauthorized();
    }
    
    // 2. Filename
    var filename = req.Headers["X-Filename"].ToString();
    if (string.IsNullOrWhiteSpace(filename) || filename.Contains("..") || 
        filename.Contains('/') || filename.Contains('\\')) {
        return Results.BadRequest(new { error = "filename inválido" });
    }
    
    // 3. Leer contenido
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    var content = ms.ToArray();
    
    if (content.Length == 0)
        return Results.BadRequest(new { error = "contenido vacío" });
    
    // 4. Validación mínima del contenido EDI
    var ediText = Encoding.UTF8.GetString(content);
    if (!ediText.Contains("#END#"))
        return Results.UnprocessableEntity(new { error = "EDI sin marca #END#" });
    
    // 5. Escribir
    var fullPath = Path.Combine(importPath, filename);
    try {
        await File.WriteAllBytesAsync(fullPath, content);
        log.LogInformation("Drop OK: {File}, {Bytes} bytes", filename, content.Length);
        return Results.Ok(new {
            filename,
            path = fullPath,
            bytes_written = content.Length,
            written_at = DateTimeOffset.UtcNow
        });
    }
    catch (Exception ex) {
        log.LogError(ex, "Falló escritura a {Path}", fullPath);
        return Results.Problem("Error escribiendo archivo", statusCode: 500);
    }
});

app.MapGet("/health", () => Results.Ok(new {
    status = "alive",
    version = typeof(Program).Assembly.GetName().Version?.ToString(),
    timestamp = DateTimeOffset.UtcNow
}));

app.Run("http://localhost:5000");
```

### 9.3 Instalación como servicio Windows

```powershell
# En SER-DATA, después de compilar y publicar:
sc.exe create MilletAwDropService binPath= "C:\Apps\MilletAwDropService\MilletAwDropService.exe" start= auto displayName= "Millet A+W Drop Service"
sc.exe description MilletAwDropService "Recibe archivos EDI vía Hybrid Connection y los escribe a la carpeta de import de A+W"
sc.exe start MilletAwDropService
```

### 9.4 Configuración

`appsettings.json` del servicio (en `C:\Apps\MilletAwDropService\`):

```json
{
  "ApiKey": "<key generada, idéntica a la que está en KV del ERP>",
  "AwImportPath": "C:\\AW\\Import\\",
  "Logging": {
    "LogLevel": { "Default": "Information" }
  }
}
```

La `ApiKey` se sincroniza manualmente: misma cadena aquí y en `kv-millet-{env}-mxc-01` (secret `aw-drop-service-api-key`). Rotación cada 6 meses.

---

## 10. Métricas y observabilidad

Métricas custom emitidas a Application Insights:

| Métrica | Tipo | Descripción |
|---|---|---|
| `aw.cotizacion.submitted` | Counter | Cada POST /cotizaciones exitoso |
| `aw.cotizacion.drop_success` | Counter | Drop al on-prem completado |
| `aw.cotizacion.drop_failed` | Counter | Drop falló (transitorio o permanente) |
| `aw.cotizacion.correlated` | Counter | Correlación con A+W exitosa |
| `aw.cotizacion.correlation_expired` | Counter | Timeout sin correlación |
| `aw.correlation.latency_ms` | Histogram | Tiempo desde drop hasta correlación |
| `aw.polling.cycle_duration_ms` | Histogram | Duración de cada ciclo de polling |
| `aw.polling.matches_found` | Histogram | Matches encontrados por ciclo |
| `aw.hybrid_connection.healthy` | Gauge | 1=OK, 0=down |

Alertas recomendadas:

| Condición | Severidad | Acción |
|---|---|---|
| `drop_failed` > 5 en 5 min | Alta | Email + verificar HCM |
| `correlation_expired` > 0 | Alta | Email + verificar A+W batch |
| `polling.cycle_duration_ms` p95 > 5s | Media | Verificar SQL A+W health |
| `hybrid_connection.healthy` = 0 por >2 min | Crítica | Email + SMS si configurado |

---

## 11. Pendientes / TODOs

| TODO | Bloqueante para | Resolución |
|---|---|---|
| Campo exacto en `pool_auftrag` para correlación | `AwSqlReader.GetCorrelacionesAsync` | Pregunta a equipo A+W de Millet |
| Cómo modificar la customización A+W | Funcionalidad de correlación | Conversación con quien administre A+W |
| Posición del `quote_reference` en record FH del EDI | Customización A+W | Confirmar con `AWEdiGenerator` en Glass Agent |
| Ruta exacta de `AwImportPath` | Drop service config | Equipo A+W de Millet |
| Decisión: hub SignalR compartido vs nuevo | `AwCorrelationWorker` notificación | Decisión técnica al codear |
| Endpoint admin `POST /cotizaciones/{ref}/marcar-resuelto` | Operación de DLQ | Definir en `01-api-contract.md` v1.1 |
| Endpoint admin `POST /admin/integraciones/aw/expire-pause` | Recuperación de fallas HCM largas | Post-MVP |

---

## 12. Cross-references

- **`00-system-overview.md`** §5 (diagrama), §7 (flujos), §10 (PLATFORM-TODOs)
- **`01-api-contract.md`** §4 (endpoints), §6 (errores), §5 (SignalR)
- **`03-deployment.md`** (próximo) — Bicep para Service Bus topic + Hybrid Connection
- **ADR-0009** Outbox pattern
- **ADR-0030** Multi-DbContext (cada módulo con su esquema)
- **ADR-0031** PLATFORM-TODOs

---

## CHANGELOG

### v1.0.0 — 2026-05-15
- Versión inicial.
- Define las tres etapas del flujo asíncrono.
- Modelo de datos completo del esquema `integraciones_aw`.
- Comportamiento detallado de `AwDropWorker` y `AwCorrelationWorker`.
- Políticas de retry, timeout, DLQ.
- Drop service de referencia (~150 líneas C#).
- Catálogo de modos de falla y procedimientos de recuperación.
- Customización A+W identificada como dependencia humana crítica.
