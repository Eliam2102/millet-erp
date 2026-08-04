# Diseño — Módulo Integraciones Mailbox (`Millet.Integraciones.Mailbox`)

> **Lee primero:** [`00-levantamiento.md`](00-levantamiento.md).
> **Versión:** 0.1 — Draft inicial, 2026-05-24.

---

## 1. Arquitectura

```
┌──────────────────────────────────────────────────────────────┐
│              Millet.Integraciones.Mailbox                     │
│                                                                │
│  Domain                                                        │
│   - IMailboxMessageHandler (puerto INVERSO — lo implementan    │
│     los módulos consumidores)                                  │
│   - MailboxMessage, MailboxHandleResult (records)              │
│   - ConfiguracionBuzon (agregado)                              │
│   - MensajeProcesado (entidad — dedupe + auditoría)            │
│                                                                │
│  Application                                                   │
│   - GuardarConfiguracionBuzonCommand + Handler                 │
│   - TestConexionBuzonCommand + Handler                         │
│   - ReintentarMensajeFallidoCommand                            │
│   - ListarHistorialBuzonQuery                                  │
│                                                                │
│  Infrastructure                                                │
│   - IntegracionesMailboxDbContext (schema integraciones_mailbox)│
│   - GraphMailboxClient                                          │
│   - MailboxIngestionWorker (BackgroundService)                 │
│   - SecretCipher (reusa el de Integraciones.Fiscal? — ver §6)   │
└──────────────────────────────────────────────────────────────┘
         │                                            ▲
         │ resuelve handler por purpose               │
         │                                            │
         ▼                                            │
┌────────────────────────────┐               ┌────────────────┐
│ CxP                        │               │ Facturación    │
│ CfdiRecibidosHandler       │               │ CfdiEmitidos   │
│ (implements                │               │ Handler        │
│  IMailboxMessageHandler)   │               │ (futuro)       │
└────────────────────────────┘               └────────────────┘
```

### Project references

```
Integraciones.Mailbox → SharedKernel, Identidad
CuentasPorPagar       → Integraciones.Mailbox (implementa IMailboxMessageHandler)
Facturación (futuro)  → Integraciones.Mailbox
Api                   → Integraciones.Mailbox (wiring + endpoints)
```

### Pattern: puerto inverso

`IMailboxMessageHandler` se declara en `Integraciones.Mailbox.Domain`,
pero lo **implementan** los consumidores (CxP registra su
`CfdiRecibidosMailboxHandler`). En DI:

```csharp
// CxP registra:
services.AddScoped<IMailboxMessageHandler, CfdiRecibidosMailboxHandler>();

// El worker resuelve todos los IEnumerable<IMailboxMessageHandler> y
// matchea por purpose con la config del buzón.
```

---

## 2. Schema Postgres

Schema dedicado `integraciones_mailbox`.

### `configuracion_buzon`

```sql
CREATE TABLE integraciones_mailbox.configuracion_buzon (
  id                         uuid PRIMARY KEY,
  empresa_id                 uuid NOT NULL,
  purpose                    varchar(64) NOT NULL,    -- "cfdi-recibidos", "cfdi-emitidos", ...
  nombre                     varchar(200) NOT NULL,   -- legible: "CFDI recibidos Millet S.A."
  tenant_id                  varchar(64) NOT NULL,
  client_id                  varchar(64) NOT NULL,
  -- Certificate-based auth (D3): referencias al KV, nunca el certificate
  -- en sí. El KV es el source-of-truth del certificate.
  certificate_keyvault_name  varchar(127) NOT NULL,   -- nombre del Certificate en KV
  certificate_thumbprint     varchar(40)  NOT NULL,   -- para detectar cambios + audit
  mailbox_upn                varchar(254) NOT NULL,
  inbox_folder               varchar(100) NOT NULL DEFAULT 'Inbox',
  processed_folder           varchar(100) NOT NULL DEFAULT 'Processed',
  failed_folder              varchar(100) NOT NULL DEFAULT 'Failed',
  intervalo_segundos         int NOT NULL DEFAULT 300,
  max_mensajes_por_tick      int NOT NULL DEFAULT 25,
  timeout_segundos_graph     int NOT NULL DEFAULT 30,
  activo                     boolean NOT NULL DEFAULT true,
  ultima_rotacion_at         timestamptz,
  vencimiento_certificado_at timestamptz,             -- snapshot del KV para alertas UI
  ultima_test_at             timestamptz,
  ultima_test_exitosa        boolean,
  ultimo_tick_at             timestamptz,
  ultimo_error_texto         text,
  version                    int NOT NULL DEFAULT 1,
  created_at                 timestamptz NOT NULL,
  updated_at                 timestamptz NOT NULL,
  created_by                 text,
  updated_by                 text,
  deleted_at                 timestamptz,
  CONSTRAINT uq_configuracion_buzon_empresa_purpose
    UNIQUE (empresa_id, purpose)
);

CREATE INDEX ix_configuracion_buzon_activo
  ON integraciones_mailbox.configuracion_buzon (activo)
  WHERE deleted_at IS NULL;
```

