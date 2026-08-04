# Levantamiento — Módulo Integraciones Mailbox (`Millet.Integraciones.Mailbox`)

> **Proyecto:** ERP Millet — Módulo transversal de ingesta de correo
> electrónico vía Microsoft Graph.
> **Versión:** 0.1 — Draft inicial.
> **Fecha:** 2026-05-24
>
> **Origen:** levantar la cliente Microsoft Graph fuera de CxP a un módulo
> compartido. Hoy `IMailboxClient` vive en
> [`backend/src/CuentasPorPagar/Domain/Mailbox/`](../../../backend/src/CuentasPorPagar/Domain/Mailbox/),
> pero **cada módulo consumidor tendrá un buzón distinto**: CxP =
> `cfdi-recibidos@millet`, Facturación = `cfdi-emitidos@millet`, RH =
> `nominas-correo@millet`, etc. La conexión a Graph es la misma; el UPN
> y los folders son por consumidor.
>
> **Owner del módulo:** Eduardo Paredes — `eduardo.paredes@tiglass.net`
>
> **Patrón:** sigue el exemplar de
> [`Millet.Integraciones.Aw`](../integraciones-aw/00-levantamiento.md).
> Hereda decisiones transversales (hexagonal, CQRS, ADR-0030, ADR-0009,
> ADR-0020, ADR-0021, ADR-0007, ADR-0010, ADR-0012, ADR-0031).

---

## 0. Cómo leer este documento

- `[Verificado]` — leído del código actual o confirmado con el owner.
- `[Inferido]` — deducido por convenciones; no confirmado.
- `[Gap]` — agujero que requiere confirmación.

---

## 1. Contexto

Millet recibe documentos importantes por correo. El que ya está
implementado es la ingesta de CFDIs en CxP (canal `Mailbox`): proveedores
envían su factura PDF/XML a un buzón dedicado, el worker lo lee vía
Microsoft Graph, parsea, persiste el blob, y dispara `IngresarCfdiCommand`.

Casos previstos con el mismo patrón:

1. **CFDI recibidos (CxP)** — ya implementado, hoy en CxP.
2. **CFDI emitidos** que el ERP debe registrar — Facturación futura.
3. **Estados de cuenta TC** que envían los bancos por correo — TC en CxP
   (hoy es carga manual, pero pronto admin querrá auto-ingesta).
4. **Reportes de nómina** del banco / SAT — RH futuro.
5. **Confirmaciones de pago de proveedores** — Tesorería futura.

Todos comparten:

- Mismo cliente Graph (mismo App Registration de Entra ID o múltiples
  según permisos).
- Mismo flujo: poll → leer attachments → procesar → mover a folder
  `Processed` o `Failed`.
- Misma necesidad de credenciales admin-configurables (no solo KV).

Mantener cada uno embebido en su módulo (CxP, Facturación, RH) lleva a
N implementaciones del mismo cliente, retry policies divergentes,
inconsistencia operativa.

> **Cita ancla — CLAUDE.md:** "El sub-namespace `Integraciones` queda
> reservado para integraciones futuras (SAT, bancos, etc.)."

---

## 2. Objetivos

### De negocio

1. **Un buzón por consumidor**, configurable por admin sin redeploy.
2. **Auditable**: timestamps de última ingesta, contador de éxitos/
   fallas, último mensaje procesado por buzón.
3. **Resiliente**: si Graph falla, mensajes quedan en `Failed` folder
   con razón legible; reintento manual desde admin UI.

### Técnicos

1. **Mover** `IMailboxClient` + `GraphMailboxClient` + worker de CxP a
   `Millet.Integraciones.Mailbox`.
2. **Generalizar** el contrato del worker: el consumidor (CxP,
   Facturación, etc.) registra un `IMailboxMessageHandler` con su
   purpose key (`"cfdi-recibidos"`, `"cfdi-emitidos"`, etc.); el worker
   despacha mensajes al handler correspondiente según el buzón.
3. **Persistir** configuración por buzón: tabla
   `integraciones_mailbox.configuracion_buzon` con `purpose`,
   `mailbox_upn`, credenciales cifradas, folders, intervalo.
4. **Admin UI** para CRUD de buzones con test connection.

### Métricas

- Tras cutover, CxP **no** referencia `GraphMailboxClient` ni
  `MailboxOptions` — solo el handler que registra.
- Admin agrega un buzón nuevo en < 5 minutos sin redeploy.
- 0 credenciales en logs / queries / backups.

---

## 3. Alcance

### En el módulo (fase 1)

- Cliente Graph reusable (mismo App Registration; configurable por
  buzón si se requiere multi-tenant Entra).
- Tabla `integraciones_mailbox.configuracion_buzon`:
  - `purpose` (clave del consumidor: `"cfdi-recibidos"`,
    `"cfdi-emitidos"`, …).
  - `empresa_id` (multi-empresa per ADR-0030).
  - `tenant_id`, `client_id`, `client_secret_cifrado`, `mailbox_upn`.
  - `inbox_folder`, `processed_folder`, `failed_folder`.
  - `intervalo_segundos`, `max_mensajes_por_tick`, `activo`.
