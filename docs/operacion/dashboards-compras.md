# Dashboards y queries de operación — Módulo Compras

Este documento contiene las queries Kusto (KQL) base para Application
Insights / Azure Monitor que el equipo de operación usa para investigar
problemas en el módulo Compras. Wireado en F8-PR2 (ADR-0006).

> **Convención de custom dimensions** (todas pushadas vía Serilog):
> - `EmpresaId` — tenant. Pushado por `RequestContextLoggingMiddleware` post-auth.
> - `UsuarioId` — Sub claim del JWT. Mismo middleware.
> - `RequisicionId` — Pushado por los handlers de comandos que mutan
>   requisiciones (F8-PR2: Autorizar, Cancelar, RegistrarRecepcion;
>   futuros handlers replican el patrón).
> - `TraceId` / `SpanId` — Auto-incluidos por OpenTelemetry.

> **`ActivitySource: Millet.Compras`** registrado en F8-PR2. Spans:
> - `Compras.Autorizar` — tags `compras.requisicion.id`, `compras.autorizacion.nivel`,
>   `compras.estado.antes`, `compras.estado.despues`.
> - `Compras.Cancelar` — mismos tags base.
> - `Compras.RegistrarRecepcion` — tags `compras.requisicion.id`,
>   `compras.linea.id`, `compras.estado.antes`, `compras.estado.despues`.

---

## 1. Idempotency — health del middleware (F8-PR1)

### 1.1 Tasa de IDEMPOTENCY_BODY_MISMATCH (alerta si > 0 sostenido)

```kusto
traces
| where timestamp > ago(1h)
| where customDimensions.code == "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY"
| summarize count() by bin(timestamp, 5m), tostring(customDimensions.UsuarioId)
| render timechart
```

### 1.2 Conflictos IN_PROGRESS por endpoint (debería bajar con tiempo)

```kusto
traces
| where timestamp > ago(24h)
| where customDimensions.code == "IDEMPOTENCY_IN_PROGRESS"
| summarize count() by bin(timestamp, 1h), tostring(customDimensions.RequestPath)
| render timechart
```

### 1.3 Cache hit ratio (replays exitosos)

```kusto
requests
| where timestamp > ago(1h)
| where url contains "/api/v1/compras/"
| extend replayed = tostring(customDimensions.["Idempotent-Replayed"]) == "true"
| summarize total = count(), replays = countif(replayed) by bin(timestamp, 5m)
| extend hit_ratio_pct = round(100.0 * replays / total, 1)
| render timechart
```

---

## 2. Latencia de operaciones críticas

### 2.1 P50/P95/P99 de Autorizar (incluye bifurcación stock-aware)

```kusto
dependencies
| where timestamp > ago(24h)
| where name == "Compras.Autorizar"
| summarize
    p50 = percentile(duration, 50),
    p95 = percentile(duration, 95),
    p99 = percentile(duration, 99),
    count_total = count()
  by bin(timestamp, 15m)
| render timechart
```

> **SLO objetivo (F8-PR3 confirmará benchmark)**: p95 < 1500 ms.
> Si p95 > 2000 ms sostenido, abrir incident — probable contención
> en `IConsultarStockPort` o transacción larga.

### 2.2 Cancelar — latencia y tasa de CANCELAR_FALLO

```kusto
dependencies
| where timestamp > ago(24h)
| where name == "Compras.Cancelar"
| join kind=leftouter (
    traces
    | where customDimensions.code == "CANCELAR_FALLO"
    | project operation_Id, fallo_msg = message
  ) on operation_Id
| summarize
    p95 = percentile(duration, 95),
    fallos = countif(isnotempty(fallo_msg)),
    total = count()
  by bin(timestamp, 1h)
| extend fallo_pct = round(100.0 * fallos / total, 1)
| render timechart
```

### 2.3 Recepción — tiempo desde EnSurtido a Cerrada por RQ

```kusto
dependencies
| where timestamp > ago(7d)
| where name == "Compras.RegistrarRecepcion"
| where customDimensions.["compras.estado.despues"] == "Cerrada"
| extend req_id = tostring(customDimensions.["compras.requisicion.id"])
| summarize cierre_at = max(timestamp), duration_total_ms = sum(duration) by req_id
```

---

## 3. Errores de negocio por endpoint

### 3.1 Top 10 códigos de error en Compras (últimas 24h)

```kusto
traces
| where timestamp > ago(24h)
| where operation_Name startswith "POST /api/v1/compras/"
| where customDimensions.code != ""
| summarize count() by tostring(customDimensions.code)
| top 10 by count_
| render barchart
```

### 3.2 BIFURCACION_FALLO — investigación por RequisicionId

Útil cuando un usuario reporta que "no pude autorizar".

```kusto
traces
| where timestamp > ago(2h)
| where customDimensions.code == "BIFURCACION_FALLO"
| project timestamp, message, RequisicionId = customDimensions.RequisicionId,
          UsuarioId = customDimensions.UsuarioId, operation_Id
| order by timestamp desc
```