> **Sin columnas cifradas.** La fila solo guarda **referencias** al
> Key Vault. La key privada del certificate nunca toca la BD ni la
> aplicación — `CertificateClient` la mantiene en KV y solo expone
> operaciones de sign cuando se autentica contra Microsoft Graph.

### `mensajes_procesados`

```sql
CREATE TABLE integraciones_mailbox.mensajes_procesados (
  id                  uuid PRIMARY KEY,
  buzon_id            uuid NOT NULL,
  graph_message_id    varchar(200) NOT NULL,
  asunto              varchar(500),
  remitente           varchar(254),
  fecha_recibido      timestamptz,
  fecha_procesado     timestamptz NOT NULL,
  exitoso             boolean NOT NULL,
  error_texto         text,
  resultado_json      jsonb,  -- payload del handler, ej. { cfdiId: "...", canalOrigen: "Mailbox" }
  CONSTRAINT uq_mensajes_procesados_buzon_graph_id
    UNIQUE (buzon_id, graph_message_id),
  CONSTRAINT fk_mensajes_procesados_buzon
    FOREIGN KEY (buzon_id)
    REFERENCES integraciones_mailbox.configuracion_buzon (id)
    ON DELETE CASCADE
);

CREATE INDEX ix_mensajes_procesados_buzon_fecha
  ON integraciones_mailbox.mensajes_procesados (buzon_id, fecha_procesado DESC);
CREATE INDEX ix_mensajes_procesados_exitoso
  ON integraciones_mailbox.mensajes_procesados (buzon_id, exitoso, fecha_procesado DESC);
```

---

## 3. Contrato del handler

```csharp
namespace Millet.Integraciones.Mailbox.Domain;

public sealed record MailboxMessage(
    string GraphMessageId,
    string Asunto,
    string Remitente,
    DateTimeOffset FechaRecibido,
    IReadOnlyList<MailboxAttachment> Attachments,
    string? CuerpoTextoPlano);

public sealed record MailboxAttachment(
    string Nombre,
    string ContentType,
    long Tamano,
    Func<CancellationToken, Task<Stream>> AbrirContenidoAsync);

public abstract record MailboxHandleResult
{
    public sealed record Exitoso(object? PayloadAuditoria = null) : MailboxHandleResult;
    public sealed record Fallido(string CodigoError, string Mensaje) : MailboxHandleResult;
    public sealed record Ignorado(string Motivo) : MailboxHandleResult;  // No mover a Failed; queda en Inbox o se descarta.
}

public interface IMailboxMessageHandler
{
    /// <summary>
    /// Clave del consumidor. El worker matchea contra
    /// <c>ConfiguracionBuzon.Purpose</c>.
    /// </summary>
    string Purpose { get; }

    Task<MailboxHandleResult> HandleAsync(
        MailboxMessage mensaje,
        CancellationToken cancellationToken);
}
```

### Routing en el worker

Por cada `ConfiguracionBuzon activo` el worker:

1. Resuelve `IEnumerable<IMailboxMessageHandler>` del DI scope.
2. Busca `h.Purpose == buzon.Purpose`. Si no hay handler → loggea
   warning, marca buzón con `ultimo_error_texto = "Sin handler"` y
   continúa (sin throw).
