# Documentos PDF de A+W (oferta/pedido) → ERP → Glass Agent

> Feature `aw-documentos-pdf`. Cierra el ciclo de vuelta del documento:
> cuando A+W termina de procesar una oferta o un pedido, **exporta un PDF**
> que el ERP recoge, almacena y expone al Glass Agent.

## 1. Flujo (extremo a extremo)

```
A+W procesa el EDI y exporta el PDF a una carpeta on-prem (la "caliente")
   oferta_<aw_doc_id>.pdf   (quotes)
   pedido_<aw_doc_id>.pdf   (orders)
        │
        ▼  (drop service on-prem, lee la carpeta caliente; NO hace salientes)
GET  /documents                  → lista PDFs de la caliente (cursor modified_at)
GET  /documents/{filename}        → bytes del PDF (application/pdf)
POST /documents/{filename}/archive→ mueve el PDF a archive\ (ack tras tratarlo)
        │
        ▼  PULL — el ERP llama al drop service vía Hybrid Connection
AwDocumentSyncWorker (ERP, timer ~120s) — DOC-LIST-DRIVEN
   1. lista /documents (la caliente está acotada: los tratados ya se movieron)
   2. por cada doc: resuelve entidad por aw_doc_id (el ERP guarda TODA entidad
      como Cotizacion — el doc_type oferta/pedido del PDF es solo metadata, no
      corresponde al TipoEntidad; el aw_doc_id es único en A+W)
      - sin entidad → lo deja (correlacionará/aparecerá luego)
      - sin PDF      → descarga, sube a Blob (container integraciones-aw-blobs),
                       AdjuntarDocumentoPdf, publica AwDocumentoAdjuntado + notifica
      - tratado      → POST /archive (ack) → el drop service lo saca de la caliente
        │
        ▼
Glass Agent obtiene la URL del PDF (Soketi push u on-demand) y la muestra al vendedor.
```

