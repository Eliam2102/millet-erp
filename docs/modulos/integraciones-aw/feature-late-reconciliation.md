# Feature — Reconciliación tardía de drops A+W

> **Estado:** Propuesta — pendiente aprobación de Eduardo Paredes antes de
> abrir PRs.
>
> **Fecha:** 2026-05-29
>
> **Owner:** Eduardo Paredes — `eduardo.paredes@tiglass.net`
>
> **Módulo:** `Millet.Integraciones.Aw`
>
> **Relacionado con:** PR #198 (callback contract), PR #199 (per-EDI sync),
> PR #201 (retiro del `AwCorrelationWorker` polling). Este feature **no
> revierte** esos PRs — los complementa para el caso borde donde A+W
> procesa el EDI **después** del timeout del drop service.

---

## 1. Contexto y problema

El flow actual de cotizaciones del Glass Agent termina así cuando A+W se
demora más de `DropService:WaitTimeoutSeconds` (default 120s) en procesar
el EDI:

1. `AwDropWorker` consume `AwCotizacionRecibida` y llama
   [`IAwDropAdapter.SendEdiAsync`](../../../backend/src/Integraciones.Aw/Application/Ports/IAwDropAdapter.cs).
2. El drop service on-prem escribe el archivo en `Work\`, polling-ea
   `last_batch.log` durante 120s, no encuentra match y responde
   `outcome=stuck` ([Program.cs:300-345](../../../on-prem/aw-drop-service/Program.cs#L300-L345)).
3. El worker llama `EntidadExterna.MarcarStuck` →
   `Estado=FailedDrop`, `LastError=[aw_processing_timeout] …`.
4. **A+W procesa el EDI tarde** (segundos, minutos u horas después) y crea
   el documento. El log se escribe en `last_batch.log`. Nadie le avisa al
   ERP.

Tres sub-problemas independientes:

| ID | Descripción | Severidad |
|---|---|---|
| **G1** | A+W procesa el EDI después del timeout, ningún componente notifica al ERP. La cotización queda en `FailedDrop` con `AwDocId=null`. | Alta — bloquea reportería, CxC y todo lo que dependa del doc en A+W. |
| **G2** | Si el outcome del drop service fue `stuck`, el log A+W **no se archiva** en `Processed\` y se sobreescribe en el siguiente ciclo del customizing. Sin esto, ninguna estrategia de recovery automática puede leer el `AwDocId` real. | Media — prerequisito de G1. |
| **G3** | No hay endpoint admin para correlacionar manualmente una cotización con un `AwDocId`. `MarcarResueltoManual` solo guarda texto libre, no asigna `AwDocId`. | Alta — escape hatch para casos donde la recovery automática falla. |

---

## 2. Drivers de la decisión

- **Preservar la decisión arquitectónica de PR #199 + #201:** el log A+W
  (no SQL) es la fuente de verdad del outcome. No revertir hacia polling
  SQL como flow normal.
- **Mantener la direccionalidad del HCM:** ERP → on-prem, no abrir
  conexiones inbound desde SER-DATA al cloud.
- **Drop service stateless:** que no necesite persistir estado para
  sobrevivir reinicios.
- **Cobertura completa:** todo `aw_doc_id` que A+W generó debe llegar al
  ERP, automático o manual.
- **YAGNI:** no introducir SQL fallback hasta tener telemetría que lo
  justifique.

---

## 3. Decisiones cerradas

| # | Decisión | Valor | Razón |
|---|---|---|---|
| **D1** | Fuente de reconciliación tardía | **Solo logs (`Processed\`)** vía nuevo endpoint `GET /completions` del drop service. SQL de A+W queda fuera de Fase 1. | Consistencia con PR #201; aprovecha parser existente; no requiere vistas SQL nuevas. |
| **D2** | Intervalo del worker | **120s configurable** (`IntegracionesAw:LateReconciliation:IntervalSeconds`). `WaitTimeoutSeconds` del drop service se mantiene en 120s. | Sin telemetría del p95 real de A+W. Calibrar con métrica `aw.cotizacion.late_correlated` post-deploy. |
| **D3** | Ventana de búsqueda | **24h hacia atrás** desde `now`. Cotizaciones con `FailedAt < now - 24h` requieren G3 (correlación manual). | Captura demoras razonables (overnight, fin de semana) sin queries pesadas indefinidas. |
| **D4** | GC de `Processed\` | **Scheduled task de Windows externa** al drop service, en SER-DATA, retención **30 días**. Documentado en runbook. | Drop service stateless; 30 días = ventana de recovery (24h) + ~29 días de forense humano. |
| **D5** | Ubicación del campo de matching | Columna `Filename` en la entidad **`Envio`** (no en `EntidadExterna`). Poblada por `AwDropWorker.HandleMessageAsync` antes de `Envio.Empezar(...)`. | Cada intento tiene filename distinto (timestamp); `Envio` ya es tabla de historial por intento. |
| **D6** | ADR formal para `FailedDrop → Correlated` | **No requerido.** Es una ampliación de transición de aggregate, no un cambio arquitectónico. Se documenta en este doc + XML doc del método. | Los ADRs vigentes son arquitecturales. Esta transición es interna al aggregate. |
| **D7** | G3 desde `FailedCorrelation` | **No permitido.** Solo desde `FailedDrop`. `FailedCorrelation` sigue requiriendo corregir el EDI y reintentar. | Si A+W rechazó por dato malo, taparlo con un doc creado a mano oculta el error real. La fricción es deliberada. |

---

## 4. Diseño detallado

### 4.1. Cambio en la entidad `Envio` (D5)

Agregar columna nullable `Filename` (string, max 200) a [`Envio.cs`](../../../backend/src/Integraciones.Aw/Domain/Envio.cs):

```csharp
public string? Filename { get; private set; }