3. Itera mensajes del Graph (con dedupe via `mensajes_procesados`).
4. Para cada mensaje, invoca `handler.HandleAsync(...)`.
5. Mapea el `MailboxHandleResult` a acción:
   - `Exitoso` → mueve a `processed_folder`, inserta en
     `mensajes_procesados` con `exitoso=true`.
   - `Fallido` → mueve a `failed_folder`, inserta con `exitoso=false` +
     `error_texto`.
   - `Ignorado` → no mueve, no inserta. (Útil para correos del jefe que
     se cuelan al buzón de cfdi.)

---

## 4. Autenticación con Certificate (no Client Secret)

**Decisión D3**: el App Registration de Entra ID se autentica con
**Certificate** (formato `.pfx`), no con Client Secret. Razones:

- **Vencimiento más largo**: 1-2 años configurable vs. 180 días default
  del Client Secret. Reduce frecuencia de rotación significativamente.
- **Auditoría granular en Key Vault**: cada uso del certificate queda
  en el log de KV con identidad del caller.
- **Key privada nunca sale de Key Vault**: las operaciones de firma
  ocurren dentro de KV (`SignData`); la app solo ve el resultado.
- **Sin cifrado de columna en la BD** — la fila guarda solo
  `certificate_keyvault_name` + `thumbprint` (ambos públicos).

### Setup

1. Operador genera o adquiere un certificate (auto-firmado válido para
   App Registrations de Entra ID, o de CA si la org lo requiere).
2. Operador sube `.pfx` a Key Vault como `Certificate` con nombre
   semántico (ej. `mailbox-cfdi-recibidos-millet-2026`).
3. Operador carga la parte pública (`.cer`) al App Registration en
   Entra ID, sección "Certificates & secrets".
4. Admin ERP crea fila en `configuracion_buzon` referenciando
   `certificate_keyvault_name`.

### `IGraphTokenAcquirer`

```csharp
public sealed class GraphTokenAcquirer : IGraphTokenAcquirer
{
    private readonly CertificateClient _certClient;
    private readonly IMemoryCache _cache;

    public async Task<AccessToken> AdquirirAsync(
        ConfiguracionBuzon config, CancellationToken ct)
    {
        // 1. Lee el cert del KV (cacheado 1h con TTL).
        var cert = await _certClient.DownloadCertificateAsync(
            config.CertificateKeyvaultName, cancellationToken: ct);

        // 2. Construye ClientCertificateCredential.
        var credential = new ClientCertificateCredential(
            config.TenantId,
            config.ClientId,
            cert.Value);

        // 3. Adquiere token para Graph.
        var token = await credential.GetTokenAsync(
            new TokenRequestContext(["https://graph.microsoft.com/.default"]),
            ct);

        return token;
    }
}
```

### Auto-detección de vencimiento

Job semanal `CertificateExpiryCheckJob` (ADR-0022):

1. Por cada `configuracion_buzon` activa, lee el cert del KV.
2. Actualiza `vencimiento_certificado_at` con `cert.Properties.ExpiresOn`.
3. Si vence en < 30 días → publica `IntegracionesMailboxCertificadoProximoAExpirarEvent`
   al Outbox (consumido por Notificaciones cuando llegue).

### Sin DataProtection en este módulo

A diferencia de `Integraciones.Fiscal` que usa DataProtection (porque
el `ApiKey` es un string que va en BD), **este módulo no necesita
cifrado de columna**. KV gestiona la seguridad del certificate
nativamente. ADR-0037 sigue aplicando a Fiscal; **no se invoca aquí**.

---

## 5. Endpoints admin

Base: `/api/v1/integraciones/mailbox`. Permisos:
`integraciones.mailbox.administrar` (mutaciones),
`integraciones.mailbox.leer` (GETs).

### `GET /buzones`

Lista todos los buzones de las empresas a las que el usuario tiene
acceso. Mask `clientSecret` siempre.

### `GET /buzones/{id}`

