# Levantamiento — Módulo Integraciones Fiscal (`Millet.Integraciones.Fiscal`)

> **Proyecto:** ERP Millet — Módulo transversal de integraciones con
> autoridad fiscal y PACs.
> **Versión:** 0.1 — Draft inicial.
> **Fecha:** 2026-05-24
>
> **Origen:** levantar la cliente HTTP de FiscalAPI fuera de CxP a un
> módulo compartido. Hoy `IFiscalApiClient` vive en
> [`backend/src/CuentasPorPagar/Domain/Fiscal/`](../../../backend/src/CuentasPorPagar/Domain/Fiscal/),
> pero pronto lo consumirán Facturación (timbrado CFDI 4.0) y Documentos
> (descarga XML/PDF al blob storage).
>
> **Owner del módulo:** Eduardo Paredes — `eduardo.paredes@tiglass.net`
>
> **Patrón:** sigue el exemplar de
> [`Millet.Integraciones.Aw`](../integraciones-aw/00-levantamiento.md).
> Hereda decisiones transversales del backend: hexagonal + CQRS,
> multi-DbContext (ADR-0030), Outbox (ADR-0009), Idempotency-Key
> (ADR-0020), versionado `/api/v1/` (ADR-0021), RBAC granular
> (ADR-0007), Problem Details (ADR-0010), ETag (ADR-0012), PLATFORM-TODO
> (ADR-0031).

---

## 0. Cómo leer este documento

- `[Verificado]` — leído del código actual o confirmado con el owner.
- `[Inferido]` — deducido por convenciones; no confirmado.
- `[Gap]` — agujero que requiere confirmación.

---

## 1. Contexto

Millet integra con el SAT (autoridad fiscal de México) y con un PAC
(Proveedor Autorizado de Certificación) para todo el ciclo fiscal:

1. **Descarga de CFDIs recibidos** — para que CxP pueda procesar pasivos
   de proveedores que llegan vía SAT (canal `DescargaSat`). Hoy en
   producción se usa **FiscalAPI** como PAC intermediario que consulta
   el SAT y entrega CFDIs ya descargados.
2. **Validación de estado SAT** — consultar si un UUID sigue vigente
   o fue cancelado por el emisor. CxP lo usa para refresh periódico de
   CFDIs `PorProcesar`.
3. **Timbrado de CFDIs emitidos** — futuro consumidor **Facturación**.
   El timbre lo da el PAC; el ERP construye el XML, lo sella con la CSD
   propia, y el PAC agrega el TFD (Timbre Fiscal Digital).
4. **Cancelación de CFDIs emitidos** — futuro consumidor Facturación.
   Flujo post-CFDI 4.0 con aprobación del receptor.
5. **Respuesta a solicitudes de cancelación recibidas** — cuando un
   emisor cancela un CFDI dirigido a Millet, hay que aceptar o rechazar.

**Decisión (2026-05-24):** Millet adopta **FiscalAPI como PAC único**
para los 5 flujos (ver [ADR-0038](../../decisiones/0038-fiscalapi-pac-unico.md)).
La idea original de tener OneFactura para timbrado + FiscalAPI para
descarga (ADR-0027, ahora reemplazado) queda descartada — un solo
contrato, una sola admin UI, una sola abstracción.