Click en `operation_Id` te lleva al trace completo (requisición HTTP +
spans EF + spans de puertos cross-module).

---

## 4. Audit trail — quién hizo qué

### 4.1 Todas las mutaciones a una requisición específica

```kusto
let req_id = "00000000-0000-0000-0000-000000000000";  // <-- reemplazar
union dependencies, traces, requests
| where timestamp > ago(30d)
| where customDimensions.RequisicionId == req_id
   or customDimensions.["compras.requisicion.id"] == req_id
| project timestamp, name, operation_Name,
          UsuarioId = customDimensions.UsuarioId,
          EmpresaId = customDimensions.EmpresaId,
          message
| order by timestamp asc
```

### 4.2 Actividad por usuario en una empresa (últimas 24h)

```kusto
let usuario = "00000000-0000-0000-0000-000000000000";  // <-- reemplazar
requests
| where timestamp > ago(24h)
| where customDimensions.UsuarioId == usuario
| where url contains "/api/v1/compras/"
| project timestamp, url, resultCode, duration,
          RequisicionId = customDimensions.RequisicionId
| order by timestamp desc
```

---

## 5. Outbox + Service Bus (F6)

### 5.1 Eventos pendientes (no publicados)

```kusto
customMetrics
| where timestamp > ago(15m)
| where name == "compras.outbox.pending"
| summarize avg(value) by bin(timestamp, 1m)
| render timechart
```

### 5.2 Falla recurrente por evento

```kusto
traces
| where timestamp > ago(24h)
| where message contains "Falla publicando evento"
| extend event_type = extract(@"\\{EventType\\}\\s*=\\s*([^\\s]+)", 1, message)
| summarize fallos = count() by event_type, bin(timestamp, 1h)
| where fallos > 3
| render timechart
```

---

## 6. CollaborationHub — métricas del hub SignalR

Métricas custom emitidas por `ComprasHubMeter` (Sprint 3, ADR-0006). Las
exporta el AzureMonitor OTel Distro a `customMetrics` cuando el host tiene
`APPLICATIONINSIGHTS_CONNECTION_STRING` set.

> **Tags estándar:** `compras.hub.empresa.id`, `compras.hub.entidad`,
> `compras.hub.modo` (Viewing | Editing).

### 6.1 Conexiones activas al hub (delta)

```kusto
customMetrics
| where timestamp > ago(1h)
| where name == "hub.connections.active"
| summarize sum_delta = sum(value) by bin(timestamp, 1m)
| serialize active = row_cumsum(sum_delta)
| project timestamp, active
| render timechart
```

> Si `active` colapsa a 0 sostenido (y sigue habiendo usuarios en la
> aplicación) → backplane SignalR caído. Cruzar con
> `AzureMetrics | where MetricName == "ConnectionCount"`.

### 6.2 Soft locks: tracked vs released vs expired

```kusto
customMetrics
| where timestamp > ago(1h)
| where name in ("softlock.tracked", "softlock.released", "softlock.expired")
| summarize total = sum(value) by name, bin(timestamp, 5m)
| render timechart
```

> Indicador de salud:
> - `tracked` ≈ `released` (con offset de los heartbeat-vivos): normal.
> - `expired` >> `released - tracked-vivos`: clientes no mandan heartbeats
>   (red intermitente o bug en `useCollaboration` del FE).

### 6.3 Hot resources (recursos con más colaboración)

```kusto
customMetrics
| where timestamp > ago(24h)
| where name == "softlock.tracked"
| extend entidad = tostring(customDimensions["compras.hub.entidad"])
| summarize tracks = sum(value) by entidad, bin(timestamp, 1h)
| render timechart
```

### 6.4 Modo de colaboración (Viewing vs Editing)

```kusto
customMetrics
| where timestamp > ago(24h)
| where name == "softlock.tracked"
| extend modo = tostring(customDimensions["compras.hub.modo"])
| summarize total = sum(value) by modo, bin(timestamp, 1h)
| render columnchart
```

> Si `Editing` >> `Viewing` sostenido → el FE puede estar emitiendo
> EditingResource en exceso (e.g. en cada keystroke en lugar de en focus).

---

## 7. Pendientes para F8-PR3 / F8-PR4

- **Dashboard JSON exportable** (Workbook): consolidar las queries en una sola
  vista para oncall. Plan en F8-PR4 junto con el runbook.
- **Alertas configuradas en Azure Monitor**:
  - Tasa de `IDEMPOTENCY_BODY_MISMATCH` > 0 sostenida 5 min → P3.
  - p95 `Compras.Autorizar` > 2 s sostenida 10 min → P2.
  - Outbox pending > 50 sostenido 5 min → P2.
- **Métricas custom** en App Insights (counters de cache hits, in-progress,
  body mismatch). Queda para una iteración posterior; las queries arriba
  reconstruyen lo equivalente desde traces.