```json
{
  "id": "...",
  "empresaId": "...",
  "purpose": "cfdi-recibidos",
  "nombre": "CFDI recibidos Millet S.A.",
  "tenantId": "...",
  "clientId": "...",
  "certificateKeyvaultName": "mailbox-cfdi-recibidos-millet-2026",
  "certificateThumbprint": "A1B2C3D4...",
  "vencimientoCertificadoAt": "2027-05-01T00:00:00Z",
  "mailboxUpn": "cfdi@millet.com.mx",
  "inboxFolder": "Inbox",
  "processedFolder": "Processed",
  "failedFolder": "Failed",
  "intervaloSegundos": 300,
  "maxMensajesPorTick": 25,
  "timeoutSegundosGraph": 30,
  "activo": true,
  "ultimaRotacionAt": "2026-05-01T...",
  "ultimaTestAt": "2026-05-24T...",
  "ultimaTestExitosa": true,
  "ultimoTickAt": "2026-05-24T17:35:00Z",
  "ultimoErrorTexto": null,
  "purposesDisponibles": ["cfdi-recibidos", "cfdi-emitidos"]
}
```

### `GET /certificates-disponibles?empresaId=...`

Lista certificates del KV de la empresa filtrando por convención de
nombre (ej. prefijo `mailbox-`). Sirve al dropdown del admin UI.

```json
{
  "items": [
    {
      "keyvaultName": "mailbox-cfdi-recibidos-millet-2026",
      "thumbprint": "A1B2C3D4...",
      "vencimientoAt": "2027-05-01T00:00:00Z",
      "subject": "CN=ERP Millet Mailbox cfdi-recibidos"
    }
  ]
}
```

### `POST /buzones`

Crear buzón. Body con todos los campos excepto `id`. Idempotency-Key
obligatorio. **No incluye client secret** — solo
`certificateKeyvaultName`.

### `PUT /buzones/{id}`

- ETag obligatorio.
- `certificateKeyvaultName: null` → no toca; con valor → rota
  referencia al certificate nuevo (puede haber varias rotaciones
  paralelas como pre-deployment de un cert nuevo).

### `POST /buzones/{id}/test`

Body: `{ "certificateKeyvaultName": "..." }` opcional (si falta, usa la
actual de la config).

Backend hace `GET /users/{upn}/messages?$top=1&$select=id` autenticándose
con el certificate del KV. Response:
`{ "exitosa": bool, "mensaje": "...", "tiempoMs": 234, "thumbprintUsado": "..." }`.

### `GET /buzones/{id}/historial`

Paginado. Filtros: `exitoso`, `desde`, `hasta`.

```json
{
  "items": [
    {
      "id": "...",
      "graphMessageId": "...",
      "asunto": "Factura 123",
      "remitente": "proveedor@ejemplo.com",
      "fechaRecibido": "2026-05-24T15:00:00Z",
      "fechaProcesado": "2026-05-24T15:05:12Z",
      "exitoso": true,
      "errorTexto": null
    }
  ],
  "total": 234,
  "offset": 0,
  "limit": 50
}
```

### `POST /historial/{id}/reintentar`

- Mueve el mensaje de `failed_folder` a `inbox_folder`.
- Borra la entry de `mensajes_procesados` (próximo tick lo reprocesa).
- Requiere permiso adicional (TBD — pendiente §11).

---

## 6. Discovery de purposes

Para que el dropdown del admin UI muestre purposes válidos, el módulo
expone:

```csharp
public sealed class PurposeCatalog
{
    private readonly IEnumerable<IMailboxMessageHandler> _handlers;
    public IReadOnlyList<string> PurposesDisponibles =>
        _handlers.Select(h => h.Purpose).Distinct().OrderBy(x => x).ToList();
}
```

El endpoint `GET /buzones/purposes` retorna esta lista. Si un consumidor
nuevo registra un handler con purpose `"reportes-nomina"`, aparece
automáticamente en el dropdown sin cambios en la UI.

---

## 7. Cutover desde CxP

### Estrategia

**Fase A** — Crear módulo nuevo + esquema. Sin tocar CxP.

**Fase B** — Crear `CfdiRecibidosMailboxHandler` en CxP que implementa
`IMailboxMessageHandler`. Mismo flujo de hoy:
`MailboxMessage` → `IngresarCfdiCommand`. Registrado en DI pero el
worker **viejo** sigue corriendo.