**Cómo se sabe "ya enviado vs no" (sin escanear histórico):** por **presencia
física**. La carpeta caliente solo contiene PDFs *no tratados*; al subirlos a
Blob con éxito, el ERP hace `POST /archive` y el drop service los mueve a
`archive\` renombrados con `_<utcTimestamp>`. Así el scan de `/documents` queda
O(pendientes), no O(histórico), aunque A+W exporte miles. El archive ocurre
**solo tras subir+persistir** (at-least-once): si algo falla antes del ack, el
PDF sigue en la caliente y se reintenta; la idempotencia del ERP
(`PdfBlobUrl != null`) evita re-subir si el ack se perdió.

**Restricción clave:** la Hybrid Connection es **inbound-only** (ERP → on-prem).
Por eso el ERP hace *pull* del endpoint `/documents`, espejo de cómo ya
consume `/completions`. La URL que recibe el Agent **no** es la del blob:
es un **endpoint proxy autenticado del ERP**, que el Agent consume con su
JWT Bearer. El blob nunca se expone público.

## 2. Cambios de contrato que el Glass Agent debe consumir

### 2.1 `GET /api/v1/integraciones/aw/cotizaciones/{id}` (detalle, ya existente)
Tres campos nuevos en la respuesta (camelCase, `null` hasta que el PDF esté listo):

```jsonc
{
  // ...campos existentes (estado, awDocId, ...)
  "pdfUrl": "/api/v1/integraciones/aw/cotizaciones/{id}/pdf", // ruta del proxy, o null
  "pdfFilename": "oferta_4614.pdf",                            // o null
  "pdfUploadedAt": "2026-06-30T18:21:09.12Z"                   // o null
}
```

> **`pdfUploadedAt`** es la hora **real de exportación del PDF por A+W**
> (`modified_at` del archivo en la carpeta on-prem), en UTC ISO 8601 —
> **no** la de correlación ni la de subida a blob. Cumple
> `pdfUploadedAt >= correlatedAt` (A+W exporta tras correlacionar), por lo
> que `pdfUploadedAt - correlatedAt` mide el tiempo correlación→entrega.

### 2.2 `GET /api/v1/integraciones/aw/cotizaciones/{id}/pdf` (nuevo)
- **Auth:** `Authorization: Bearer <jwt>` con permiso `IntegracionesAwCotizacionesConsultar`
  (el mismo que ya usa el Agent para consultar). Scoping por empresa automático.
- **200** `application/pdf` con `Content-Disposition` (`fileDownloadName` = `pdfFilename`).
- **404** si la cotización no existe o aún no tiene PDF.

### 2.3 Soketi `aw-cotizacion-actualizada` (evento ya existente)
Payload (snake_case) ahora incluye:

```jsonc
{
  "erp_id": "…", "quote_reference": "…", "estado": 2, "aw_doc_id": 4614,
  "correlated_at": "2026-06-30T18:19:02.00Z",                        // AUTORITATIVO y estable
  "pdf_url": "/api/v1/integraciones/aw/cotizaciones/{erp_id}/pdf", // o null
  "pdf_filename": "oferta_4614.pdf",                                // o null
  "pdf_uploaded_at": "2026-06-30T18:21:09.12Z",                     // hora real de exportación A+W, o null
  "updated_at": "…"
}
```

> **`correlated_at`** viene en **todos** los eventos (correlación y PDF) con el
> mismo valor estable (`entidad.CorrelatedAt`). El Agent debe usarlo para setear
> `aw_correlated_at` y **nunca** caer a `updated_at` (que en el evento del PDF es
> la hora del PDF) — si no, la métrica "tiempo hasta correlacionar" queda mal.
> El webhook (§3.2) también lo envía.

---

## 3. ¿Cómo se entera el Glass Agent del PDF? (importante)

El PDF llega **después** de la correlación (A+W correlaciona y *luego* exporta
el PDF). El reconcile worker actual (`erp_reconcile_worker.php`) solo pollea
órdenes en `status='submitted_to_erp'`: una vez correlacionadas pasan a
`aw_correlated` y **salen del poll**, así que **ese cron NO vería el `pdfUrl`**.

**El ERP es quien informa al Agent**, vía el push de Soketi. Tres caminos, en
orden de preferencia:

| # | Mecanismo | Cambio en el Agent | Cuándo |
|---|---|---|---|
| 0 | **Webhook server-to-server ERP→Agent** (recomendado, live real) — al adjuntar el PDF, el `AwDocumentSyncWorker` hace `POST` a `erp_webhook.php` con shared secret; el Agent persiste `pdf_url` al instante (indep. del browser) | endpoint dedicado `erp_webhook.php` (ver §3.2) | live, **sin depender de la pestaña** |
| 1 | **Push Soketi** `aw-cotizacion-actualizada` con `pdf_url` — el worker también lo empuja al adjuntar | leer `pdf_url` en el handler `handleAwCotizacionUpdate` (**ya existe**) | live, solo si el vendedor está conectado |
| 2 | **On-demand**: al abrir la orden, `GET /cotizaciones/{id}` ya devuelve `pdfUrl` | ninguno | cuando el vendedor mira la orden |
| 3 | **Poll de respaldo**: el reconcile ya pollea `status='aw_correlated' AND pdf_url IS NULL` (ventana 7 días) | ninguno | respaldo garantizado |

**El #0 (webhook) es el que hace la entrega live confiable**; #1 (Soketi) sigue como
instantáneo cuando la pestaña está abierta; #3 (cron) es el respaldo. Todos
persisten `pdf_url` de forma **idempotente** (`glass_erp_persist_state_change` solo
escribe si estaba vacío) — el primero que llega gana, el resto son no-op.

## 3.1 PROMPT para el agente del Glass Agent (PHP)

> Copiar/pegar al agente que trabaja sobre `glass-agent` (PHP). El backend
> del ERP ya está implementado; esto es solo el lado consumidor.

```
Contexto: el ERP de Millet ahora expone el PDF que A+W genera para cada
oferta/pedido. El ERP NOS INFORMA por Soketi en cuanto adjunta el PDF
(evento aw-cotizacion-actualizada con pdf_url). El PDF llega DESPUÉS de la
correlación, así que el reconcile worker actual (que solo pollea
status='submitted_to_erp') NO lo ve — no agregar un cron nuevo para esto.

Cambios a implementar en glass-agent (PHP):

1. Migración de BD: agregar a `glass_orders` dos columnas nullables:
   - `pdf_url`       VARCHAR(255)  (ruta relativa que devuelve el ERP)
   - `pdf_filename`  VARCHAR(120)
   - (opcional) `pdf_available_at` DATETIME

2. PRINCIPAL — handler del evento Soketi `aw-cotizacion-actualizada`
   (`include/functions_erp.php` / donde se procese el realtime): el payload
   ahora trae `pdf_url` y `pdf_filename`. Cuando `pdf_url` no sea null y la
   orden aún no lo tenga, persistirlo en `glass_orders` y refrescar la card.

