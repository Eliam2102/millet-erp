# Diseño — Flujo asíncrono de descarga masiva SAT

> **Lee primero:** [`00-levantamiento.md`](00-levantamiento.md),
> [`01-diseno.md`](01-diseno.md),
> [`ADR-0038`](../../decisiones/0038-fiscalapi-pac-unico.md).
>
> **Versión:** 0.2 — 2026-05-25 (post-validación sandbox + SDK oficial).
> **Owner:** Eduardo Paredes — `eduardo.paredes@tiglass.net`.
> **Estado:** **APROBADO** — D1-D10 cerradas con evidencia del probe
> sandbox + lectura del SDK oficial. Listo para arrancar PR-9.

## Cambios desde v0.1

- Validación end-to-end contra `test.fiscalapi.com` con script PowerShell
  (ver `tools/fiscalapi-probe/`).
- Lectura del SDK oficial `Fiscalapi` en C# para confirmar contratos.
- 13 hallazgos nuevos documentados en §13.
- D1-D10 cerradas con evidencia, no especulación.
- **Decisión arquitectónica revisada**: Millet usa SDK NuGet oficial
  apuntando a `live.fiscalapi.com` en todos los ambientes — sandbox no
  soporta el flujo completo de descarga masiva.

---

## 0. TL;DR