Hoy las funciones de descarga (#1 y #2) viven embebidas en CxP. Cuando
entre Facturación (prevista Q3 2026) y eventualmente Documentos
(descarga masiva de XML/PDF al blob), van a requerir el mismo cliente
HTTP, las mismas credenciales, la misma configuración multi-empresa.
Mantenerlo en CxP deja deuda arquitectónica.

> **Cita ancla — CLAUDE.md:** "El sub-namespace `Integraciones` queda
> reservado para integraciones futuras (SAT, bancos, etc.)."

---

## 2. Objetivos

### De negocio

1. **Permitir al admin configurar credenciales de PAC desde la UI**, no
   solo desde Key Vault al deploy. Rotación, alta de empresa nueva y
   troubleshooting "test connection" sin pasar por DevOps.
2. **Una configuración por empresa.** Millet puede tener varias razones
   sociales con PACs distintos o el mismo PAC con cuentas separadas.
3. **Habilitar Facturación y Documentos** sin que tengan que reimplementar
   el cliente HTTP, retry/circuit breaker, ni la persistencia de
   credenciales.

### Técnicos

1. **Mover** `IFiscalApiClient` + workers `DescargaMasivaSat` +
   `EstadoSatRefresh` de `Millet.CuentasPorPagar` a
   `Millet.Integraciones.Fiscal`.
2. **Exponer** `IFiscalApiClient` como **puerto público** del módulo
   (mismo patrón que `IComprasOcReadPort` del ADR-0030 — Compras lo
   expone, CxP lo consume).
3. **Persistir** la configuración (BaseUrl, ApiKey, timeouts, RFCs
   receptores, schedule) en su propio schema `integraciones_fiscal` con
   cifrado at-rest del ApiKey.
4. **Admin UI** funcional para CRUD de la configuración con
   "test connection" antes de guardar.

### Métricas de éxito

- Tras el cutover, CxP **no** referencia `FiscalApiHttpClient`
  directamente — solo el puerto.
- Admin puede agregar una empresa nueva con FiscalAPI en < 5 minutos sin
  redeploy.
- Workers de descarga corren con credenciales **por empresa** (sin
  hardcode global `RfcsReceptores`).
- 0 incidentes de credencial filtrada en logs/queries/backups (cifrado
  efectivo).

---

## 3. Alcance

### En el módulo (fase 1 — descarga)

- Cliente HTTP `IFiscalApiClient` con Polly retry + circuit breaker
  (mismo shape que hoy).
- Tabla `integraciones_fiscal.configuracion_pac` por empresa:
  `(empresa_id, base_url, api_key_cifrado, timeouts, retry, activo)`.
- Tabla `integraciones_fiscal.rfcs_receptores` por empresa:
  `(empresa_id, rfc, descarga_habilitada, refresh_habilitada)`.
- Workers `DescargaMasivaSatWorker` + `EstadoSatRefreshWorker` movidos
  aquí — corren un tick por cada configuración activa.
- Endpoints admin:
  - `GET/PUT /api/v1/integraciones/fiscal/configuracion/{empresaId}`
  - `POST /api/v1/integraciones/fiscal/configuracion/{empresaId}/test`
  - `GET/POST/DELETE /api/v1/integraciones/fiscal/rfcs-receptores`
- Puerto público `IFiscalApiClient` para CxP (y futuros consumidores).
- Outbox + eventos:
  - `IntegracionesFiscalConfiguracionActualizadaEvent` (informativo para
    auditoría / logging cruzado).
- Permisos canónicos nuevos:
  - `integraciones.fiscal.leer`
  - `integraciones.fiscal.administrar`

### En el módulo (fase 2 — emisión, cuando arranque Facturación)

- **`IFiscalApiClient` se amplía** con operaciones de timbrado,
  cancelación, consulta de estatus y respuesta a solicitudes de
  cancelación. **No se introduce `IPacProvider`** — un solo puerto
  (ver ADR-0038).
- **CSD del SAT** por empresa: se guarda en Key Vault como `Certificate`
  (no en la BD). El módulo expone `ICsdProvider` que la inyecta cuando
  el caller (Facturación) construye y sella el XML antes de pasarlo al
  PAC. Manejo idéntico al que ADR-0027 §"CSD" describía — esa parte se
  preserva.
- Endpoints admin adicionales para subir CSD (`POST /admin/empresas/{id}/csd`,
  multipart con `.cer`/`.key` + password).
- Permisos canónicos adicionales (re-namespaceados desde ADR-0027):
  - `integraciones.fiscal.csd.gestionar`
  - `integraciones.fiscal.cfdi.timbrar`
  - `integraciones.fiscal.cfdi.cancelar.solicitar`
  - `integraciones.fiscal.cfdi.cancelar.aprobar`
  - `integraciones.fiscal.cfdi.consultar-estatus`

> **Fase 2 NO entra en este levantamiento de detalle** — se diseñará
> cuando arranque Facturación. Lo que importa hoy es que el módulo
> queda **arquitectónicamente listo** para hostearla.

### Fuera del módulo

- **Documentos / Blob storage**. Sigue en CxP hasta que el módulo de
  Documentos exista. El módulo de Documentos consumirá `IFiscalApiClient`
  cuando llegue.
- **Bancos**. El sub-namespace `Integraciones` aloja bancos en futuras
  fases (`Millet.Integraciones.Bancos`). Este módulo es solo fiscal/SAT.
- **Catálogos SAT** (formas de pago, usos CFDI, regímenes). Viven en
  `Compartido` como hoy.
- **Construcción del XML CFDI 4.0** — esa lógica pertenece al módulo
  Facturación (`ICfdiBuilder` cuando exista). Este módulo solo expone
  el client HTTP al PAC.
- **Tablas legales** (constancias de retención). Fuera de scope.

---

## 4. No-objetivos

- **NO** reemplaza al PAC. FiscalAPI sigue siendo un proveedor externo
  pago; el ERP es cliente HTTP.
- **NO** modela CFDIs (esos son del dominio de CxP y Facturación). Solo
  los descarga / valida.
- **NO** decide qué hacer con un CFDI descargado. Eso lo decide CxP vía
  `IngresarCfdiCommand`.
- **NO** hace polling síncrono al admin. El "test connection" es un
  endpoint dedicado.
- **NO** maneja secretos en plaintext en BD. Cifrado obligatorio.

---

## 5. Stakeholders

| Rol | Quién | Responsabilidad |
|---|---|---|
| Sponsor | Eduardo Paredes (owner ERP) | Aprueba alcance + decisiones |
| Owner técnico | Eduardo Paredes | Diseño + revisión PRs |
| Operador admin | Auxiliar CxP / DBA | Rota credenciales + da de alta empresas |
| Consumidor fase 1 | Módulo CxP | Cliente del puerto `IFiscalApiClient` |
| Consumidor fase 2 | Módulo Facturación (futuro) | Timbrado CFDI emitido |
| Consumidor fase 3 | Módulo Documentos (futuro) | Descarga XML/PDF al blob |
| Proveedor externo | FiscalAPI (PAC) | API REST timbrado/descarga/cancelación |

---

## 6. Casos de uso

### CU-1 — Alta inicial de empresa con FiscalAPI

1. Admin entra a `/admin/integraciones/fiscal`.
2. Selecciona empresa del dropdown.
3. Captura `BaseUrl` (default `https://api.fiscalapi.com`), `ApiKey`,
   timeouts.
4. Click "Test connection" → backend invoca un endpoint barato
   (`GET /health` o similar) con la `ApiKey` capturada (sin guardar).
5. Si responde 200 → habilita botón "Guardar". Si falla → muestra error
   del PAC.
6. Click "Guardar" → backend cifra `ApiKey` con DataProtection y
   persiste.
7. Admin agrega RFCs receptores (puede ser 1-N por empresa).
8. Worker `DescargaMasivaSatWorker` empieza a procesar en el próximo
   tick (default cada 6h, configurable).

### CU-2 — Rotación de ApiKey

1. PAC renovó el contrato y rotó la key.
2. Admin entra al editor, captura nueva key, "Test connection", Guardar.
3. La columna `ultima_rotacion_at` se actualiza. Logs/audit visibles.

### CU-3 — Desactivar empresa temporalmente

1. Admin pone `activo = false` en la configuración.
2. Worker omite esa empresa en cada tick (filtro `WHERE activo = true`).
3. Sin borrar nada — sigue auditable.

### CU-4 — CxP consume el puerto

Sin cambios funcionales desde el punto de vista de CxP: `IFiscalApiClient`
sigue siendo la misma interfaz. La diferencia es que la **implementación**
vive ahora en `Millet.Integraciones.Fiscal.Infrastructure` y resuelve la
config dinámicamente por empresa (usando `ICurrentEmpresaContext`).

---

## 7. Decisiones cerradas

| ID | Decisión | Confirmado por |
|---|---|---|
| D1 | Nombre del módulo: `Millet.Integraciones.Fiscal` | Eduardo, 2026-05-24 |
| D2 | Multi-empresa: **una configuración por empresa** (no global) | Eduardo, 2026-05-24 |
| D3 | Mailbox de correo (ingesta CFDI) se promueve a módulo separado `Millet.Integraciones.Mailbox` (no entra en este levantamiento). | Eduardo, 2026-05-24 |
| D4 | Cifrado de secretos: **ASP.NET DataProtection con DEK en Key Vault**. Ciphertext en columna Postgres normal; sin pgcrypto. | Eduardo, 2026-05-24 |
| D5 | Workers `IHostedService` dentro de `Millet.Api` (mismo patrón que `Integraciones.Aw`). No Container Apps Jobs. | Eduardo, 2026-05-24 |
| D6 | Endpoints versionados `/api/v1/integraciones/fiscal/...` (ADR-0021). | Convención |
| D7 | **FiscalAPI como PAC único** (descarga + timbrado + cancelación + consulta). OneFactura descartado. Una sola abstracción `IFiscalApiClient`. Ver [ADR-0038](../../decisiones/0038-fiscalapi-pac-unico.md). | Eduardo, 2026-05-24 |
| D8 | Rotación de credenciales — CSD cada 4 años (forzoso por SAT), FiscalAPI ApiKey anual (recomendada), DEK del DataProtection anual (recomendada NIST). Documentado en runbook §8. | Eduardo, 2026-05-24 |

---

## 8. Riesgos y mitigaciones

| Riesgo | Probabilidad | Impacto | Mitigación |
|---|---|---|---|
| Cutover desde CxP rompe descarga en producción | Media | Alto | Feature flag `Integraciones.Fiscal.Enabled`; si false, CxP usa el cliente viejo. PR de cutover es separado del PR de creación. |
| Credenciales en logs por debug accidental | Baja | Crítico | `[SensitiveData]` attribute custom + logging filter que masquea `api_key`. Tests específicos. |
| Pérdida del DEK rota cifrado de todos los secrets | Muy baja | Crítico | KV soft-delete + purge protection (ya configurado); doc de recovery en runbook §8. |
| Admin captura ApiKey en un campo que no es password | Baja | Alto | `<input type="password">` + autocomplete=off + no se renderiza en GET (siempre placeholder `••••` si ya está set). |
| Test connection consume cuota del PAC | Baja | Bajo | Rate limit en endpoint admin (max 10/min por usuario). |

---

## 9. Glosario

- **PAC** — Proveedor Autorizado de Certificación. Empresa con autorización
  SAT para timbrar CFDIs. FiscalAPI es uno.
- **Timbrar** — Firmar un CFDI emitido con el sello digital del PAC para
  que sea fiscalmente válido.
- **UUID CFDI** — Identificador único del CFDI asignado por el SAT al
  timbrar (NO el `Id` interno del `CfdiRecibido`).
- **DescargaMasiva** — Operación que solicita al PAC los CFDIs emitidos
  a un RFC receptor en una ventana de fechas.
- **RFC receptor** — RFC de Millet (o de cada razón social de Millet) al
  que están dirigidos los CFDIs descargados.
- **DEK** — Data Encryption Key. Llave maestra usada por ASP.NET
  DataProtection. Vive en Key Vault.

---

## 10. Anexo — Mapeo del estado actual (CxP) al nuevo módulo

| Componente actual (CxP) | Destino (Integraciones.Fiscal) | Notas del cutover |
|---|---|---|
| [`IFiscalApiClient.cs`](../../../backend/src/CuentasPorPagar/Domain/Fiscal/IFiscalApiClient.cs) | `Integraciones.Fiscal.Domain.IFiscalApiClient` | Mismo shape, namespace nuevo |
| [`FiscalApiHttpClient.cs`](../../../backend/src/CuentasPorPagar/Infrastructure/Fiscal/FiscalApiHttpClient.cs) | `Integraciones.Fiscal.Infrastructure.FiscalApiHttpClient` | Lee config de DB, no de `IOptions` |
| [`FiscalApiOptions.cs`](../../../backend/src/CuentasPorPagar/Infrastructure/Fiscal/FiscalApiOptions.cs) | Reemplazado por tabla `configuracion_pac` | Eliminado |
| [`NoOpFiscalApiClient.cs`](../../../backend/src/CuentasPorPagar/Infrastructure/Fiscal/NoOpFiscalApiClient.cs) | `Integraciones.Fiscal.Infrastructure.NoOpFiscalApiClient` | Activo cuando no hay config para la empresa |
| [`CfdiDescargaMasivaSatWorker.cs`](../../../backend/src/CuentasPorPagar/Infrastructure/Workers/CfdiDescargaMasivaSatWorker.cs) | `Integraciones.Fiscal.Infrastructure.Workers.DescargaMasivaSatWorker` | Itera por empresa activa |
| [`CfdiEstadoSatRefreshWorker.cs`](../../../backend/src/CuentasPorPagar/Infrastructure/Workers/CfdiEstadoSatRefreshWorker.cs) | `Integraciones.Fiscal.Infrastructure.Workers.EstadoSatRefreshWorker` | Igual |
| `WorkerSchedulingOptions.RfcsReceptores` | Tabla `rfcs_receptores` | Migration de seed |

El `IngresarCfdiCommand` y los handlers de CFDI **se quedan en CxP** — son
lógica de dominio CxP, no de integración.

---

## 11. Pendientes para el `01-diseno.md`

1. Shape exacto de `configuracion_pac` con tipos (¿varchar(N)? ¿text?).
2. Política de retry/circuit breaker — ¿se mantiene global o se configura
   por empresa?
3. Cómo el worker sabe qué configuracion procesar — query por `activo`
   ordenando por `prioridad` (¿es necesario?).
4. Si el admin UI muestra el ApiKey actual cifrada al editar (NO),
   solo permite "rotar".
5. Endpoint de "test connection" — ¿cobra al PAC? Revisar contrato de
   FiscalAPI antes de exponerlo.
6. ADR nuevo "ADR-0037 — DataProtection con DEK en Key Vault para
   credenciales operativas".