public static Envio Empezar(
    EntidadExterna entidad,
    short attemptNumber,
    DateTimeOffset startedAt,
    string dropServiceUrl,
    string filename)            // ← parámetro nuevo
{
    // …validaciones existentes…
    ArgumentException.ThrowIfNullOrWhiteSpace(filename);

    var envio = new Envio(
        Guid.CreateVersion7(),
        entidad.Id,
        attemptNumber,
        startedAt,
        dropServiceUrl);
    envio.Filename = filename;
    return envio;
}
```

**Callsites a actualizar:** `AwDropWorker` cuando construye el `Envio`
antes del `SendEdiAsync` — el filename ya está disponible en
`evt.FilenameSuggestion`.

**Migración:** `AddFilenameToEnvio` (nullable, sin default — los envíos
históricos quedan `null` y el matcher los ignora).

### 4.2. Archivado robusto en el drop service (G2)

Modificar [`Program.cs:309-313`](../../../on-prem/aw-drop-service/Program.cs#L309-L313):

```csharp
// Audit trail: archivar SIEMPRE que el log haya sido leído al menos una vez,
// incluso si outcome=Stuck. El late-reconciler del ERP consume esta carpeta.
if (waitResult.ActiveLogPath is not null)
{
    AwLogWatcher.TryCopyToProcessed(
        waitResult.ActiveLogPath,
        filename,
        outcomeStr,                 // ← nuevo parámetro
        log);
}
```

Y en `AwLogWatcher.TryCopyToProcessed`, naming:

```text
{processedDir}\{ourFilename basename}_{outcome}_{utcTimestamp}.log
```

Esto da al reader del endpoint `/completions` la posibilidad de filtrar
por outcome sin re-parsear el archivo (optimización futura).

**Edge case:** si `outcome=Stuck` y `ActiveLogPath == null` (nunca apareció
el log activo durante el timeout), no hay nada que archivar. La cotización
queda en `FailedDrop` y solo G3 puede recuperarla.

### 4.3. Endpoint `GET /completions` del drop service

Nuevo endpoint en [`Program.cs`](../../../on-prem/aw-drop-service/Program.cs):

```http
GET /completions?since={iso8601}&limit={int}
Authorization: X-API-Key: <same as /drop-edi>

Response 200:
{
  "completions": [
    {
      "filename": "cot_NIN_Q-2026-00346-20260529103045.edi",
      "outcome": "success",
      "aw_doc_id": 31247,
      "aw_error_codes": [],
      "aw_error_message": null,
      "parsed_at": "2026-05-29T10:33:12.118Z",
      "lane": "default"
    },
    …
  ],
  "next_since": "2026-05-29T10:33:12.118Z"
}
```

**Comportamiento:**
- Lee todos los archivos en `Processed\` de cada lane configurada con
  mtime > `since`.
- Parsea cada uno con `AwLogParser.Parse(content, filenameFromName)`.
- Ordena por `parsed_at` ascendente.
- `limit` default 500, max 2000.
- `next_since` = `parsed_at` del último item devuelto (para paginación
  cursor-based).
- Auth: mismo `X-API-Key` que `/drop-edi`.
- Body máximo: 5MB.

**Por qué cursor-based en vez de offset:** `Processed\` crece con el
tiempo y los archivos no se borran sincronizados con la lectura del ERP
(GC externo, retención 30 días). Cursor por mtime es estable ante el GC.

### 4.4. Puerto `IAwCompletionsReader` y adapter HTTP

Patrón paralelo a `IAwSqlReader` ya existente:

```csharp
// backend/src/Integraciones.Aw/Application/Ports/IAwCompletionsReader.cs
namespace Millet.Integraciones.Aw.Application.Ports;