El diseño actual modela la descarga masiva como un **endpoint REST
síncrono** (`DescargarRecibidosAsync(empresaId, request) → List<Cfdi>`),
pero el modelo real de FiscalAPI (validado contra
[docs.fiscalapi.com](https://docs.fiscalapi.com)) es **asíncrono con
estados estilo SAT**:

```
Crear Regla → Crear Solicitud → SAT genera paquetes (horas) →
FiscalAPI pollea cada 30 min → Solicitud queda "Terminada" →
Tú cosechas XMLs / metadata
```

Esto significa que `DescargaMasivaSatWorker`, `IFiscalApiClient`, el
modelo de dominio y las opciones de `ConfiguracionPac` están
conceptualmente desalineados con el vendor. **Hay que rediseñar** el
flujo de descarga (no el módulo entero — la admin UI, cifrado, refresh
worker y arquitectura hexagonal del módulo se conservan).

Este doc describe el nuevo modelo y el plan de migración.

---

## 1. Cómo se descubrió la brecha

PR-8 frontend (`#320`) cerró el master-detail del admin UI. Al revisar
end-to-end, el owner cuestionó si el proceso de descarga estaba bien
pensado.

Audit del 2026-05-25 — contraste código actual vs
[docs.fiscalapi.com](https://docs.fiscalapi.com):

| Doc oficial dice | Código actual asume |
|---|---|
| `POST /api/v4/download-requests` crea solicitud, devuelve ID; estado evoluciona en background. | Endpoint síncrono que devuelve `List<CfdiResumenSat>` en una sola llamada. |
| Cada `DownloadRule` cubre **una** combinación (Recibidos × Vigente). Para "Recibidos Vigentes + Cancelados" se necesitan **2 reglas**. | El worker no maneja reglas; mete todo en un solo request. |
| Ventana máxima **31 días** por solicitud CFDI/Metadata (400 días Retenciones). | `descargaBackfillHoras` permite hasta 168 (7d) — dentro del límite pero por razones equivocadas. |
| Estados: `downloadRequestStatusId` (1/2/3) × `satRequestStatusId` (0/1/2/3 con "3=Terminada"). | El worker asume "respuesta inmediata o falla". |
| Auth: dos headers `X-TENANT-KEY` + `X-API-KEY` + `X-TIME-ZONE`. | El `FiscalApiHttpClient` (placeholder) habla de un solo header. |
| Sandbox: `https://test.fiscalapi.com`. Prod: `https://live.fiscalapi.com`. | `base_url` por empresa libre — OK, pero el "test connection" pega a `/health` que no valida la api-key. |
| Reglas atadas a `personId` (un `Person` de FiscalAPI tiene RFC, certificados). | `RfcReceptor` solo guarda el RFC string — no hay vínculo con un `personId` de FiscalAPI. |
| Catálogo de tipos CFDI: `I/E/N/P/T`. `T` (Traslado) y `N` (Nómina) no son útiles para CxP. | El worker descarga todo y CxP filtra al parsear el XML. |
| Response envelope: `{data, succeeded, message, httpStatusCode, traceIdentifier}`. Paginación con `PagedList<T>` (`{items, pageNumber, totalPages, hasNextPage, ...}`). | El cliente espera arrays directos. |

Los `PLATFORM-TODO(<FiscalApi*Endpoint>)` siguen vivos en
`FiscalApiHttpClient.cs:74,96,136,187` — los paths son placeholders, los
formatos de request/response son inventados. **Nadie validó contra el
vendor real.**

---

## 2. Modelo real de FiscalAPI

### 2.1 Entidades

| Entidad | Endpoint base | Rol |
|---|---|---|
| **Person** | `/api/v4/people` | Receptor / emisor en FiscalAPI. Tiene `id` (UUID FiscalAPI), `tin` (RFC), `legalName`, `email`. Persona física o moral. |
| **TaxFile** | `/api/v4/tax-files` | Certificados fiscales (.cer + .key) asociados a un `Person`. Hoy documentados como **CSD** (para emisión). `[Gap]` ver §11/D2. |
| **DownloadRule** | `/api/v4/download-rules` | Plantilla de descarga. Vincula `personId` + `satQueryTypeId` + `downloadTypeId` + `satInvoiceStatusId`. Se crea una vez y se reutiliza. |
| **DownloadRequest** | `/api/v4/download-requests` | Instancia de descarga. Lleva `downloadRuleId` + `startDate` + `endDate`. Tiene estados, ID persistente, contador `invoiceCount`. |
| **Catalogs** | `/api/v4/download-catalogs` | Catálogos del SAT: `SatInvoiceTypes (I/E/N/P/T)`, estados, etc. |

### 2.2 Flujo end-to-end

```
[Aprovisionamiento — una vez por empresa]
  POST /api/v4/people       (si no existe Person de la empresa)
  POST /api/v4/tax-files    (sube CSD/FIEL — Gap D2)
  POST /api/v4/download-rules  ×N
    {personId, downloadTypeId: "Recibidos",
     satQueryTypeId: "Metadata" | "CFDI",
     satInvoiceStatusId: "Vigente"}

[Operación diaria — Submitter Worker]
  POST /api/v4/download-requests
    {downloadRuleId, startDate, endDate}      # ventana ≤ 31 días
  → response: {data: {id, downloadRequestStatusId: 1, ...}}
  Persistir SolicitudDescarga local con id externo, estado=Pendiente.

[Poll periódico — Poller Worker]
  GET /api/v4/download-requests/{id}
  → response: {data: {satRequestStatusId, downloadRequestStatusId,
                      invoiceCount, lastAttemptDate, nextAttemptDate}}
  Avanza FSM. Cuando downloadRequestStatusId == 3 (Terminada):

[Cosecha — mismo Poller Worker, al detectar Terminada]
  GET /api/v4/download-requests/{id}/meta-items?pageNumber=1&pageSize=50
  → response: {data: {items: [...], pageNumber, totalPages, hasNextPage}}
  Paginar hasta hasNextPage=false; enviar UUIDs a ICfdiIngestaPort.

[Sólo si necesitamos XML para algún UUID]
  GET /api/v4/download-requests/{id}/xmls?pageNumber=...
  o
  GET /api/v4/download-requests/{id}/package  (ZIP completo)
```

### 2.3 Diferencias clave vs lo asumido

- **No hay "checkpoint continuo".** Lo que hay son solicitudes
  independientes, cada una con su ventana. El "estado actual de
  descarga" se reconstruye listando solicitudes (no leyendo un cursor).
- **El SAT manda los paquetes "cuando quiere"** (horas, no segundos).
  FiscalAPI los pollea cada 30 min. Nuestro worker no necesita correr
  cada 6h — basta con 1× al día crear solicitudes y N× al día pollear
  hasta que estén listas.
- **3 tipos de query, no 2:**
  - `Metadata` — barato, sin XML. Bueno para enterarse rápido qué hay.
  - `CFDI` — XML parseado, más caro (más tiempo en SAT y más cuota).
  - `Retenciones` — comprobantes de retenciones (uso futuro).
- **Cada combinación = 1 regla.** Para cubrir "Vigentes + Cancelados"
  hay que hacer 2 solicitudes (o aceptar perder los Cancelados — ver
  D4).

### 2.4 Lo que FiscalAPI documenta pero el doc no especifica

| Tema | Estado |
|---|---|
| Rate limit (429) y `Retry-After` | `[Gap]` — validar con sandbox |
| Tiempo típico de SAT a "Terminada" | `[Gap]` — empíricamente "horas" |
| TTL de solicitudes (¿cuándo se borran del lado FiscalAPI?) | `[Gap]` |
| Si "Consultar estado de factura" (POST en `/invoices-by-reference`) sirve para reconciliación batch o solo individual | `[Gap]` — para refresh worker |
| Si el FIEL/e.firma es requerido o FiscalAPI lo provee | `[Gap]` — solo CSD documentado; D2 |
| Soporte explícito para REPP (complemento de pago) descarga masiva | Soportado como tipo P en catálogos; flujo de descarga no documentado aparte |

---

## 3. Brechas en el código actual

### 3.1 Conceptuales (bloqueantes)

1. **`IFiscalApiClient.DescargarRecibidosAsync` es síncrono.** Hay que
   partirlo en `CrearSolicitudAsync` + `ConsultarSolicitudAsync` +
   `ListarMetadataAsync` + `ListarXmlsAsync`.
2. **`DescargaMasivaSatWorker` asume "consumir respuesta inmediata".**
   No hay máquina de estados, no hay persistencia de `requestId`
   externo.
3. **`ConfiguracionPac.descarga_intervalo_segundos` y
   `descarga_backfill_horas` son sliders del modelo viejo.** El intervalo
   relevante es entre ticks del Poller, no entre creación de solicitudes
   (eso es típicamente 1×/día por regla). Quedan deprecated.
4. **`RfcReceptor.CheckpointDescargaAt` se asume "el último timestamp
   cubierto".** En el modelo real, la cobertura se reconstruye sumando
   las `[startDate, endDate]` de las `SolicitudDescarga` con estado
   `Terminada` o `Descargada`.
5. **`ConfiguracionPac.api_key_*` cifrado se conserva, pero hay que
   agregar `tenant_key_cifrado`.** Auth de FiscalAPI requiere AMBOS
   headers (`X-TENANT-KEY` + `X-API-KEY`), no solo uno.

### 3.2 Operativas (importantes)

6. **No hay endpoint admin "re-descarga manual"** — para auditoría /
   recuperación, hoy hay que tocar BD.
7. **No hay métricas** de "solicitudes abiertas por empresa", "atraso
   del último Terminada", "tasa de error".
8. **Test conexión actual le pega a `/health`** (probablemente anónimo).
   Falso positivo con key inválida.
9. **Conciliación mailbox vs descarga incompleta** — el adapter actual
   hace dedupe simple por UUID y omite la ingesta cuando ya existe; no
   crea registro `Duplicado` como dice CxP §9.4.
10. **Política de cancelación SAT post-pago no definida** — el refresh
    worker actual solo descarta si estado interno == `PorProcesar`.
11. **Polly policies (`retry`, `circuit_breaker`) hardcoded en DI**, no
    leen los valores de `ConfiguracionPac`. La UI de admin tiene sliders
    decorativos.

### 3.3 No mencionadas como deuda

12. REPP (complemento de pago, tipo P) — no se modela el ingreso de
    REPPs a CxP a partir del feed SAT.
13. Cancelación SAT con CFDI ya pagado — no hay flujo de alerta.
14. Reglas y solicitudes huérfanas en FiscalAPI si eliminamos una
    `RfcReceptor` del lado nuestro — no se hace cleanup remoto.

---

## 4. Nuevo modelo de dominio

### 4.1 Diagrama

```
Empresa (Identidad)
   │ 1
   │
   ▼ N
ConfiguracionPac  ──── 1:1 con (empresa, proveedor)
   │
   │ 1
   │
   ▼ N
RfcReceptor (existente — se conserva, se le agrega PersonIdExterno)
   │ 1
   │
   ▼ N
DownloadRuleExterna  ──── 1:1 con (rfc_receptor, satQueryType, downloadType, satInvoiceStatus)
   │ 1                    cachea el id de FiscalAPI
   │
   ▼ N
SolicitudDescarga         (FSM: Pendiente→EsperandoSat→Terminada→Cosechada→Cerrada / Error)
   │ 1
   │
   ▼ N
PaqueteMeta               (1 paquete = 1 page de meta-items de la solicitud)
   │
   │ N
   │
   ▼ N
CfdiResumen               (UUID + metadata; se entrega a ICfdiIngestaPort)
```

### 4.2 Tablas nuevas

```sql
-- Aprovisionamiento del Person en FiscalAPI por empresa+RFC.
-- Hoy RfcReceptor.PersonIdExterno guarda el id que devuelve FiscalAPI
-- la primera vez que se sincroniza.
ALTER TABLE integraciones_fiscal.rfcs_receptores
  ADD COLUMN person_id_externo text NULL;     -- id de FiscalAPI Person
ALTER TABLE integraciones_fiscal.rfcs_receptores
  ADD COLUMN aprovisionado_at timestamptz NULL;

-- Reglas creadas en FiscalAPI. UNA por combinación (queryType × statusSat).
-- En MVP: 1 sola regla por RFC con queryType=Metadata + statusSat=Vigente.
-- D4 abierta: ¿agregamos también una regla con statusSat=Cancelado para
-- detectar cancelaciones temprano sin tener que pegar al endpoint
-- "Consultar estado de factura" uno por uno?
CREATE TABLE integraciones_fiscal.download_rules_externas (
  id                       uuid PRIMARY KEY,
  empresa_id               uuid NOT NULL,
  rfc_receptor_id          uuid NOT NULL REFERENCES integraciones_fiscal.rfcs_receptores(id),
  rule_id_externo          text NOT NULL,        -- id de FiscalAPI download-rule
  sat_query_type           smallint NOT NULL,    -- 1=Metadata, 2=Cfdi, 3=Retenciones
  download_type            smallint NOT NULL,    -- 1=Recibidos, 2=Emitidos
  sat_invoice_status       smallint NOT NULL,    -- 1=Vigente, 2=Cancelado
  activa                   boolean NOT NULL DEFAULT true,
  created_at               timestamptz NOT NULL,
  updated_at               timestamptz NOT NULL,
  CONSTRAINT uq_download_rules_combo
    UNIQUE (rfc_receptor_id, sat_query_type, download_type, sat_invoice_status)
);

-- Solicitudes creadas y su estado FSM.
CREATE TABLE integraciones_fiscal.solicitudes_descarga (
  id                       uuid PRIMARY KEY,
  empresa_id               uuid NOT NULL,
  download_rule_id         uuid NOT NULL REFERENCES integraciones_fiscal.download_rules_externas(id),
  request_id_externo       text NOT NULL,         -- id de FiscalAPI download-request
  start_date               timestamptz NOT NULL,
  end_date                 timestamptz NOT NULL,
  estado                   smallint NOT NULL,     -- ver §4.3
  sat_request_status_externo  smallint NULL,      -- copia del satRequestStatusId
  download_request_status_externo smallint NULL,  -- copia del downloadRequestStatusId
  invoice_count            int NULL,
  last_poll_at             timestamptz NULL,
  next_poll_at             timestamptz NOT NULL,  -- agendamiento del Poller
  cosechada_at             timestamptz NULL,
  cerrada_at               timestamptz NULL,
  error_codigo             text NULL,
  error_mensaje            text NULL,
  attempts_poll            int NOT NULL DEFAULT 0,
  created_at               timestamptz NOT NULL,
  updated_at               timestamptz NOT NULL,
  CONSTRAINT uq_solicitudes_request_externo
    UNIQUE (request_id_externo)
);

CREATE INDEX ix_solicitudes_descarga_next_poll
  ON integraciones_fiscal.solicitudes_descarga (estado, next_poll_at)
  WHERE estado IN (1, 2);   -- Pendiente, EsperandoSat
```

> No persistimos los meta-items individuales en este módulo — el
> `ICfdiIngestaPort` los entrega a CxP, que es donde viven.

### 4.3 FSM de `SolicitudDescarga`

```
Pendiente (1) ──submit OK──▶ EsperandoSat (2) ──Terminada SAT──▶ Terminada (3)
   │                              │                                  │
   │                              │ Error SAT (errorCodigo)          │ cosecha OK
   │                              ▼                                  ▼
   │                            Error (5)                          Cosechada (4)
   │                                                                  │
   └── submit falla ──▶ Error (5)                                     │ adapter ack
                                                                       ▼
                                                                    Cerrada (6)
```

- **`Pendiente`**: persistida localmente, `POST /download-requests`
  aún no devolvió éxito.
- **`EsperandoSat`**: FiscalAPI tiene la solicitud, el SAT aún no
  responde. Poller la consulta cada `next_poll_at`.
- **`Terminada`**: `downloadRequestStatusId == 3` (Completada). Se
  agenda cosecha en mismo tick.
- **`Cosechada`**: paginamos `/meta-items`, llamamos
  `ICfdiIngestaPort.IngresarMetadataAsync` por cada UUID. Sigue
  "viva" para auditoría.
- **`Cerrada`**: el consumidor (CxP) acusó recibo del último UUID.
  Se puede borrar después de N días (auditoría).
- **`Error`**: terminal. Requiere intervención manual o re-creación.

### 4.4 Ports e impl

```csharp
// Reemplaza al actual IFiscalApiClient.
public interface IFiscalApiClient
{
    // Aprovisionamiento
    Task<PersonExterno> AsegurarPersonAsync(Guid empresaId, string rfc, string razonSocial, CancellationToken ct);
    Task<DownloadRuleExterna> AsegurarDownloadRuleAsync(
        Guid empresaId, string personId, SatQueryType qt, DownloadType dt, SatInvoiceStatus st, CancellationToken ct);

    // Descarga
    Task<SolicitudExterna> CrearSolicitudAsync(
        Guid empresaId, string ruleId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task<SolicitudExterna> ConsultarSolicitudAsync(Guid empresaId, string requestId, CancellationToken ct);
    IAsyncEnumerable<MetaItem> ListarMetadataAsync(Guid empresaId, string requestId, CancellationToken ct);
    IAsyncEnumerable<XmlItem> ListarXmlsAsync(Guid empresaId, string requestId, CancellationToken ct);

    // Consulta individual (refresh worker)
    Task<EstatusCfdiResult> ConsultarEstadoFacturaAsync(Guid empresaId, ConsultarEstadoFacturaRequest req, CancellationToken ct);
}
```

El puerto inverso `ICfdiIngestaPort` se conserva pero su contrato cambia:
metadata + opcional XML, no XML obligatorio.

```csharp
public interface ICfdiIngestaPort
{
    Task<bool> EsConocidoAsync(Guid empresaId, string uuidCfdi, CancellationToken ct);
    Task IngresarMetadataAsync(Guid empresaId, CfdiMetadataPayload payload, CancellationToken ct);
    Task IngresarXmlAsync(Guid empresaId, string uuidCfdi, byte[] xml, CancellationToken ct);
}
```

> Idea: en MVP descargamos solo **Metadata** (más barato, más rápido).
> CxP recibe el UUID + datos del SAT y crea un `CfdiRecibido` con
> estado `PendienteXml`. Cuando el usuario abre el detalle, CxP solicita
> el XML on-demand vía `IngresarXmlAsync`. Esto reduce volumen × 10–50.

---

## 5. Workers nuevos

### 5.1 `DescargaSubmitterWorker` (reemplaza `DescargaMasivaSatWorker`)

**Función:** garantiza que existe una `SolicitudDescarga` cubriendo
cada día del rango `[hoy - backfillDias, hoy)`, por cada
`DownloadRuleExterna` activa.

**Cadencia:** 1× al día (cron 02:00 local) + on-demand vía endpoint
admin.

**Lógica:**

```
for each ConfiguracionPac activa:
  for each RfcReceptor habilitado:
    asegurar Person en FiscalAPI (idempotente)
    for each combinación esperada (qt × dt × statusSat):
      asegurar DownloadRule en FiscalAPI (idempotente, cachea rule_id)
    for each día en [hoy - backfillDias, hoy):
      si no existe SolicitudDescarga (rule, día) en estado != Error:
        POST /download-requests → persistir SolicitudDescarga(estado=Pendiente)
```

**Ventanas:** Cada solicitud cubre **un día calendario** (no 31 — buscamos
granularidad operacional). Backfill default: 7 días.

**Backfill grande:** endpoint admin `POST /api/v1/integraciones/fiscal/
descarga/manual { empresaId, rfc, desde, hasta }`. Valida `hasta - desde
≤ 31 días`; agenda N solicitudes.

### 5.2 `DescargaPollerWorker` (nuevo)

**Función:** revisa `SolicitudDescarga` con estado en
`{Pendiente, EsperandoSat}` y `next_poll_at <= now`. Las consulta a
FiscalAPI y avanza la FSM.

**Cadencia:** 15 min default (configurable). Cubre el "polleo cada
30 min de FiscalAPI" sin desperdiciar.

**Backoff:** `next_poll_at = now + min(2^attempts * 15min, 6h)`. Acota
el polleo en caso de SAT lento.

**Lógica:**

```
batch = SELECT * FROM solicitudes_descarga
        WHERE estado IN (Pendiente, EsperandoSat) AND next_poll_at <= now
        ORDER BY next_poll_at
        LIMIT 100
        FOR UPDATE SKIP LOCKED;       # lock por solicitud, no por RFC

for s in batch:
  resp = GET /download-requests/{s.request_id_externo}
  s.attempts_poll++
  s.last_poll_at = now
  s.next_poll_at = now + backoff(s.attempts_poll)
  match resp.downloadRequestStatusId:
    case 3 (Completada):
      cosechar(s)
      s.estado = Cosechada
    case error:
      s.estado = Error
      s.error_codigo = ...
    else:
      s.estado = EsperandoSat (sin cambio operativo)
```

### 5.3 `cosechar(s)`

```
async for page in ListarMetadataAsync(s.request_id_externo):
  for item in page.items:
    if not await port.EsConocidoAsync(s.empresa_id, item.uuid):
      await port.IngresarMetadataAsync(s.empresa_id, item.toPayload(s.id))
s.cosechada_at = now
```

Idempotente por UUID. Si el adapter `ICfdiIngestaPort` falla a la
mitad del page, el siguiente tick re-cosecha y `EsConocidoAsync` filtra
los ya procesados — sin doble inserción.

### 5.4 `EstadoSatRefreshWorker` — qué hacer con él

Sigue **conceptualmente correcto**: itera CFDIs `PorProcesar` y los
revalida en SAT. **Pero hay 3 opciones** de implementación nueva:

| Opción | Implementación | Pros | Contras |
|---|---|---|---|
| A. **Cancelado-rule** | Crear una `DownloadRule` con `satInvoiceStatusId=Cancelado` además de la `Vigente`. El Submitter crea solicitudes diarias para Cancelados. Cuando llega un UUID con estado `Cancelado`, el adapter de CxP busca el CFDI y lo marca. | Aprovecha el mismo flujo. Detecta cancelaciones del mes anterior si la regla cubre back-window. | Más cuota SAT. Latencia de hasta 24h. |
| B. **Consulta individual** | Usar `POST /invoices-by-reference/consultar-estado-de-factura` por cada UUID (como hoy hace `EstadoSatRefreshWorker`). | Bajo lag (consulta ad-hoc). | Costo lineal en N UUIDs. Caro si hay 5000 pendientes. |
| C. **Híbrido** | Opción A para batch diario. Opción B para casos críticos (CFDI con pago programado en 48h). | Balance. | Más complejidad. |

**Recomendación**: **Opción A** en MVP. Reemplaza a
`EstadoSatRefreshWorker` con una segunda regla diaria. La Opción B se
mantiene como endpoint `POST /api/v1/integraciones/fiscal/refresh-uuid`
para casos manuales.

`[D5 — owner decide]`

---

## 6. Plan de migración

### 6.1 Lo que se conserva

- Schema `integraciones_fiscal` + tablas `configuracion_pac` y
  `rfcs_receptores` (extendiendo con `person_id_externo`).
- ASP.NET DataProtection + `SecretCipher` (ADR-0037).
- Admin UI master-detail (PR-8 frontend, ya mergeado).
- Puerto inverso `ICfdiIngestaPort` / `ICfdiEstadoActualizadoPort`
  (ajustando contrato).
- Feature flag `IntegracionesFiscal:Workers:Disabled` y los del cutover
  viejo en CxP (siguen `Disabled=true` permanentemente).

### 6.2 Lo que se reescribe

- `IFiscalApiClient` (interfaz + impl HTTP) — partido en 6 métodos
  reales.
- `DescargaMasivaSatWorker` → eliminado. Dos workers nuevos.
- `EstadoSatRefreshWorker` → eliminado o demoted a endpoint admin
  (según D5).
- `ConfiguracionPac.descarga_intervalo_segundos`,
  `descarga_backfill_horas`, `refresh_intervalo_segundos`,
  `refresh_batch_size` → deprecated. Reemplazados por options globales
  del Submitter/Poller (cron daily + 15min) + `backfill_dias` por RFC.

### 6.3 Lo que se agrega

- Tablas `download_rules_externas`, `solicitudes_descarga`.
- Migration EF Core + seed inicial vacío.
- Endpoint admin `POST .../descarga/manual` para re-descarga histórica.
- 2 nuevas métricas (Application Insights):
  `IntegracionesFiscal.SolicitudesAbiertas` (gauge por empresa) y
  `IntegracionesFiscal.HorasUltimaTerminada` (gauge por RFC).
- `tenant_key_cifrado` en `configuracion_pac` (Auth de FiscalAPI usa
  ambos headers).

### 6.4 Pasos sugeridos

| PR | Alcance | Bloquea |
|---|---|---|
| **PR-9** | Migration tablas nuevas + cambios `RfcReceptor` + `ConfiguracionPac.TenantKey` + `IFiscalApiClient` interfaz nueva + impl HTTP estilo `live.fiscalapi.com` (real, no placeholder). Tests unit. | — |
| **PR-10** | `DescargaSubmitterWorker` + endpoint admin `descarga/manual`. Tests integ contra sandbox (`test.fiscalapi.com`). | PR-9 |
| **PR-11** | `DescargaPollerWorker` + cosecha + ajustes `ICfdiIngestaPort` (separar metadata de xml). Tests integ. | PR-10 |
| **PR-12** | Cleanup: borrar `DescargaMasivaSatWorker` y `EstadoSatRefreshWorker` viejos, eliminar opciones deprecated de `ConfiguracionPac`, ajustar admin UI (esconder sliders que ya no aplican). | PR-11 |
| **PR-13** (D5) | EstadoSat opción A (regla Cancelado) o B (endpoint individual) o C (híbrido). | PR-11 |

---

## 7. Conciliación con mailbox

La política documentada en
[`docs/modulos/cuentas-por-pagar/00-levantamiento.md`](../cuentas-por-pagar/00-levantamiento.md)
§9.4 dice "si llega por dos canales (SAT + mailbox), el segundo se
marca `Duplicado`".

El adapter actual hace dedupe **omitido**, no `Duplicado`. Hay dos
escenarios:

| Caso | Hoy | Nuevo |
|---|---|---|
| Mailbox primero, luego SAT | El SAT trae el UUID; `EsConocidoAsync` retorna true; **no se hace nada**. | Igual, pero crear un audit log `cfdi_descarga_audit` con `canal=Sat`, `accion=Duplicado` para reporte. |
| SAT primero, luego mailbox | Mailbox detecta dup, marca `Duplicado` en CxP. | Igual. |

Esto se resuelve en CxP (adapter), no en `Integraciones.Fiscal`. Sin
embargo, el flujo nuevo (Metadata-only por default) **cambia el orden
típico**: el SAT trae metadata sin XML; mailbox trae el XML completo.
El adapter en CxP debe poder hacer "merge" cuando coinciden — agregar
el XML al CFDI ya creado en estado `PendienteXml`. `[D3 — decidir
política exacta de merge].`

---

## 8. Cancelación SAT post-pago

Escenario: proveedor cancela un CFDI 30 días después de que ya lo
pagamos. Hoy `MarcarCanceladoAsync` solo actúa si estado interno ==
`PorProcesar` (no `Pagado`).

**Propuesta:**

1. **No bloquear automáticamente** — el pago ya salió, los efectos
   contables ya corrieron.
2. **Emitir evento `CfdiCanceladoPostPagoEvent`** que CxP captura para:
   - Crear una entrada `NotaCargoCandidata` (recovery: el ERP propone
     una nota de cargo al proveedor por el monto del CFDI cancelado).
   - Alertar al Tesorero responsable del proveedor (notificación
     in-app).
3. **Bloquear próximos pagos al mismo proveedor** hasta que el usuario
   revise la nota de cargo. `proveedor.bloqueado_revision = true`.

`[D6 — owner decide]`. Política sensible legalmente.

---

## 9. Errores y resiliencia

### 9.1 Por status HTTP

| Status | Significado | Acción |
|---|---|---|
| 400 / 422 | Validación FiscalAPI (modelo `{succeeded:false, data:[errors]}`). | **No reintentar.** Marcar `SolicitudDescarga.estado=Error` con el `errorCode` del primer error. Alertar admin. |
| 401 / 403 | Credenciales inválidas o sin permiso. | **No reintentar.** Marcar `ConfiguracionPac.activa=false` y alertar al admin (toast en el UI + métrica). |
| 404 | Recurso no existe. | Si era una solicitud, marcar `Error` (probablemente TTL expirado). Si era una regla, recrearla y reintentar 1×. |
| 429 | Rate limit. | Backoff exponencial respetando header `Retry-After` si presente. Min 60s, max 1h. |
| 5xx / timeout | Transitorio. | Retry 3× con jitter (Polly), luego deja en `EsperandoSat` con `next_poll_at` mayor. |

Polly policies se leen ahora desde `ConfiguracionPac` por empresa
(resolviéndose en un `PolicyRegistry` keyed por empresaId). Cierra
la deuda actual de policies hardcoded.

### 9.2 Errores específicos de SAT

`satRequestStatusId` puede llegar con códigos de error del SAT
(`5000`, `5001`, etc. — `[Gap]` validar contra catalogo
`SatRequestStatuses`). Mapearlos a:

- **Transitorio** (SAT down, mantenimiento): mantener `EsperandoSat`,
  backoff.
- **Permanente** (RFC inválido, sin permisos): `Error`, alertar.

### 9.3 Distribución / concurrencia

- **Lock por solicitud** vía `FOR UPDATE SKIP LOCKED` (PG nativo). No
  más lock por RFC.
- 2 instancias del App Service pueden correr en paralelo sin
  duplicar trabajo. Cada una toma su slice del batch.
- `Submitter` corre 1× al día (cron) — protegido por
  `INSERT ... ON CONFLICT (rule, día) DO NOTHING` en
  `solicitudes_descarga`.

---

## 10. Métricas y observabilidad

Counter:
- `integraciones_fiscal.solicitudes.creadas.total{empresa, rfc, queryType, status}`
- `integraciones_fiscal.solicitudes.terminadas.total{empresa, rfc}`
- `integraciones_fiscal.solicitudes.errores.total{empresa, rfc, code}`
- `integraciones_fiscal.cfdis.cosechados.total{empresa, rfc}`

Gauge:
- `integraciones_fiscal.solicitudes.abiertas{empresa, rfc, estado}`
- `integraciones_fiscal.horas_desde_ultima_terminada{empresa, rfc}`
- `integraciones_fiscal.cuota_descarga_porcentaje_uso{empresa}` (`[Gap]` —
  si FiscalAPI expone quota)

Alertas (Application Insights):
- Solicitud en `EsperandoSat` > 48h.
- 3 errores 401/403 consecutivos en una `ConfiguracionPac`.
- Tasa de error > 10% en 1h.

---

## 11. Decisiones pendientes para el owner

| # | Decisión | Opciones | Recomendación |
|---|---|---|---|
| **D1** | ¿`queryType` por default? | (a) `Metadata` (rápido, sin XML); (b) `CFDI` (XML parseado); (c) ambos. | **(a) Metadata** + XML on-demand. Reduce costo y volumen ~10×. CxP descarga XML al abrir el CFDI. |
| **D2** | ¿FIEL/e.firma se sube a FiscalAPI? | (a) Sí, agregar `tax_files` per Person; (b) FiscalAPI lo provee con sus propios certificados; (c) `[Gap]` — confirmar con sandbox. | Validar con sandbox antes de decidir. Si (a), agregar tabla `certificados_fiscales` con el binario cifrado. |
| **D3** | Política de merge SAT+mailbox | (a) Mailbox prevalece (XML completo); (b) SAT prevalece (versión "oficial"); (c) Primero gana, segundo audita. | **(c) Primero gana** — audit log para reporte de duplicados. |
| **D4** | ¿Cubrir CFDIs Cancelados en regla diaria? | (a) Sí, segunda regla diaria; (b) No, solo Vigentes + consulta individual cuando necesario. | **(a) Sí** — detecta cancelaciones de hoy mañana, sin lag de N días. |
| **D5** | Reemplazo de `EstadoSatRefreshWorker` | A / B / C de §5.4. | **A** (regla Cancelados) en MVP. B/C deferred. |
| **D6** | Política de cancelación post-pago | (a) Solo alertar; (b) Bloquear proveedor + nota de cargo candidata; (c) Sin acción. | **(b)** — protege a la empresa. |
| **D7** | Granularidad de `SolicitudDescarga` | (a) 1 por día; (b) 1 por semana; (c) 1 por mes (límite SAT 31d). | **(a) Diaria** — reanudación granular si una falla. |
| **D8** | Cron del Submitter | (a) 02:00 local fijo; (b) configurable por empresa. | **(a)** — simplicidad. |
| **D9** | Backfill default | 7 / 14 / 30 días. | **7 días** — alinea con backfill viejo. Manual extiende vía endpoint. |
| **D10** | TTL de `SolicitudDescarga` cerrada | (a) Borrar a los 90d; (b) Conservar siempre (auditoría). | **(b)** — solo creamos N×365 = ~3650/año por RFC. Manejable. |

---

## 12. Variables a validar contra sandbox

Antes de PR-9 hay que pegar a `https://test.fiscalapi.com` con
credenciales reales y confirmar:

- [ ] Headers exactos: `X-TENANT-KEY` + `X-API-KEY` + `X-TIME-ZONE`.
- [ ] Catálogo completo de `SatQueryTypeId` (¿solo CFDI/Metadata/Retenciones?).
- [ ] Catálogo completo de `SatRequestStatusId` (códigos de error del SAT).
- [ ] Si necesita FIEL subido o no.
- [ ] Si "Consultar estado de factura" sirve para batch o solo unitario.
- [ ] Rate limit real (¿hay header `Retry-After` en 429?).
- [ ] Tiempo típico de `Pendiente` → `Terminada` (¿2h? ¿24h?).
- [ ] Si solicitudes tienen TTL (¿qué pasa con una solicitud "vieja"?).
- [ ] Formato exacto del response paginado de `/meta-items` (¿incluye XML
      en JSON parseado o solo metadata?).
- [ ] Si `personId` debe pre-existir o se crea on-the-fly al crear la
      regla.

> **Convención:** todo Gap o D pendiente lleva un
> `PLATFORM-TODO(<FiscalApiSandboxValidation>)` en el código stub hasta
> cerrarse.

---

## 13. Cronograma propuesto

| Semana | Hito |
|---|---|
| W1 | Validar sandbox (§12) + cerrar D1-D10. |
| W2 | PR-9 (interfaz + impl HTTP real + migration). |
| W3 | PR-10 (Submitter + manual endpoint) + tests sandbox. |
| W4 | PR-11 (Poller + cosecha) + tests sandbox. |
| W5 | PR-12 (cleanup viejo) + PR-13 (Refresh strategy). |
| W6 | UAT con CFDIs reales del proveedor de Millet en sandbox. |
| W7 | Go-live en prod con 1 empresa, monitoreo cerrado. |
| W8+ | Roll-out a las demás empresas. |

---

## Apéndice A — Referencias FiscalAPI

- [docs.fiscalapi.com — Overview](https://docs.fiscalapi.com/)
- [docs.fiscalapi.com/download-info](https://docs.fiscalapi.com/download-info)
- [docs.fiscalapi.com/download-rules](https://docs.fiscalapi.com/download-rules)
- [docs.fiscalapi.com/download-requests](https://docs.fiscalapi.com/download-requests)
- [docs.fiscalapi.com/download-catalogs](https://docs.fiscalapi.com/download-catalogs)
- [docs.fiscalapi.com/authentication](https://docs.fiscalapi.com/authentication)
- [docs.fiscalapi.com/environments](https://docs.fiscalapi.com/environments)
- [docs.fiscalapi.com/api-response](https://docs.fiscalapi.com/api-response)
- [docs.fiscalapi.com/errors](https://docs.fiscalapi.com/errors)
- [docs.fiscalapi.com/tax-files](https://docs.fiscalapi.com/tax-files)
- [docs.fiscalapi.com/recipients](https://docs.fiscalapi.com/recipients)
- [docs.fiscalapi.com/invoices-by-reference](https://docs.fiscalapi.com/invoices-by-reference)
- Postman collection: `https://documenter.getpostman.com/view/4346593/2sB2j4eqXr`
- SDK .NET: [github.com/FiscalAPI/fiscalapi-net](https://github.com/FiscalAPI/fiscalapi-net)

---

## 13. Hallazgos del probe sandbox (2026-05-25)

Tras ~12 iteraciones contra `test.fiscalapi.com` con el script
`tools/fiscalapi-probe/Invoke-FiscalApiProbeE2E.ps1` y lectura cruzada
del SDK oficial `Fiscalapi` en C#, esta sección documenta lo descubierto
empíricamente. **Estos puntos sobrescriben suposiciones de v0.1**.

### 13.1 Auth y envelope (confirmado)

- **3 headers requeridos**: `X-TENANT-KEY` + `X-API-KEY` + `X-TIME-ZONE`
  (no 1 como asumimos en v0.1).
- Response envelope:
  ```json
  {
    "data": <T | items[]>,
    "succeeded": true|false,
    "message": "...",
    "details": "...",
    "httpStatusCode": 200,
    "traceIdentifier": "..."
  }
  ```
- Paginación: `data.{items[], pageNumber, totalPages, totalCount, hasPreviousPage, hasNextPage}`.

### 13.2 Endpoint correcto para emitir factura

- **`POST /api/v4/invoices`** — NO `/api/v4/invoices/income` como
  sugería un blog post obsoleto. El SDK lo confirma en
  `Services/InvoiceService.cs`. El `typeCode` (I/E/P) discrimina.
- **`POST /api/v4/invoices/status`** — refresh por UUID individual
  (no `/sat-status`).

### 13.3 Catálogos completos (con IDs exactos)

| Catálogo | IDs |
|---|---|
| `SatQueryTypes` | `CFDI` (XML completo), `Metadata` (resumen), `Retenciones` |
| `DownloadTypes` | `Emitidos`, `Recibidos`, **`Uuid`** ← *no documentado en docs.fiscalapi.com pero existe* |
| `SatInvoiceStatuses` | `""` (Todos), `Cancelado`, `Vigente` |
| `SatInvoiceTypes` | `""` (Todos), `E`, `I`, `N`, `P`, `T` |
| `SatRequestStatuses` (estado en el SAT) | `0` Desconocido, `1` Aceptada, `2` En proceso, `3` Terminada, `4` Error, `5` Rechazada, `6` Vencida, `-1` Abandonada |
| `DownloadRequestStatuses` (estado en FiscalAPI) | `1` Esperando SAT, `2` **Esperando API**, `3` Completada, `-1` Abandonada |
| `DownloadRequestTypes` | `Automatica`, `Manual` |

#### Implicación 1 — `DownloadTypes.Uuid` reemplaza al refresh worker

El doc v0.1 §5.4 listaba 3 opciones (A/B/C) para el refresh worker.
**La opción A definitiva** es usar `DownloadType=Uuid` en una rule
recurrente — mismo flujo Submitter + Poller, sin endpoint distinto.

#### Implicación 2 — `SatInvoiceStatusId=""` cubre TODOS

D4 v0.1 proponía 2 rules paralelas (Vigentes + Cancelados). Con `""`,
**una sola rule** cubre ambos estados — el campo `Estatus` viene en el
meta-item de cada CFDI. Reduce a la mitad las solicitudes diarias.

#### Implicación 3 — FSM más rico

El doc v0.1 §4.3 tenía FSM `Pendiente → EsperandoSat → Terminada`.
La realidad es:
```
Pendiente (1=Esperando SAT)
   ↓ SAT acepta
EsperandoApi (2=Esperando API)   ← FiscalAPI procesando paquetes
   ↓ FiscalAPI cosecha
Completada (3)
```

### 13.4 CFDI 4.0 — validaciones SAT críticas

CFDI 4.0 tiene catálogos estrictos. Errores comunes encontrados y sus fixes:

| Error SAT | Causa | Fix en PR-9 |
|---|---|---|
| `CFDI40139` (Nombre emisor) | LegalName con régimen societario ("SA DE CV") | Limpiar el CN del cert antes de mandarlo — el SDK lo hace via mapeo en `Person.LegalName` |
| `CFDI40145` (Nombre receptor) | Mismo | Mismo |
| `CFDI40148` (CP receptor) | CP del Person no coincide con SAT | Validar al alta del Person — admin UI tiene que requerirlo |
| `CFDI40179` (TasaOCuota) | Tasa enviada como `0.16` en vez de `0.160000` | SDK C# usa `decimal` (preserva precisión 6); en JSON manual hay que enviar string |
| `305` (Vigencia CSD) | Cert no está en LCO sintética (sandbox) | En prod no aplica — los CSDs/FIEL reales están en LCO real |
| `401` (Fecha fuera de rango) | Fecha enviada en TZ del cliente, no TZ servidor (México) | SDK NO maneja esto automáticamente — el ERP debe convertir a `America/Mexico_City` al construir el body |

### 13.5 Sandbox vs Producción

| Aspecto | Sandbox (`test.fiscalapi.com`) | Producción (`live.fiscalapi.com`) |
|---|---|---|
| **Crear DownloadRule** | `POST /api/v4/download-rules/test` (variante con `/test`). Ignora `personId` del body y crea rule sobre el primer Person del tenant con cert válido. | `POST /api/v4/download-rules` (sin `/test`) |
| **Crear DownloadRequest** | **NO funciona** (`403 "Solo disponible en producción"`). | `POST /api/v4/download-requests` |
| **Poll + Cosecha** | No aplica (no hay requests creables) | `GET /api/v4/download-requests/{id}` + `/meta-items` + `/xmls` |
| **Cert del emisor** | Debe estar en LCO sintética → usar **certs del SDK sample** (`30001000000500003416`), NO los públicos del ZIP `developers.sw.com.mx` | FIEL real del receptor cargada via `POST /api/v4/tax-files` |
| **Cuota** | Gratis | Consume timbres reales |

> **Conclusión**: la validación end-to-end del flujo de descarga masiva
> **solo se puede hacer en producción**. Tests de integración del
> módulo Integraciones.Fiscal deben:
> - Tests unit + contract tests contra mocks → corren siempre en CI.
> - Tests de integración → manualmente en QA con creds prod via Key
>   Vault, ejecutados al cambiar el contrato del SDK o al onboardear
>   una nueva empresa.

### 13.6 SDK oficial — comportamiento confirmado

Del repo `github.com/FiscalAPI/fiscalapi-net` master:

- **`FileType` enum**: solo 2 valores (`CertificateCsd=0`, `PrivateKeyCsd=1`).
  FiscalAPI distingue CSD vs FIEL leyendo el subject del cert, no por flag.
- **`InvoiceService.CreateAsync`** → `POST /api/v4/invoices` (genérico).
  Las constantes `IncomeEndpoint = "income"` están comentadas (legacy).
- **Serialización Newtonsoft.Json** con `CamelCasePropertyNamesContractResolver`
  y `NullValueHandling.Ignore`. Preserva precisión decimal por default.
- **Modos by-references / by-values**: ambos van al mismo endpoint;
  by-references es más limpio cuando el Person está pre-creado.

### 13.7 Decisión arquitectónica final

**El módulo `Millet.Integraciones.Fiscal` usa el SDK NuGet oficial
`Fiscalapi`** envuelto en una capa de abstracción (`IFiscalApiClient`
propio) que:

1. Apunta a `live.fiscalapi.com` en **todos los ambientes Millet**
   (dev/qa/prod). No hay configuración para `test.fiscalapi.com`
   porque sandbox no soporta el flujo completo.
2. **Cada ambiente Millet tiene su propio tenant FiscalAPI** (con
   suscripción gratuita o pagada según el caso).
3. Tests automatizados son **unit** (mocks del SDK) y **contract**
   (validan el shape de DTOs). Tests de integración son manuales en QA.
4. Admin UI sube la FIEL del receptor al Person de FiscalAPI; ese flujo
   se hace una vez por empresa al onboarding y al renovar FIEL
   (típicamente cada 4 años).

### 13.8 D-decisions revisadas con evidencia

| # | Decisión v0.1 | Estado v0.2 |
|---|---|---|
| **D1** queryType default | Metadata | 🔁 **Re-cerrada (PR-14): `CFDI` masivo**. La premisa "reduce costo ~10×" era falsa — FiscalAPI cobra por **suscripción**, no por solicitud ni por byte. Ver §13.10. |
| **D2** ¿FIEL requerida en prod? | `[Gap]` | ✅ Cerrada: **Sí en prod**, sandbox usa CSD pre-cargado |
| **D3** Merge SAT+mailbox | Primero gana, audita | ✅ Sin cambio — se implementa en CxP, no en Integraciones.Fiscal |
| **D4** Regla diaria de Cancelados | Sí, 2 rules | ❌ **Revertida**: `SatInvoiceStatusId=""` cubre ambos. 1 rule. |
| **D5** Refresh worker | Opción A (regla) | ✅ **Cerrada con `DownloadType=Uuid`** (mismo flujo Submitter/Poller, no rule paralela) |
| **D6** Cancelación post-pago | Bloquear + nota cargo | ✅ Sin cambio — implementación en CxP |
| **D7** Granularidad solicitud | 1/día | ✅ Sin cambio |
| **D8** Cron Submitter | 02:00 local | ✅ Sin cambio |
| **D9** Backfill default | 7 días | ✅ Sin cambio (límite SAT/FiscalAPI: 31 días) |
| **D10** TTL solicitudes | Conservar siempre | ✅ Sin cambio |

### 13.10 D1 revisada en PR-14 — `SatQueryType=CFDI`

**Premisa original (v0.2, D1 cerrada con Metadata + XML on-demand):**
> "Metadata + XML on-demand reduce costo ~10× porque solo se baja el XML
> cuando el usuario abre el CFDI."

**Por qué era falsa:**

1. **FiscalAPI cobra por suscripción**, no por solicitud ni por byte
   descargado. Una rule `Metadata` y una rule `CFDI` consumen exactamente
   la misma cuota.
2. **El SAT no cobra** por download-requests. Solo aplica rate-limit
   (~1 solicitud/RFC/hora). Una solicitud `CFDI` no es más cara que una
   `Metadata`.
3. **CxP procesa el 100% de los CFDIs recibidos** (todos se concilian
   contra OC). La hipótesis "solo se abre el 10%" no aplica: si todo
   CFDI termina siendo abierto, Metadata + on-demand duplica trabajo.

**Implicación operativa:**

- El SDK NuGet **no expone un endpoint XML-por-UUID** real. Solo
  `GetXmlsAsync(requestId)` que entrega TODOS los XMLs de una request
  completada. El método `DescargarXmlAsync(uuid)` que existía era una
  heurística rota (`Base64Content.Contains(uuid)` falla porque la
  codificación base64 oculta el UUID literal del XML).
- Para hidratar un XML de una rule `Metadata` correctamente habría que
  crear un download-request **nuevo** con `DownloadType=Uuid` apuntando
  al UUID puntual, esperar a que termine (segundos a minutos),
  cosecharlo. Es un sub-flujo paralelo costoso de mantener.

**Decisión PR-14:**

| Aspecto | v0.2 (Metadata) | v0.3 (CFDI) |
|---|---|---|
| `SatQueryType` default | `Metadata` | **`CFDI`** |
| Cosecha del Poller | `ListarMetaItemsAsync` | `ListarMetaItemsAsync` + `ListarXmlsAsync` (paired por UUID) |
| Estado inicial en CxP | `MetadataOnly` | **`PorProcesar`** (con XML completo, blob ref, hash) |
| Hidratación on-demand | requiere PR adicional | **N/A** — XML ya está |
| Storage por CFDI | ~1 KB metadata en BD | ~10 KB XML en blob (Azure Blob ≈ $0.02/GB/mes) |

**Cambios concretos del PR-14:**

1. `DescargaSubmitterWorker.CrearRuleAsync` usa `SatQueryType.Cfdi` por
   default (3 sitios).
2. `IFiscalApiSdkClient`: agregar `ListarXmlsAsync(requestId)` que
   devuelve `IAsyncEnumerable<XmlCfdiItemDto>` (UUID extraído del XML
   decodificado vía regex contra `<tfd:TimbreFiscalDigital UUID="...">`).
3. `IFiscalApiSdkClient.DescargarXmlAsync(uuid)` heurístico eliminado.
4. `IFiscalMetadataReceiver` renombrado a `IFiscalCfdiReceiver`; payload
   incluye `byte[] XmlBytes` (no opcional).
5. `DescargaPollerWorker.CosecharAsync`: lee meta-items + XMLs en
   memoria, pairea por UUID, llama receiver. Si un meta-item no tiene
   XML pareable (caso edge), se loggea warning y se salta — el caller
   puede pedir refresh manual con `DownloadType=Uuid`.
6. `FiscalCfdiReceiverAdapter` (CxP): parsea XML con `IXmlCfdiParser`,
   calcula SHA-256, sube a `ICfdiBlobStorage`, crea `CfdiRecibido` en
   estado `PorProcesar` normal (no `MetadataOnly`).
7. `EstadoCfdiRecibido.MetadataOnly` eliminado del enum + factory
   `CfdiRecibido.IngresarMetadataOnly` eliminada + migration que dropea
   las columnas `SolicitudDescargaId` y `RequestIdExternoFiscalApi` (se
   pierden con el estado).

**Lo que no cambia:**

- `DownloadType.Uuid` sigue siendo el camino para refresh por UUID
  individual cuando se necesite (D5). PR-14 no lo implementa pero no
  lo rompe.
- `SatInvoiceStatusFilter=Todos` se mantiene (D4 ya cubría Vigentes +
  Cancelados con una sola rule).
- Workers (Submitter + Poller), Outbox, persistencia: mismo diseño.

**Diferida a PR-15 — detección de cancelaciones post-timbre:**

El XML cosechado por la rule `CFDI` **NO refleja** cancelaciones SAT
ocurridas después del timbre — el XML es inmutable, el estatus actual
de SAT no se inscribe en él. Para detectar cancelaciones se necesita una
de estas dos vías (ambas posibles, mismo SDK):

| Vía | Cómo | Granularidad |
|---|---|---|
| Refresh per-UUID | `IFiscalApiSdkClient.ConsultarEstatusUuidAsync` (endpoint `POST /api/v4/invoices/status`) | Síncrono, 1 UUID por llamada |
| Rule `DownloadType=Uuid` | Submitter crea download-request `Uuid` con N UUIDs de interés | Asíncrono, batch |

**Propuesta tentativa PR-15:** worker periódico (`EstatusSatRefreshWorker`,
diferente al legacy borrado en PR-13) que recorre CFDIs en estado
`PorProcesar` y `ConvertidoEnPasivo` no consultados en los últimos N
días, los manda en chunks a `ConsultarEstatusUuidAsync`, y actualiza
`CfdiRecibido.EstatusSat` + emite evento si pasa a `Cancelado`. La
política exacta (frecuencia, ventana, chunk size) la cerramos al abrir
PR-15.

### 13.9 Sobre el probe

- `tools/fiscalapi-probe/Invoke-FiscalApiProbeE2E.ps1` — sandbox. Llega
  hasta crear rule via `/test`. NO puede crear requests (limitación
  sandbox). Útil como referencia del flujo y como tests de contrato.
- `tools/fiscalapi-probe/Invoke-FiscalApiProbeProd.ps1` — producción.
  Para validación end-to-end con FIEL real. **No corrido aún**; se
  reserva para QA pre-prod cuando PR-9 esté listo.
- Tests automatizados del módulo en `backend/tests/Integraciones.Fiscal.*`
  usan mocks del SDK, no estos probes.