3. On-demand: al abrir el detalle de una orden ya se hace
   GET /integraciones/aw/cotizaciones/{erp_id}; ahora trae `pdfUrl`,
   `pdfFilename`, `pdfUploadedAt`. Mostrar el link si viene no-null.
   Nota: `pdfUrl` es una ruta RELATIVA — la URL final = host del ERP + pdfUrl
   (GLASS_ERP_BASE_URL ya incluye /api/v1; no duplicar el prefijo).

4. Descarga: cuando el vendedor abra el PDF, GET a esa URL con header
   `Authorization: Bearer <jwt>` (reusar `glass_erp_get_token()`). El ERP
   responde application/pdf. Servirlo al navegador vía proxy PHP — NO
   exponer el JWT en el front.

5. (Opcional, respaldo) Si se quiere garantizar el pdf_url aunque el
   vendedor no estuviera conectado al recibir el push: AMPLIAR el WHERE del
   reconcile worker existente con
     OR (status = 'aw_correlated' AND pdf_url IS NULL AND aw_correlated_at > NOW() - INTERVAL 7 DAY)
   y persistir pdfUrl desde la respuesta del GET. NO crear un cron nuevo.

Restricciones:
- El Agent NUNCA recibe la URL del blob de Azure; solo la ruta del proxy
  del ERP, que requiere el Bearer JWT del Agent.
- Idempotencia: no re-descargar ni re-persistir si `glass_orders.pdf_url`
  ya está seteado.
```

## 3.2 PROMPT para el webhook live (`erp_webhook.php`)

> El ERP requiere sesión SB en `ajax.php`, así que el webhook server-to-server
> necesita un archivo dedicado con auth por shared secret. Reusa el patrón del
> reconcile worker (`glass_erp_persist_state_change` + `sb_realtime_trigger`).

```
Crear glass-agent/erp_webhook.php (dedicado, NO ajax.php porque exige sesión SB):

1. Auth por shared secret (NO sesión SB): leer header 'X-Webhook-Secret',
   comparar con hash_equals contra GLASS_ERP_WEBHOOK_SECRET (constante nueva en
   config.php + config.example.php; valor real gitignoreado). Falla → 401.
2. require_once config.php + include/functions_erp.php (sin bootstrap de sesión).
3. Body JSON: { erp_id, quote_reference, estado, aw_doc_id, correlated_at,
   pdf_url, pdf_filename, pdf_uploaded_at }. Validar erp_id (GUID) → si no, 400.
4. Buscar la orden por erp_id en glass_orders (para user_id). No existe → 404.
5. Persistir con la función EXISTENTE (idempotente por $pdf_needs_write). PASAR
   correlated_at (autoritativo) para que aw_correlated_at NO se sobreescriba con
   la hora del PDF:
     glass_erp_persist_state_change($erp_id, [
       'estado'=>(int)$estado, 'aw_doc_id'=>$aw_doc_id,
       'correlated_at'=>$correlated_at, 'pdf_url'=>$pdf_url,
       'pdf_filename'=>$pdf_filename, 'pdf_available_at'=>$pdf_uploaded_at ]);
6. Emitir Soketi (refresca pestañas abiertas; mismo payload que
   erp_reconcile_worker.php:122-135, source='erp_webhook'):
     sb_realtime_trigger('private-user-'.(int)$user_id,
       'aw-cotizacion-actualizada', [...]);
7. Responder 200 { ok:true, persisted:<bool> }.

Server-to-server: sin cookies ni JWT de usuario, solo el shared secret.
Idempotente: si pdf_url ya estaba → no-op. El reconcile sigue como respaldo.
```

---

## 4. Configuración del drop service (on-prem)

Sección nueva en `appsettings.json` del drop service
(`on-prem/aw-drop-service`):

```jsonc
"DropService": {
  "Documents": {
    // Carpetas donde A+W exporta los PDF. doc_type se deriva del prefijo
    // del nombre (oferta_/pedido_), no de la carpeta.
    "Folders": [
      "C:\\AW\\Quotes",   // PLATFORM-TODO(<AwDocumentsFolders>): ruta real
      "C:\\AW\\Orders"
    ]
  }
}
```

## 5. Configuración del ERP

```jsonc
"IntegracionesAw": {
  "DocumentSync": {
    "IntervalSeconds": 120,
    "LookbackHours": 72,
    "BatchSize": 500,
    "MaxPagesPerCycle": 20
  },
  // Blob: con ConnectionString → Azure real (container integraciones-aw-blobs,
  // auto-creado); sin ella → stub filesystem local (dev).
  "BlobStorage": { "ConnectionString": "", "ContainerName": "integraciones-aw-blobs" }
}
```