public interface IAwCompletionsReader
{
    Task<AwCompletionsPage> ListSinceAsync(
        DateTimeOffset since,
        int limit,
        CancellationToken cancellationToken);
}

public sealed record AwCompletionsPage(
    IReadOnlyList<AwCompletionItem> Completions,
    DateTimeOffset? NextSince);

public sealed record AwCompletionItem(
    string Filename,
    DropOutcome Outcome,
    long? AwDocId,
    IReadOnlyList<string> ErrorCodes,
    string? ErrorMessage,
    DateTimeOffset ParsedAt,
    string Lane);
```

Adapter en
`backend/src/Integraciones.Aw/Infrastructure/Adapters/HttpAwCompletionsAdapter.cs`,
mismo patrón que [`HttpAwDropAdapter`](../../../backend/src/Integraciones.Aw/Infrastructure/Adapters/HttpAwDropAdapter.cs):

- Reuso del `HttpClient` registrado para el drop service (named client
  `aw-drop-service`).
- Header `X-API-Key` desde `IntegracionesAwOptions.DropService.ApiKey`.
- Timeout 30s.
- Excepción `AwReaderException` (ya existe) con `IsTransient` para
  errores 5xx vs configuración.

### 4.5. Ampliación de transición `MarcarCorrelacionadaDirectamente` (D6)

Hoy ([EntidadExterna.cs:230-233](../../../backend/src/Integraciones.Aw/Domain/EntidadExterna.cs#L230-L233))
el método solo acepta `Estado = Submitted`. Ampliar para aceptar
`FailedDrop` cuando el `ErrorKind` original era `aw_processing_timeout`:

```csharp
public void MarcarCorrelacionadaDirectamente(
    long? awDocId,
    DateTimeOffset correlatedAt,
    string? diagnosticLog = null,
    bool permitirDesdeFailedDrop = false)        // ← nuevo flag
{
    // …idempotencia existente…

    if (Estado is EstadoEntidad.Submitted)
    {
        // path normal
    }
    else if (Estado is EstadoEntidad.FailedDrop
             && permitirDesdeFailedDrop
             && IsErrorKind("aw_processing_timeout"))
    {
        // path de reconciliación tardía
    }
    else
    {
        throw new InvalidStateTransitionException(Estado, …);
    }

    // …resto de la transición existente…
}
```

**Por qué el flag explícito y no abrir el path siempre:** mantener
fricción para que el path `FailedDrop → Correlated` solo se use desde el
worker de reconciliación tardía y el endpoint admin de G3. Si abrimos
la transición sin flag, futuros refactors podrían disparar
correlaciones desde estados inválidos.

`MarcarCorrelacionFallidaDirectamente` recibe el mismo tratamiento (caso
real: A+W rechazó en su ciclo tardío y el log lo refleja como `failed`).

### 4.6. Worker `AwLateReconciliationWorker` (G1)

Nuevo `IHostedService` en
`backend/src/Api/Workers/AwLateReconciliationWorker.cs` (mismo lugar que
`OutboxPublisherWorker`, `AwDropWorker`).

**Pseudocódigo:**

```csharp
public sealed class AwLateReconciliationWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.LateReconciliation.IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ReconcileOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Late reconciliation cycle falló");
                _meter.LateReconciliationErrors.Add(1);
            }
            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task ReconcileOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db      = scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>();
        var reader  = scope.ServiceProvider.GetRequiredService<IAwCompletionsReader>();
        var bypass  = scope.ServiceProvider.GetRequiredService<IEmpresaBypass>();
        using var _ = bypass.EnableForBackground();

        var ventana = _clock.UtcNow.AddHours(-24);

        // 1. Cotizaciones candidatas en FailedDrop con kind timeout, en ventana.
        var candidatas = await db.EntidadesExternas
            .Include(e => e.Envios)
            .Where(e => e.Estado == EstadoEntidad.FailedDrop
                     && e.LastError != null
                     && EF.Functions.Like(e.LastError, "[aw_processing_timeout]%")
                     && e.FailedAt >= ventana)
            .ToListAsync(ct);

        if (candidatas.Count == 0) return;

        // 2. Pedir completions desde el FailedAt más antiguo.
        var since = candidatas.Min(c => c.FailedAt!.Value);
        var page  = await reader.ListSinceAsync(since, limit: 2000, ct);

        var indexByFilename = page.Completions
            .ToLookup(c => c.Filename, StringComparer.OrdinalIgnoreCase);

        // 3. Matchear y aplicar transiciones.
        foreach (var candidata in candidatas)
        {
            var ultimoFilename = candidata.Envios
                .OrderByDescending(e => e.AttemptNumber)
                .Select(e => e.Filename)
                .FirstOrDefault(f => f is not null);

            if (ultimoFilename is null) continue;  // envíos pre-feature

            var match = indexByFilename[ultimoFilename]
                .OrderByDescending(c => c.ParsedAt)
                .FirstOrDefault();

            if (match is null) continue;

            switch (match.Outcome)
            {
                case DropOutcome.Success:
                    candidata.MarcarCorrelacionadaDirectamente(
                        match.AwDocId,
                        match.ParsedAt,
                        diagnosticLog: $"late-reconcile: lane={match.Lane}",
                        permitirDesdeFailedDrop: true);
                    await _publisher.PublishAsync(
                        new AwPedidoCorrelacionado(…), ct);
                    _meter.LateCorrelated.Add(1);
                    break;

                case DropOutcome.Failed:
                    candidata.MarcarCorrelacionFallidaDirectamente(
                        match.ErrorCodes, match.ErrorMessage!, match.ParsedAt,
                        diagnosticLog: $"late-reconcile: lane={match.Lane}",
                        permitirDesdeFailedDrop: true);
                    await _publisher.PublishAsync(
                        new AwCotizacionRechazadaPorAw(…), ct);
                    _meter.LateFailed.Add(1);
                    break;

                case DropOutcome.Stuck:
                    // sigue stuck en el log → seguimos esperando, no hacemos nada
                    break;
            }
        }

        await db.SaveChangesAsync(ct);

        // 4. Métrica de cotizaciones fuera de ventana (señal para G3).
        var huerfanas = await db.EntidadesExternas.CountAsync(e =>
            e.Estado == EstadoEntidad.FailedDrop
         && e.LastError != null
         && EF.Functions.Like(e.LastError, "[aw_processing_timeout]%")
         && e.FailedAt < ventana, ct);
        _meter.LateReconciliationMisses.Record(huerfanas);
    }
}
```

**Registro:** `Millet.Api/Program.cs` agregar
`builder.Services.AddHostedService<AwLateReconciliationWorker>();` junto
a los demás.

**Health check:** flag `IsRunning` siguiendo el patrón de `AwDropWorker`,
para que `/health/ready` reporte Unhealthy si el worker no arrancó.

**Concurrencia:** un worker por instancia del API. Si en el futuro
escalamos a múltiples instancias, agregar lease via
`SoftLockManager` (ya disponible en SharedKernel) — fuera de Fase 1.

### 4.7. Endpoint `POST /{id}/correlacionar-manual` (G3)

```http
POST /api/v1/integraciones/aw/cotizaciones/{id}/correlacionar-manual
Idempotency-Key: <uuid v4>
Authorization: Bearer <jwt with permission integraciones.aw.cotizaciones.correlacionar-manual>