**Fase C** — Cutover con feature flag
`Integraciones.Mailbox.Enabled`:
- `false` (default inicial): worker viejo de CxP corre.
- `true`: worker viejo está disabled (`MailboxOptions.IsConfigured`
  retorna false porque DI ya no lo wirea); worker nuevo lee
  `configuracion_buzon` y despacha al handler.
- Seed inicial: migration copia `MailboxOptions` de appsettings a una
  fila de `configuracion_buzon` con `purpose='cfdi-recibidos'`.

**Fase D** — Cleanup: borrar `CfdiMailboxIngestionWorker`,
`GraphMailboxClient`, `MailboxOptions`, `NoOpMailboxClient` de CxP.

---

## 8. Frontend admin UI

Página `/admin/integraciones/mailbox`. Patrón master-detail.

- **Lista**: empresa, purpose, UPN, último tick, último error, activo.
- **Detalle** secciones:
  1. **Identificación**: empresa (dropdown), purpose (dropdown desde
     `/buzones/purposes`), nombre legible.
  2. **Credenciales Entra ID**: TenantId, ClientId, MailboxUpn,
     **Certificate** (dropdown desde `/certificates-disponibles?empresaId=...`).
     El dropdown muestra `keyvaultName + vencimiento + thumbprint`.
  3. **Folders**: Inbox, Processed, Failed (defaults).
  4. **Schedule**: intervalo, max-por-tick, timeout.
  5. **Estado** (read-only): última rotación, vencimiento del cert con
     badge (verde > 30d, amarillo 7-30d, rojo < 7d), último tick,
     último error, contador últimas 24h.
- **Tabs**: General | Historial.
  - Historial: tabla paginada con filtros `exitoso` / fecha / asunto.
    Click en fila → drawer con detalle. Botón "Reintentar" si
    `exitoso=false`.

---

## 9. Plan de PRs

| # | Branch | Scope | Tamaño |
|---|---|---|---|
| 1 | `docs/integraciones-fiscal-mailbox` | (ya: este doc + el de Fiscal) | XS |
| 2 | `feature/integraciones-mailbox-foundation` | Proyecto + DbContext + tablas + migrations + permisos | M |
| 3 | `feature/integraciones-mailbox-contrato` | `IMailboxMessageHandler`, `MailboxMessage`, `PurposeCatalog` | S |
| 4 | `feature/integraciones-mailbox-endpoints` | Endpoints admin + handlers + tests | M |
| 5 | `feature/integraciones-mailbox-worker` | `GraphMailboxClient` + worker + routing | M |
| 6 | `feature/integraciones-mailbox-cxp-handler` | `CfdiRecibidosMailboxHandler` en CxP | S |
| 7 | `feature/integraciones-mailbox-cutover` | Feature flag + cutover en dev | S |
| 8 | `feature/integraciones-mailbox-frontend` | Admin UI completo | M |
| 9 | `chore/integraciones-mailbox-cleanup` | Borrar código viejo de CxP | S |

Total: ~5-6 semanas si paraleliza con Integraciones.Fiscal en ramas
distintas. **No hay dependencia técnica** entre los dos módulos (solo
comparten patrón de cifrado).

---

## 10. Pendientes que requieren confirmación

1. **Multi-tenant Entra ID**: ¿soportamos buzones de tenants distintos
   al de Millet? Recomendación: **no** en fase 1 — un solo tenant.
2. **Endpoint de reintento**: ¿requiere permiso
   `integraciones.mailbox.administrar` o uno adicional
   `integraciones.mailbox.reintentar`?
3. **Compartir handler entre purposes**: ¿un handler puede manejar
   varios purposes (lista) o solo uno? Recomendación: uno (más
   explícito; si quieres más, registra dos handlers).
4. **Notificaciones**: ¿alertar al admin si un buzón tiene > 5 fallos
   consecutivos? Necesita módulo Notificaciones (PLATFORM-TODO).
5. **Cuotas**: Microsoft Graph tiene rate limits. ¿Cómo respondemos a
   429? Polly retry-after es lo estándar.
6. **Backup del DEK**: si Key Vault del ambiente se pierde, los
   ciphertext son irrecuperables. Doc de recovery en runbook §10.