- Tabla `integraciones_mailbox.mensajes_procesados`:
  - `(buzon_id, graph_message_id, fecha_procesado, exitoso, error_texto)`.
  - Index único `(buzon_id, graph_message_id)` para dedupe.
- Worker `MailboxIngestionWorker` itera buzones activos por tick.
- Contrato `IMailboxMessageHandler`:
  ```csharp
  public interface IMailboxMessageHandler
  {
      string Purpose { get; }
      Task<MailboxHandleResult> HandleAsync(MailboxMessage msg, CancellationToken ct);
  }
  ```
- Endpoints admin:
  - `GET/POST/PUT/DELETE /api/v1/integraciones/mailbox/buzones`
  - `POST /api/v1/integraciones/mailbox/buzones/{id}/test`
  - `GET /api/v1/integraciones/mailbox/buzones/{id}/historial`
- Outbox + evento `IntegracionesMailboxConfiguracionActualizadaEvent`.
- Permisos canónicos:
  - `integraciones.mailbox.leer`
  - `integraciones.mailbox.administrar`

### Fuera del módulo (fase 1)

- **Parseo de XML CFDI** — queda en CxP (`IXmlCfdiParser`). El handler
  de CxP transforma `MailboxMessage` → `IngresarCfdiCommand`.
- **Lógica de qué hacer con el mensaje procesado** — del consumidor.
- **App Registration de Entra ID** — operación, no código. Doc en
  runbook §10.

---

## 4. No-objetivos

- **NO** reemplaza a Microsoft Graph. Es cliente HTTP.
- **NO** parsea contenido — solo entrega `MailboxMessage` con
  attachments al handler.
- **NO** maneja attachments arbitrarios > 25 MB (límite de Graph).
- **NO** soporta IMAP/POP3 — solo Microsoft 365 vía Graph.
- **NO** envía correos. Solo lee.

---

## 5. Stakeholders

| Rol | Quién | Responsabilidad |
|---|---|---|
| Sponsor | Eduardo Paredes | Aprueba alcance |
| Owner técnico | Eduardo Paredes | Diseño + revisión |
| Operador admin | Auxiliar CxP / DBA | Crea buzones + rota credenciales |
| Operador Entra ID | Admin de Microsoft 365 (Eduardo / IT) | App Registration + permisos Mail.ReadWrite |
| Consumidor fase 1 | Módulo CxP (cfdi-recibidos) | Handler ya existe |
| Consumidor fase 2 | Módulo Facturación, TC, RH | Handlers nuevos cuando lleguen |
| Proveedor externo | Microsoft Graph API | API REST |

---

## 6. Casos de uso

### CU-1 — Alta de buzón "cfdi-recibidos" en empresa Millet S.A.

1. Admin Entra ID crea App Registration con permiso `Mail.ReadWrite`
   delegado al buzón `cfdi@millet.com.mx`.
2. Admin Entra ID **carga un certificate** (auto-firmado o de CA) en el
   App Registration. Copia `TenantId`, `ClientId`, **`thumbprint`**.
3. Admin Entra ID o DBA sube el `.pfx` del certificate a Azure Key Vault
   como `Certificate` (operación de infra, no del ERP).
4. Admin ERP entra a `/admin/integraciones/mailbox`.
5. Click "Nuevo buzón". Captura:
   - Empresa: Millet S.A.
   - Purpose: `cfdi-recibidos` (dropdown — los purposes los registran
     los módulos consumidores).
   - Mailbox UPN: `cfdi@millet.com.mx`.
   - TenantId, ClientId.
   - **CertificateKeyVaultName** (dropdown con certificates disponibles
     en el KV de la empresa) + thumbprint auto-rellenado.
   - Folders (defaults `Inbox` / `Processed` / `Failed`).
6. Click "Test conexión" → backend hace `GET /users/{upn}/messages?$top=1`
   autenticándose con el certificate del KV (no se persiste config aún).
7. Si OK, "Guardar". Backend persiste la fila (sin secretos en BD; KV es
   el source-of-truth del certificate).
8. Worker en próximo tick procesa el buzón.

### CU-2 — Reintento manual de mensaje fallido

1. Admin entra a `/admin/integraciones/mailbox/{id}/historial`.
2. Filtra `exitoso=false`. Selecciona un mensaje.
3. Click "Reintentar" → backend mueve el mensaje del folder `Failed` a
   `Inbox` y borra la entry de `mensajes_procesados` (próximo tick lo
   reprocesa).

### CU-3 — Auditoría de ingesta

1. Admin entra a `/admin/integraciones/mailbox/{id}/historial`.
2. Ve métrica: últimas 24h, 100 mensajes procesados, 98 exitosos, 2
   fallos con motivo.
3. Click en uno fallido → ve el error trace.

---

## 7. Decisiones cerradas