Body:
{
  "awDocId": 31247,
  "justificacion": "Operador confirmó doc creado por A+W el 28/05 09:15, log rotado antes de archivado."
}

Response 200:
{
  "id": "…",
  "quoteReference": "Q-2026-00346",
  "estado": "Correlated",
  "awDocId": 31247,
  "correlatedAt": "2026-05-29T11:02:18Z"
}
```

**Permisos:**
- Nuevo permiso canónico en `Identidad.Domain.PermisosCanonicos`:
  `integraciones.aw.cotizaciones.correlacionar-manual`.
- **Separado** del de reintentar (más sensible — el operador asume la
  responsabilidad del `AwDocId`).

**Validación (FluentValidation):**
- `awDocId > 0`.
- `justificacion.Length` ≥ 20 y ≤ 1000.

**Estados permitidos:** `FailedDrop` únicamente. `FailedCorrelation`
retorna 409 (D7).

**Comportamiento interno:** llama
`MarcarCorrelacionadaDirectamente(awDocId, now, diagnosticLog: $"manual:{operadorId}|{justificacion}", permitirDesdeFailedDrop: true)`,
emite `AwPedidoCorrelacionado` por Outbox, notifica SignalR.

### 4.8. UI

Frontend en
`frontend/src/features/integraciones-aw/cotizaciones/detalle/`:

- En la card de detalle, cuando `estado === "FailedDrop"` y
  `errorKind === "aw_processing_timeout"`, mostrar botón **"Correlacionar
  manualmente"** además del existente **"Reintentar"**.
- Sheet (slide-from-right, patrón Compras §6) con form:
  - Input numérico `awDocId` (requerido).
  - Textarea `justificacion` (requerido, min 20).
  - Aviso visible: "Esta acción asume que A+W ya creó el documento. Si no
    estás seguro, usa Reintentar."
- Permiso `integraciones.aw.cotizaciones.correlacionar-manual` en el
  guard del botón.

---

## 5. Plan de PRs

Branch base: `feature/integraciones-aw-late-reconciliation`.

| PR | Título | Scope | Dependencias |
|---|---|---|---|
| **PR-1** | `feat(integraciones-aw): Envio.Filename + archivado robusto en stuck` | D5 + G2. Migración `AddFilenameToEnvio`. Cambio en `AwDropWorker` (poblar filename). Cambio en `on-prem/aw-drop-service` (archivar siempre con outcome en el naming). Tests unit + integration en `PerEdiFlowTests`. | Ninguna. Foundation. |
| **PR-2** | `feat(aw-drop-service): GET /completions endpoint` | Endpoint nuevo en drop service. Parser ya existe. Tests `CompletionsEndpointTests`. | PR-1 (archivado consistente). |
| **PR-3** | `feat(integraciones-aw): IAwCompletionsReader + HttpAwCompletionsAdapter` | Puerto + adapter. Tests de contrato. DI registration. | PR-2. |
| **PR-4** | `feat(integraciones-aw): AwLateReconciliationWorker + ampliar transiciones del aggregate` | Worker + flag `permitirDesdeFailedDrop` en `MarcarCorrelacionada/FalladaDirectamente`. Tests de aggregate. Tests del worker con `IAwCompletionsReader` mockeado. Métricas en `IntegracionesAwMeter`. Health check. Config en `IntegracionesAwOptions.LateReconciliation`. | PR-3. |
| **PR-5** | `feat(integraciones-aw): correlacionar-manual endpoint + UI` | Permiso canónico + migration de `IdentidadDbContext` (seed). Endpoint + handler + validator. Sheet UI. Tests E2E. | PR-4 (`permitirDesdeFailedDrop` ya existe en el aggregate). |

**Notas operativas:**

- PR-1 requiere coordinación con SER-DATA para deploy del drop service
  actualizado **antes** de que PR-2 entre en uso.
- PR-5 toca `IdentidadDbContext` con seed nuevo → atención a
  `PendingModelChangesWarning` (memoria
  [feedback_permisos_canonicos_migration](../../../../memory/feedback_permisos_canonicos_migration.md)).
- PR-4 introduce `DbContext` ya existente (`IntegracionesAwDbContext`),
  pero registra un nuevo `IHostedService` que hace `SaveChangesAsync` —
  verificar que `MigrationsHealthCheckOptions` y el deploy YML estén
  alineados (memoria [feedback_dbcontext_nuevo_checklist](../../../../memory/feedback_dbcontext_nuevo_checklist.md))
  — en este caso no hay DbContext nuevo, pero el checklist mental aplica.

---

## 6. Métricas y observabilidad

Agregar a `IntegracionesAwMeter`:

| Métrica | Tipo | Tags | Significado |
|---|---|---|---|
| `aw.cotizacion.late_correlated` | Counter | `lane`, `attempts` | Cotización recuperada vía Fase 1 logs con outcome=success. |
| `aw.cotizacion.late_failed` | Counter | `lane`, `error_kind` | Cotización transitada a `FailedCorrelation` por log tardío con outcome=failed. |
| `aw.cotizacion.late_reconciliation.misses` | Histogram | — | Cotizaciones en `FailedDrop` con timeout fuera de ventana de 24h, sin match en logs. Señal de alerta: si > 0 sostenido, abrir Fase 2 (SQL fallback). |
| `aw.cotizacion.late_reconciliation.errors` | Counter | `error_type` | Errores en el ciclo del worker (HTTP al drop service, DB, etc.). |
| `aw.cotizacion.correlacionar_manual` | Counter | `operador_id` | Uso del endpoint G3. Volumen alto = la automática está fallando. |

**Dashboards (Application Insights):**
- Panel "A+W late reconciliation" en el dashboard del módulo.
- Alerta: `late_reconciliation.misses` > 0 por más de 6h → email a
  Eduardo.

**Logs estructurados:**
- `AwLateReconciliationWorker`: `cycle_started`, `candidatas_count`,
  `matches_count`, `cycle_completed_ms`.
- Por cotización reconciliada: `LogInformation` con `cotizacion_id`,
  `quote_reference`, `aw_doc_id`, `lane`, `delay_seconds`.

---

## 7. Out of scope (Fase 2 y posteriores)

Lo siguiente queda explícitamente fuera de este feature. Solo se abren
si la telemetría de Fase 1 lo justifica:

1. **SQL fallback (`IAwSqlReader` para reconciliación tardía).** Si
   `late_reconciliation.misses` es persistentemente > 0 y la causa
   raíz es pérdida de logs (rotación antes de archivado), agregar
   lookup SQL como segundo intento.
2. **Push del drop service hacia el ERP** (modelo callback). Requiere
   abrir conexión inbound desde on-prem al cloud.
3. **Subir `WaitTimeoutSeconds` del drop service más allá de 120s.**
   Solo si p95 de A+W sostenidamente lo supera.
4. **Lease multi-instancia del worker.** Solo si escalamos el API a >1
   instancia en runtime.
5. **GC automatizado de `Processed\` desde el drop service.** Hoy lo
   resuelve scheduled task externa.
6. **Notificación al Glass Agent cuando ocurre una reconciliación
   tardía.** El push SignalR existente (`AgentRealtimePublisher`) ya lo
   cubre via `AwPedidoCorrelacionado`.

---

## 8. Riesgos y mitigaciones

| Riesgo | Probabilidad | Impacto | Mitigación |
|---|---|---|---|
| Customizing A+W cambia el formato del log y rompe `AwLogParser`. | Baja | Alta — pero ya rompe el flow normal también, no es nuevo. | Tests de regresión del parser con samples reales en `AwLogParserTests`. |
| Carrera entre customizing escribiendo `last_batch.log` y `TryCopyToProcessed` leyéndolo. | Media | Bajo — `TryCopyToProcessed` ya es best-effort. | Comment existente en [AwLogWatcher.cs:106-112](../../../on-prem/aw-drop-service/AwLogWatcher.cs#L106-L112) ya describe la mitigación (COPY no MOVE). Mantenemos. |
| Worker reconcilía un envío "viejo" (>24h con `FailedAt` reciente por reintento). | Baja | Medio — falso positivo. | Match por `Filename` del último `Envio` evita esto; filename incluye timestamp del reintento. |
| `Processed\` crece sin cota si scheduled task de GC falla. | Media | Bajo (disco) — Medio (latencia de `/completions`). | Health check del drop service incluye check de `disk_free_pct` < 10%. |
| Operador correlaciona manualmente con `AwDocId` incorrecto. | Baja | Alta — corrompe trazabilidad. | Justificación obligatoria (min 20 chars), permiso separado del de reintentar, audit log con `operadorId`. UI agrega aviso. |

---

## 9. Caso de prueba: Q-2026-00346

La cotización que motivó este feature debe quedar resuelta así una vez
mergeado el paquete completo:

1. **Hoy (sin feature):** cotización en `FailedDrop` con
   `LastError=[aw_processing_timeout] …`. El log activo de A+W ya fue
   sobreescrito (no hay `Processed\` para esta porque G2 aún no estaba
   activo). Único path: G3 manual con `AwDocId` que el operador busca
   en la UI nativa de A+W.
2. **Después del feature, escenario futuro equivalente:**
   - El drop service ya habría archivado el log tarde como
     `cot_NIN_Q-2026-00346-…_success_….log` en `Processed\`.
   - El worker, en su ciclo siguiente al procesamiento tardío, llamaría
     `GET /completions?since={FailedAt}`, encontraría el match por
     filename y aplicaría `MarcarCorrelacionadaDirectamente` con el
     `AwDocId` real.
   - El frontend recibe push SignalR y la UI cambia de "Falló entrega" a
     "Correlacionada con doc 31247" sin intervención humana.

---

## 10. Aprobación

- [ ] Eduardo Paredes — owner del módulo.
- [ ] Revisor técnico (TBD).

Una vez aprobado, este documento queda como referencia de implementación
y se abren los PR-1 a PR-5 en el orden indicado.
