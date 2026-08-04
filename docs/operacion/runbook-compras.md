# Runbook operacional — Módulo Compras / Requisiciones

Playbooks para incidentes operacionales del módulo Compras. Cada
sección es un escenario auto-contenido: síntoma → diagnóstico →
remediación → verificación → escalación.

> **Audiencia:** oncall del back-office (DevOps + dev del módulo).
> **Prerrequisitos:** acceso a Azure Portal (App Service + Application
> Insights + PostgreSQL Flexible Server), `psql` con credenciales del
> ambiente, `kubectl`/Azure CLI según despliegue, este repo en local
> para correr scripts de `tools/`.

## 0. Tabla de contenidos

1. [Outbox publisher atorado](#1-outbox-publisher-atorado)
2. [Reservas huérfanas](#2-reservas-huérfanas)
3. [409 `IDEMPOTENCY_IN_PROGRESS` frecuente](#3-409-idempotency_in_progress-frecuente)
4. [422 `IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY`](#4-422-idempotency_key_reused_with_different_body)
5. [Bandeja P95 degradada](#5-bandeja-p95-degradada)
6. [Health `ready=503`](#6-health-ready503)
7. [Spot-check post-deploy](#7-spot-check-post-deploy)
8. [Hub SignalR caído / latencia degradada](#8-hub-signalr-caído--latencia-degradada)
9. [Quick reference de KQL](#9-quick-reference-de-kql)

---

## 1. Outbox publisher atorado

### Síntoma
- Métrica `compras.outbox.pending` (App Insights) crece de manera
  sostenida sin volver a cero.
- Consumidores corriente abajo (cualquier consumer de los 6 integration
  events de Compras) reportan que dejaron de recibir mensajes.
- KQL devuelve filas con `published_at IS NULL` con `attempts > 0` y
  edad > 5 min.

### Diagnóstico
1. **¿Está corriendo el worker?**
   ```bash
   az webapp log tail --resource-group rg-millet-prod-mxc-01 \
       --name app-millet-prod-mxc-01 \
       | grep -i "OutboxPublisherWorker"
   ```
   Buscar el log inicial `"OutboxPublisherWorker<ComprasDbContext> iniciado"`. Si no aparece, el worker no arrancó (problema de bootstrap).

2. **¿El worker está deshabilitado por config?**
   - Setting `Outbox:Disabled=true` lo apaga. Verificar en Azure App
     Service → Configuration → Application settings que **NO** existe.

3. **¿Service Bus está sano?**
   ```bash
   az servicebus namespace show \
       --resource-group rg-millet-prod-mxc-01 \
       --name sb-millet-prod-mxc-01 \
       --query "status"
   # Esperado: "Active"
   ```

4. **¿Hay filas en dead-letter pasivo?** (`attempts > MaxAttempts`)
   ```sql
   SELECT id, event_type, attempts, last_error, occurred_at
   FROM compras.integration_events_outbox
   WHERE published_at IS NULL AND attempts > 10
   ORDER BY occurred_at DESC
   LIMIT 50;
   ```

### Remediación

**Caso A — worker no corriendo:** restart del App Service (Azure Portal
→ Restart). El `BackgroundService` se inicia con la app.

**Caso B — Service Bus down:** sin acción del lado Compras hasta que
Service Bus vuelva. El worker reintenta cada `Outbox:PollIntervalSeconds`
(default 10s) y los `attempts` se incrementan. Una vez el bus vuelva,
se procesa solo.

**Caso C — dead-letter pasivo (attempts > 10):** inspeccionar el
`last_error`. Si es transitorio (ej. throttling), bajar el counter y
reintentar:
```sql
UPDATE compras.integration_events_outbox
SET attempts = 0, last_error = NULL
WHERE id IN ('<id1>', '<id2>')   -- IDs específicos, NO bulk
  AND published_at IS NULL;
```
Si el error es permanente (payload mal formado, schema mismatch),
escalar a dev-Compras: probablemente requiere ADR de migración o
patch al integration mapper.

### Verificación
- `compras.outbox.pending` baja a cero en ≤ 5 min.
- Logs del worker: `"Procesando N eventos de outbox"` con N > 0.
- Consumer corriente abajo confirma recepción.

### Escalación
- 30 min sin resolución → escalar a líder de plataforma (`<Outbox>`
  cerrado en F6-PR4 — la infra es de plataforma, no exclusiva de Compras).

---

## 2. Reservas huérfanas

### Contexto
Cuando una requisición es `Autorizada`, el handler reserva stock en
Almacén vía `IReservarStockPort` (TTL configurable, default 14 días).
Si la RQ se queda en `EnSurtido` sin avanzar y nadie cancela, la
reserva expira y queda como huérfana en Almacén.

> **Estado en F8 (Phase 1):** Almacén es un *stub in-memory* — las
> reservas viven en proceso, se pierden al restart, no hay tabla persistente.
> Este playbook aplica cuando Almacén real reemplace el stub
> (ticket `<StubsTeardown>`, pendiente).

### Síntoma
- Almacén reporta stock "reservado" superior al real disponible.
- KQL: alto volumen de reservas con `expires_at < now()` sin un
  evento `RequisicionCancelada` correspondiente.

### Diagnóstico
1. **Identificar las RQs problema** (en Compras; consulta a la BD del
   módulo cuando los stubs sean reemplazados):
   ```sql
   SELECT r.id, r.folio, r.estado, r.fecha_solicitud, l.reserva_id, l.articulo_id
   FROM compras.requisiciones r
   JOIN compras.requisicion_lineas l ON l.requisicion_id = r.id
   WHERE r.estado = 3   -- EnSurtido
     AND r.fecha_solicitud < now() - interval '14 days'
     AND l.reserva_id IS NOT NULL;
   ```

2. **Corroborar con Almacén** que las reservas listadas siguen vivas
   ahí (consulta del lado Almacén — ver runbook propio cuando exista).

### Remediación
**Si la RQ ya se atendió por otra vía** (ej. compra urgente fuera de
sistema, A+W creó la salida directamente):
- Cancelar la RQ desde el ERP: `POST /api/v1/compras/requisiciones/{id}/cancelar`
  con motivo `"COMPRA_FUERA_DE_SISTEMA"` (motivo ya seedeado).
- El handler de cancelar libera todas las reservas vía
  `ILiberarReservaPort` (idempotente; si ya estaban liberadas, no-op).

**Si la RQ debe seguir viva** (ej. esperando OC del proveedor):
- Renovar la reserva manualmente desde Almacén — playbook del módulo
  Almacén (cuando exista).
- Considerar bumpear el TTL en `Compras:ReservaTtlDays` para casos
  legítimos.

### Verificación
- KQL `compras.reserva.expirada` (a futuro) cae a cero.
- Stock disponible en Almacén refleja inventario real.

### Escalación
- Producción ANTES de stubs reemplazados → no aplica (estado solo en
  proceso). Restart del App Service limpia todo. Documentar incidente
  para que el reemplazo de stubs incluya tabla persistente.

---

## 3. 409 `IDEMPOTENCY_IN_PROGRESS` frecuente

### Síntoma
- Alerta App Insights: `customDimensions.code == "IDEMPOTENCY_IN_PROGRESS"`
  > 5 por minuto sostenido.
- Usuarios reportan "el botón Guardar no responde, tengo que esperar 5s".

### Causas posibles
1. **Doble-click legítimo del usuario** — el cliente ya manda la misma
   `Idempotency-Key` y el segundo request choca con el primero en
   estado `processing`. Esperado y benigno hasta cierto volumen.
2. **Cliente HTTP bugged** que reintenta agresivamente (fetch antes de
   recibir respuesta del primero).
3. **Handler downstream lento** (>1s sostenido) — el segundo request del
   cliente legítimo llega antes del primero termine.

### Diagnóstico
```kusto
traces
| where timestamp > ago(1h)
| where customDimensions.code == "IDEMPOTENCY_IN_PROGRESS"
| extend endpoint = tostring(customDimensions.RequestPath)
| summarize count(), distinct_users = dcount(tostring(customDimensions.UsuarioId))
  by endpoint, bin(timestamp, 5m)
| render timechart
```

- **Pocos usuarios, muchos hits** → bug en cliente (1 user spammeando).
- **Muchos usuarios, hits proporcionales** → causa #1 o #3.
- Cruzar con latencia del endpoint (ver §5):
  ```kusto
  dependencies
  | where timestamp > ago(1h) and name == "Compras.Autorizar"
  | summarize p95 = percentile(duration, 95) by bin(timestamp, 5m)
  | render timechart
  ```

### Remediación
**Causa #1 (legítimo):** monitorear; aceptable hasta ~10/min. Considerar
ajustar `Idempotency:RetryAfterSeconds` (default 5) más bajo si los
handlers son rápidos.

**Causa #2 (bug cliente):** identificar el usuario via `UsuarioId` →
levantar bug ticket al frontend. Mientras tanto, no requiere acción
del backend.

**Causa #3 (handler lento):** el SLO de Autorizar es P95 < 1500 ms
(ver `dashboards-compras.md`). Si está degradado:
- Verificar plan de queries (§5).
- Verificar Service Bus + Almacén stubs ports en logs.

### Verificación
- Tasa baja a niveles esperados.
- P95 del handler en rango.

---

## 4. 422 `IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY`

### Síntoma
- **Cualquier ocurrencia es una alerta** (es un bug, no un fenómeno
  esperado).
- KQL: `customDimensions.code == "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY"`.

### Significado
El cliente HTTP está reusando la misma `Idempotency-Key` para requests
con bodies distintos. Indica:
- Bug en `useFormIdempotencyKey` (frontend) — la key se mantiene viva
  más allá del scope del formulario.
- O un cliente externo (script, Postman) reusando keys mal.

### Diagnóstico
```kusto
traces
| where timestamp > ago(24h)
| where customDimensions.code == "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY"
| project timestamp, UsuarioId = customDimensions.UsuarioId,
          UserAgent = customDimensions.["http.user_agent"],
          endpoint = customDimensions.RequestPath
| order by timestamp desc
```

- Filtrar por `UserAgent` para identificar si viene del SPA o de un
  cliente externo.

### Remediación
- **SPA:** levantar bug en frontend con repro steps. El usuario afectado
  debe refrescar la página (genera nueva key) hasta que se parche.
- **Cliente externo:** contactar al integrador.

### Verificación
- Tasa cae a cero post-fix.

---

## 5. Bandeja P95 degradada

### Síntoma
- KQL: P95 de `GET /api/v1/compras/requisiciones` o `/pendientes-autorizacion`
  > 200 ms sostenido > 10 min.
- Usuarios reportan "la lista tarda en cargar".

### Diagnóstico
1. **¿Cuántas RQs hay por empresa?**
   ```sql
   SELECT empresa_id, count(*) FROM compras.requisiciones
   GROUP BY empresa_id ORDER BY 2 DESC LIMIT 10;
   ```

2. **¿El plan de query usa el índice esperado?**
   ```sql
   EXPLAIN (ANALYZE, BUFFERS)
   SELECT id, folio, estado, fecha_solicitud
   FROM compras.requisiciones
   WHERE empresa_id = '<empresa>'
     AND estado = 1
     AND deleted_at IS NULL
   ORDER BY fecha_solicitud DESC
   LIMIT 50;
   ```
   Esperado: `Index Scan using ix_requisiciones_empresa_id_estado_fecha_solicitud`.
   Si aparece `Seq Scan` o `Sort` con > 1 MB de memoria, hay problema.

3. **¿`ANALYZE` reciente?** Postgres autovacuum corre solo, pero tras
   bulk inserts puede tardar. Forzar:
   ```sql
   ANALYZE compras.requisiciones;
   ```

### Remediación
- **Estadísticas obsoletas (#3):** `ANALYZE` y revalidar.
- **Plan no usa índice esperado:** revisar selectividad. Si una
  empresa concentra > 80% de las RQs, el planner puede preferir seq
  scan. Considerar índice condicional o partition por empresa (último
  recurso, ADR nuevo).
- **Volumen real excede capacidad de los índices actuales** (`> 1M`
  RQs por empresa): aplicar A14 (read model proyectado) según el
  diseño § 8.2 — fuera de scope de hot-fix; planear iteración.

> Referencia completa: `tools/perf/bench-bandejas.md`.

### Verificación
- Re-correr bench (`tools/perf/seed-10k-rqs.ps1` + curl).
- KQL P95 vuelve bajo SLO.

---

## 6. Health `ready=503`

### Síntoma
- App Service health probe falla → instancia sale del pool.
- `GET /health/ready` → 503.

### Diagnóstico
```bash
curl -s https://app-millet-prod-mxc-01.azurewebsites.net/health \
    -H "Authorization: Bearer $(az account get-access-token --query accessToken -o tsv)" \
    | jq .
```

> El endpoint `/health` (detalle) requiere permiso `infra.health.leer`.
> Para ver sin auth, el log del App Service muestra el motivo en cada check.

### Causas comunes
- **`postgres` check fail:** PG no responde → Azure portal → PostgreSQL
  Flexible Server → Status. Reintenta autoescalado de Azure si Service
  Tier saturó.
- **`migrations_applied` check fail:** una de las 4 DbContexts (Compartido,
  Core, Identidad, Compras) tiene una migration pending. Aplicar:
  ```bash
  dotnet ef database update \
      --project backend/src/Compras/Millet.Compras.csproj \
      --startup-project backend/src/Api/Millet.Api.csproj \
      --context ComprasDbContext \
      --connection "<Postgres>"
  ```
  El deploy pipeline corre esto automáticamente; un fallo aquí indica
  rollback parcial o conflicto de migrations.

### Remediación
- **Caso PG down:** sin acción Compras hasta que vuelva. Health vuelve
  solo.
- **Caso migration pending:** aplicar manualmente como arriba; alertar
  al equipo de release que el deploy quedó inconsistente.

### Verificación
- `/health/ready` → 200 OK.
- Probe de App Service vuelve a verde.

---

## 7. Spot-check post-deploy

Cada release a producción debe pasar este smoke test antes de declarar
el deploy estable. Tarda < 5 min.

### Checklist

```bash
# 1. Health
curl -fsS https://<host>/health/live
curl -fsS https://<host>/health/ready

# 2. Auth (con un usuario de prueba dedicado)
TOKEN=$(curl -fsS -X POST https://<host>/api/auth/sesion \
    -H "Content-Type: application/json" \
    -d '{"entraToken":"'"$TEST_ENTRA_TOKEN"'"}' \
    | jq -r .accessToken)

# 3. Catálogos respondiendo (lectura, no modifica nada)
curl -fsS -H "Authorization: Bearer $TOKEN" \
    "https://<host>/api/v1/catalogos/proveedores?limit=1" | jq '.total'

# 4. Bandeja respondiendo
curl -fsS -H "Authorization: Bearer $TOKEN" \
    "https://<host>/api/v1/compras/requisiciones?limit=1" | jq '.total'

# 5. CollaborationHub — handshake del hub valida el wiring SignalR
#    (Sprint Buffer): el endpoint /hubs/compras/negotiate es un POST
#    HTTP normal del protocolo SignalR; sin token responde 401, con token
#    responde 200 con { "negotiateVersion", "connectionId", ... }.
curl -fsS -X POST "https://<host>/hubs/compras/negotiate?negotiateVersion=1" \
    | grep -q "401" && echo "hub auth OK"

curl -fsS -X POST -H "Authorization: Bearer $TOKEN" \
    "https://<host>/hubs/compras/negotiate?negotiateVersion=1" \
    | jq -r '.connectionId // empty' | grep -q . && echo "hub negotiate OK"

# 6. Crear RQ + transmitir + autorizar (en empresa de smoke,
#    NO en empresa real de operación). Ver tools/perf/bench-bandejas.md
#    para el flujo completo.
```

### Criterio de aceptación
Los 5 primeros endpoints retornan 2xx (o 401 esperado en el primer hub
check). Los logs del periodo del smoke no muestran `INTERNAL_ERROR` ni
5xx. App Insights no dispara alertas. El check `signalr_hub` aparece en
el JSON de `/health` con `mode=azure-signalr` (o `mode=in-process` en
ambientes pre-prod sin la setting de Key Vault wireada).

### Si el spot-check falla
- Marcar el deploy como sospechoso (no declarar estable).
- Capturar logs + correlation_ids.
- Decidir rollback (`az webapp deployment slot swap` revierte) o
  hot-fix dependiendo de severidad.

---

## 8. Hub SignalR caído / latencia degradada

### Síntoma
- Alerta `alert-signalr-system-errors-{env}` (Azure Monitor) — severidad 1.
- Alerta `alert-signalr-server-load-{env}` — severidad 2.
- Usuarios reportan "no veo a Pedro editando" o el indicador de presencia
  se queda colgado mostrando usuarios que ya no están.
- KQL en App Insights muestra spike de errores de tipo `HubException` o
  `ConnectionAborted` en los traces del módulo Compras.

### Diagnóstico

1. **¿Está vivo el SignalR Service?**
   ```bash
   az signalr show \
       --resource-group rg-millet-prod-mxc-01 \
       --name signalr-millet-prod-mxc-01 \
       --query "{provisioningState: provisioningState, externalIP: externalIP, status: properties.provisioningState}"
   ```
   Esperado: `provisioningState=Succeeded`. Si está en `Failed`, escalar
   a Azure Support — fuera de nuestra control.

2. **¿El App Service llega al SignalR?**
   ```bash
   az webapp log tail --resource-group rg-millet-prod-mxc-01 \
       --name app-millet-prod-mxc-01 \
       | grep -iE "signalr|hub|backplane"
   ```
   Buscar errores tipo `Connection refused`, `Authentication failed` o
   `BackplaneException`.

3. **¿La connection string en Key Vault está vigente?**
   ```bash
   az keyvault secret show \
       --vault-name kv-millet-prod-mxc-01 \
       --name signalr-connection-string \
       --query "{enabled: attributes.enabled, expires: attributes.expires}"
   ```
   Si `enabled=false` o `expires` ya pasó, rotar (ver §8.4).

4. **¿Cuántas conexiones activas vs SKU?**
   ```kusto
   AzureMetrics
   | where TimeGenerated > ago(1h)
   | where ResourceProvider == "MICROSOFT.SIGNALRSERVICE"
   | where MetricName == "ConnectionCount"
   | summarize avg(Average) by bin(TimeGenerated, 5m)
   ```
   En dev (Free F1) el límite duro es 20 conexiones; en prod (Standard S1)
   es 1000 por unit. Si `Average` >= 90% del límite → escalar SKU.

5. **¿Métricas custom del hub indican el problema?**
   ```kusto
   customMetrics
   | where timestamp > ago(1h)
   | where name in ("softlock.tracked", "softlock.released", "softlock.expired",
                    "hub.connections.active")
   | summarize sum(value) by name, bin(timestamp, 5m)
   | render timechart
   ```
   Patrones comunes:
   - `softlock.expired` >> `softlock.released` (e.g. 10x): clientes no
     mandan heartbeats — bug en FE o problema de red. Investigar
     `useCollaboration` / cliente SignalR.
   - `hub.connections.active` colapsa a 0 sostenido: backplane caído o
     todos los clientes desconectados — confirmar con el step #1.

### Remediación

- **8.1 SignalR Service degradado / con errores del sistema:**
  Sin intervención local — Azure Service Health debe estar reportando.
  Comunicar status a stakeholders ("colaboración en tiempo real
  temporalmente offline"). El módulo sigue funcionando en modo
  monoeditor (Capa 1 de concurrencia protege; el lost-update se reporta
  via 409). UAT/operación NO se debe bloquear.

- **8.2 Connection string rotada/expirada:**
  ```bash
  # Obtener nueva primary key
  az signalr key list \
      --resource-group rg-millet-prod-mxc-01 \
      --name signalr-millet-prod-mxc-01 \
      --query primaryConnectionString \
      -o tsv

  # Actualizar el secret
  az keyvault secret set \
      --vault-name kv-millet-prod-mxc-01 \
      --name signalr-connection-string \
      --value "<nueva-connection-string>"

  # Restart App Service para que recargue la setting (Key Vault references
  # se cachean ~24h; restart fuerza el refresh)
  az webapp restart \
      --resource-group rg-millet-prod-mxc-01 \
      --name app-millet-prod-mxc-01
  ```

- **8.3 SKU saturado (server load > 80% sostenido):**
  - **Dev (Free F1):** revisar si hay tests de carga sin terminar; el
    límite duro de 20 conexiones se choca rápido. No escalar dev.
  - **Prod (Standard S1):** subir a `Standard_S1` con `capacity: 2`
    (2000 conexiones) o cambiar a `Premium_P1`. Editar
    `infra/parameters/prod.bicepparam` o `infra/main.bicep` línea ~241
    y redesplegar:
    ```bash
    az deployment sub what-if \
        --location mexicocentral \
        --template-file infra/main.bicep \
        --parameters infra/parameters/prod.bicepparam
    az deployment sub create \
        --location mexicocentral \
        --template-file infra/main.bicep \
        --parameters infra/parameters/prod.bicepparam
    ```

- **8.4 Clientes con `softlock.expired` alto:**
  No es problema de hub server — el FE no está mandando heartbeats. Bug
  en `useCollaboration` o problema de red entre el navegador y el SignalR
  Service. Capturar:
  - `customDimensions.UsuarioId` de los users con expirations
  - Si todos están en una región o ISP → problema de red
  - Si están dispersos → bug del cliente (heartbeat interval, reconexión)

### Verificación
- `alert-signalr-system-errors-{env}` y `alert-signalr-server-load-{env}`
  vuelven a estado `Resolved` en Azure Monitor.
- `customMetrics | where name == "hub.connections.active"` muestra
  conexiones activas razonables (>0 si hay usuarios usando el hub).
- Spot-check manual: dos browsers distintos abren la misma requisición
  en QA — el indicador de presencia aparece en < 5s en ambos.

### Escalación
- **Falla del Azure SignalR Service en sí:** abrir ticket en Azure
  Support, severidad B (Service degradation). Mientras: documentar
  ventana de degradación en el Slack #ops y notificar al owner.
- **Falla persistente del hub en código nuestro tras rotar la conn string
  y restart:** rollback al deploy anterior (`az webapp deployment slot
  swap` si hay slots).

---

## 9. Quick reference de KQL

Las queries más usadas viven en `dashboards-compras.md`. Resumen:

| Caso | Query corta |
|---|---|
| Errores recientes | `traces \| where customDimensions.code != "" \| top 50 by timestamp desc` |
| Latencia p95 Autorizar | `dependencies \| where name == "Compras.Autorizar" \| summarize percentile(duration, 95) by bin(timestamp, 5m)` |
| Cuántos eventos publicados/min | `dependencies \| where target contains "servicebus" \| summarize count() by bin(timestamp, 1m)` |
| Tasa 409 idempotency | `traces \| where customDimensions.code == "IDEMPOTENCY_IN_PROGRESS" \| count` |
| Audit por RQ | `union dependencies, traces \| where customDimensions.RequisicionId == "<id>"` |

---

## Cambios y firma

| Fecha | Autor | Cambio |
|---|---|---|
| 2026-05-09 | F8-PR4 | Versión inicial. Cubre Fase 8. |
| 2026-05-09 | CollaborationHub Sprint 3 | Sección 8 nueva: Hub SignalR caído / latencia. Suma KQL para métricas custom (`softlock.tracked/released/expired`, `hub.connections.active`). |
| 2026-05-09 | CollaborationHub Sprint Buffer | Sección 7 (spot-check post-deploy) suma 2 curls al `/hubs/compras/negotiate` (con y sin token). El criterio de aceptación cita el check `signalr_hub` del `/health/ready`. |

> **Pendiente de firma del owner** (Eduardo Paredes) antes de declararse
> oficial — se firma en el merge del PR.

> Cuando llegue Almacén real, ampliar §2 (reservas huérfanas) con
> consultas a tabla persistente. Cuando llegue Cuentas por Pagar,
> agregar §9 (eventos pendientes en outbox por consumer down).