| ID | Decisión | Confirmado por |
|---|---|---|
| D1 | Nombre del módulo: `Millet.Integraciones.Mailbox` | Eduardo, 2026-05-24 |
| D2 | Multi-empresa + multi-purpose: una configuración por (empresa, purpose) | Eduardo, 2026-05-24 |
| D3 | **Autenticación Entra ID con Certificate** (no Client Secret). El certificate vive en Key Vault como `Certificate`; la fila en BD solo guarda `certificate_keyvault_name` + `certificate_thumbprint`. **No requiere cifrado de columna** — KV maneja el almacenamiento seguro nativamente. Vencimiento configurable hasta 2 años (vs. 180 días default del Client Secret). | Eduardo, 2026-05-24 |
| D4 | Workers `IHostedService` dentro de `Millet.Api`. | Convención |
| D5 | Sólo Microsoft Graph (no IMAP). | Convención (Millet usa M365) |
| D6 | Rotación: Certificate del App Registration cada 1-2 años (manual con admin Entra ID + upload al ERP). Sin Client Secret, no hay ciclo de 180 días. | Eduardo, 2026-05-24 |

---

## 8. Riesgos y mitigaciones

| Riesgo | Probabilidad | Impacto | Mitigación |
|---|---|---|---|
| Cutover desde CxP rompe ingesta de CFDIs | Media | Alto | Feature flag + shim. PR de cutover separado. |
| Certificate referenciado por nombre en logs | Baja | Bajo | El thumbprint en logs es público — no compromete nada. La key privada nunca sale de Key Vault. |
| Permiso Mail.ReadWrite mal otorgado lee correos no destinados | Media | Crítico | Admin Entra ID otorga permiso **delegado al UPN específico**, no Application-wide. Validación en endpoint admin. |
| Certificate del App Registration expira | Media | Medio | Admin UI muestra `vencimiento_certificado` (read del KV Certificate); banner amarillo a los 30 días, rojo a los 7. Vencimientos típicos de 1-2 años son mucho más manejables que los 180 días del client secret. |
| Mensajes huge attachments saturan worker | Baja | Medio | Límite duro: rechazar attachments > 25 MB con `MailboxHandleResult.Failed("AttachmentTooLarge")`. |
| Mismo mensaje procesado dos veces | Baja | Medio | Index único `(buzon_id, graph_message_id)` previene re-inserts. |

---

## 9. Glosario

- **UPN** — User Principal Name. Email canónico del buzón (`cfdi@millet.com.mx`).
- **Purpose** — clave que identifica al consumidor del buzón (`cfdi-recibidos`,
  `cfdi-emitidos`, etc.). El handler de cada módulo registra su purpose.
- **App Registration** — registro en Entra ID que da credenciales a la
  aplicación para llamar Graph.
- **Mail.ReadWrite** — permiso de Graph para leer y mover mensajes de un
  mailbox.
- **Folder** — carpeta del mailbox. Inbox / Processed / Failed son
  convención de este módulo.

---

## 10. Anexo — Mapeo del estado actual (CxP) al nuevo módulo

| Componente actual (CxP) | Destino (Integraciones.Mailbox) | Notas del cutover |
|---|---|---|
| [`IMailboxClient.cs`](../../../backend/src/CuentasPorPagar/Domain/Mailbox/) | Eliminado — el contrato del módulo nuevo es `IMailboxMessageHandler`, no `IMailboxClient`. | El cliente Graph queda como detalle interno del módulo. |
| [`GraphMailboxClient.cs`](../../../backend/src/CuentasPorPagar/Infrastructure/Mailbox/GraphMailboxClient.cs) | `Integraciones.Mailbox.Infrastructure.GraphMailboxClient` | Resuelve credenciales por buzón. |
| [`MailboxOptions.cs`](../../../backend/src/CuentasPorPagar/Infrastructure/Mailbox/MailboxOptions.cs) | Reemplazado por tabla `configuracion_buzon` | Eliminado |
| [`NoOpMailboxClient.cs`](../../../backend/src/CuentasPorPagar/Infrastructure/Mailbox/NoOpMailboxClient.cs) | Eliminado — si no hay buzón activo, el worker ni siquiera lo procesa. | — |
| [`CfdiMailboxIngestionWorker.cs`](../../../backend/src/CuentasPorPagar/Infrastructure/Workers/CfdiMailboxIngestionWorker.cs) | Se transforma en `CfdiRecibidosMailboxHandler : IMailboxMessageHandler` (vive en CxP). El worker general queda en `Integraciones.Mailbox`. | El handler invoca `IngresarCfdiCommand`. |

---

## 11. Pendientes para el `01-diseno.md`

1. Shape exacto de `MailboxMessage` (qué campos del mensaje Graph expone
   al handler).
2. Política de manejo de `MailboxHandleResult`: ¿el módulo mueve a
   `Failed` o el handler decide? (Recomendación: el módulo mueve según
   el result returned.)
3. Cómo se registran los purposes: ¿enum hardcoded, tabla de catálogo,
   o discovery por DI scan de `IMailboxMessageHandler`?
4. Multi-tenant Entra: ¿soportamos buzones de tenants distintos al de
   Millet? (Probablemente no en fase 1.)
5. Endpoint de reintento manual: ¿requiere permiso adicional?
