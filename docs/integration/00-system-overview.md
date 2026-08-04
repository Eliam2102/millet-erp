# 00 — System Overview: Integración Glass Agent ↔ Millet ERP ↔ A+W

> **Doc espejo.** Mantener copia idéntica en ambos repos:
> - `Project_Millet_ERP/docs/integration/00-system-overview.md`
> - `glass-agent/docs/integration/00-system-overview.md`
>
> **Versión:** 1.0.0
> **Última actualización:** 2026-05-15
> **Source of truth:** repo `Project_Millet_ERP`. Cualquier cambio se hace primero ahí y se sincroniza al repo de Glass Agent.
> **Owner:** Eduardo Paredes

---

## 1. Propósito del documento

Este documento describe la arquitectura completa de la integración entre **tres sistemas** que pertenecen al mismo cliente (Millet Industria de Vidrio):

1. **Glass Agent** — chat conversacional asistido por IA para cotizaciones de vidrio (PHP, Cloudways).
2. **Millet ERP** — ERP back-office para administración, facturación, contable y fiscal (.NET 9, Azure).
3. **A+W** — ERP industrial específico de vidrio, residente on-premise (SQL Server en `SER-DATA`).

El documento ancla los otros 4 docs de integración:
- `01-api-contract.md` — endpoints, schemas, errores
- `02-edi-correlation.md` — flujo asíncrono y polling
- `03-deployment.md` — Bicep, secrets, on-prem
- `04-levantamiento.md` — diseño formal del módulo .NET (en `docs/modulos/integraciones-aw/`)

Cualquier discrepancia entre este overview y la implementación se resuelve **actualizando primero este documento**, no el código.

---

## 2. Contexto de negocio

A+W es el sistema operativo y de producción de Millet — **se queda permanentemente**. No se reemplaza. Maneja catálogo de vidrios, cotizaciones formales, órdenes de producción, cortes, plantas, BOMs.

Millet ERP es un sistema nuevo que cubre lo que A+W **no hace**: facturación CFDI, cuentas por cobrar/pagar, contabilidad, activos fijos, reportes financieros. Reemplaza progresivamente esas áreas que hoy viven en SAP (Strangler Fig, ver CLAUDE.md del repo ERP).

Glass Agent es un chat asistido por LLM que ayuda al vendedor a armar cotizaciones en lenguaje natural. Hoy genera archivos EDI que el vendedor carga manualmente en A+W. El objetivo de esta integración es **automatizar ese último tramo**: que el archivo EDI llegue solo a A+W y que el vendedor reciba confirmación con el folio asignado por A+W.

A medida que el ERP nuevo crezca, **otros módulos** (Facturación, Inventario, Cuentas por Cobrar) también van a necesitar leer/escribir contra A+W. Por eso esta integración se diseña como **adaptador general A+W**, no como puente puntual para Glass Agent.

---

## 3. Decisiones tomadas

Las 5 decisiones estratégicas (cerradas el 2026-05-15) que rigen el diseño:

| # | Decisión | Resultado | Implicación |
|---|---|---|---|
| D1 | **Alcance** | Adaptador A+W general | Endpoints por dominio A+W (cotizaciones, pedidos, clientes, articulos, inventario), no por consumidor. Modelo de datos genérico discriminado por `tipo_entidad`. |
| D2 | **Estrategia** | Módulo completo, espera Hybrid Connection | Stubs `PLATFORM-TODO` minimizados. Código no se mergea a `main` del ERP hasta tener el túnel funcionando con Millet. Diseño + Bicep + on-prem prep avanzan en paralelo. |
| D3 | **Auth** | Service principal Entra ID | Glass Agent (y futuros sistemas M2M) usan client credentials flow. Requiere agregar concepto de "usuario de servicio" en `Identidad`. No se agrega Auth.Mode adicional. |
| D4 | **Hosting de workers** | `HostedService` dentro de `Millet.Api` | Sigue patrón de `OutboxPublisherWorker` existente. Cero infraestructura nueva. App Service Plan B1/P1v3 lo soporta. |
| D5 | **Nombre del módulo** | `Millet.Integraciones.Aw` | Sub-namespace `Integraciones` reservado para integraciones futuras (SAT, bancos, etc.). |

ADRs del ERP que ancla esta integración (se respetan, no se inventan nuevas convenciones):

- **ADR-0001** — Azure SignalR (notificación al frontend cuando llega correlación)
- **ADR-0003** — Microsoft Entra ID (auth — extender a service principals)
- **ADR-0007** — RBAC granular con `PermisosCanonicos`
- **ADR-0009** — Outbox pattern para eventos de integración
- **ADR-0010** — Problem Details RFC 7807
- **ADR-0012** — ETag/If-Match para concurrencia
- **ADR-0020** — Idempotency-Key HTTP
- **ADR-0021** — Versionado API por path `/api/v1/`
- **ADR-0030** — Multi-DbContext por módulo
- **ADR-0031** — Convención `PLATFORM-TODO` para stubs temporales

Si esta integración necesita una decisión que no encaja en ADRs existentes, se crea **ADR-0036+** en `docs/decisiones/`. Hoy no se anticipa ninguna.

---

## 4. Componentes del sistema

Inventario completo. Cada componente se marca como:
- **EXISTE** — ya está en producción/repo, no se toca o se toca mínimo.
- **NUEVO** — se construye en esta iteración.
- **MODIFICADO** — existe pero requiere cambios.

### 4.1 Glass Agent (lado consumidor)

| Componente | Estado | Tecnología | Ubicación |
|---|---|---|---|
| Chat conversacional (UI) | EXISTE | Vanilla JS | `glass-agent/js/chat.js` |
| `glass_agent_chat()` + tool dispatcher | EXISTE | PHP | `glass-agent/include/functions_agent.php` |
| `AWEdiGenerator` (genera contenido EDI) | EXISTE | PHP | `glass-agent/include/functions_edi.php` |
| Tabla `glass_orders` con estado del pedido | EXISTE → MODIFICADA | MySQL | migración 015 agrega campos `erp_quote_ref`, `aw_doc_id`, `erp_submitted_at`, `aw_correlated_at`, `erp_last_error`, `erp_retry_count` |
| Cliente HTTP al ERP | NUEVO | PHP | `glass-agent/include/functions_erp.php` (~150 líneas) |
| Adquisición de token Entra (client credentials) | NUEVO | PHP | dentro de `functions_erp.php`, con cache de token en MySQL/Redis |
| Worker de reintento (cola de salida) | NUEVO | PHP CLI | reintenta envíos fallidos por backoff exponencial |
| Tabla `glass_edi_outbox` | NUEVO | MySQL | cola local para reintentos |

### 4.2 Millet ERP (lado adaptador A+W)

| Componente | Estado | Tecnología | Ubicación |
|---|---|---|---|
| Endpoints `/api/v1/integraciones/aw/*` | NUEVO | .NET 9 Minimal API | `backend/src/Api/Endpoints/Integraciones/Aw/` |
| Módulo `Millet.Integraciones.Aw` (Domain + Application + Infrastructure) | NUEVO | C# | `backend/src/Integraciones.Aw/` |
| `IntegracionesAwDbContext` | NUEVO | EF Core 9 + Npgsql | esquema `integraciones_aw` |
| Migraciones EF Core | NUEVO | EF Core | `backend/src/Integraciones.Aw/Infrastructure/Persistence/Migrations/Aw/` |
| `AwDropWorker` (consume Service Bus, drop a on-prem) | NUEVO | C# HostedService | dentro de `Millet.Api` |
| `AwCorrelationWorker` (polling SQL Server cada 30s) | NUEVO | C# HostedService | dentro de `Millet.Api` |
| Concepto "usuario de servicio" | NUEVO | C# | en `Millet.Identidad` (flag `EsServicePrincipal` o entidad separada) |
| `EntraTokenValidator` extendido para `appid` | MODIFICADO | C# | `backend/src/Api/Auth/EntraTokenValidator.cs` |
| `PermisosCanonicos` con nuevos permisos | MODIFICADO | C# | `backend/src/Identidad/Domain/PermisosCanonicos.cs` |
| Outbox publisher (eventos a Service Bus) | EXISTE | C# | `OutboxPublisherWorker<IntegracionesAwDbContext>` en SharedKernel |
| `ComprasHub` SignalR → renombrar/extender o crear hub propio | MODIFICADO o NUEVO | C# | a decidir en doc 01: ¿hub compartido o `IntegracionesAwHub`? |

### 4.3 Infraestructura Azure (compartida con resto del ERP)

| Componente | Estado | Tipo de recurso | Nombre (dev) |
|---|---|---|---|
| App Service | EXISTE | Web App Linux .NET 9 | `app-millet-dev-mxc-01` |
| App Service Plan | EXISTE | B1 dev / P1v3 prod | `plan-millet-dev-mxc-01` |
| Service Bus Namespace | EXISTE (Standard) | Service Bus | `sb-millet-dev-mxc-01` |
| Topic `integraciones-aw-events` | NUEVO | Service Bus topic | dentro del namespace existente |
| Subscriptions (drop, correlation) | NUEVO | Service Bus subscriptions | una por worker |
| Key Vault | EXISTE | Key Vault | `kv-millet-dev-mxc-01` |
| Secret `aw-sql-connection-string` | NUEVO | secret en KV | apunta a SQL Server AWBUSINESS vía Hybrid Connection |
| Secret `glass-agent-spn-client-secret` | NUEVO | secret en KV | client secret del service principal del Glass Agent |
| Secret `aw-drop-service-api-key` | NUEVO | secret en KV | API key para autenticar Millet.Api ↔ drop service on-prem |
| Application Insights | EXISTE | App Insights | `appi-millet-dev-mxc-01` |
| Azure SignalR | EXISTE | SignalR Standard | `sigr-millet-dev-mxc-01` (en `southcentralus`) |
| PostgreSQL Flexible Server | EXISTE | Postgres 16 | `psql-millet-dev-mxc-01` (esquema `integraciones_aw` se agrega ahí) |
| **Azure Relay Namespace** | **NUEVO** | Relay | `relay-millet-dev-mxc-01` |
| **Hybrid Connection `hc-aw-business-sql`** | **NUEVO** | Hybrid Connection | target `SER-DATA:1433` |
| **Hybrid Connection `hc-aw-drop-service`** | **NUEVO** | Hybrid Connection | target `SER-DATA:5000` |
| App Registration "Glass Agent SPN" | NUEVO | Entra ID | client_id se almacena en config.php del Glass Agent |

### 4.4 On-premise (Millet, servidor `SER-DATA`)

| Componente | Estado | Tecnología | Notas |
|---|---|---|---|
| Servidor `SER-DATA` | EXISTE | Windows Server 2012 R2 Standard | ⚠️ Sistema operativo fuera de soporte Microsoft. Documentado como deuda técnica. |
| SQL Server instancia `AWBUSINESS` | EXISTE → MODIFICADO | SQL Server (Mixed Mode) | Hoy en puerto dinámico `49900`. **Se cambia a puerto estático `1433`** (acción coordinada con downtime). |
| SQL Server instancias `AWPRODUCTION`, `AWSOA` | EXISTE | SQL Server | Fuera de scope inicial. Pueden integrarse en fases futuras (otra Hybrid Connection cada una). |
| **HCM (Hybrid Connection Manager)** | INSTALADO 2026-05-15 | Servicio Windows | Listo, sin conexiones configuradas todavía. Espera Hybrid Connections en Azure. |
| **Drop service** | NUEVO | .NET 8 Minimal API | Servicio Windows ~150 líneas. Escucha en `localhost:5000`. Recibe EDI vía HTTP, valida API key, escribe a carpeta de import A+W. |
| Carpeta de import A+W | EXISTE | Filesystem | <!-- TODO: ruta exacta. Pendiente confirmar con equipo A+W de Millet --> |
| SQL login para integración | TBD | SQL login | <!-- TODO: decidir entre reusar `awserv` o crear `millet_erp_reader` con permisos read-only quirúrgicos --> |
| Customización A+W para escribir referencia externa post-import | EXISTE → MODIFICADO | A+W / AlcimBasic | Cuando A+W procesa el EDI debe persistir el `quote_reference` que viene en record FH en algún campo de `pool_auftrag` <!-- TODO: confirmar campo, típicamente `auftragsnummer_kunde` o `referenz_extern` --> |

---

## 5. Arquitectura — diagrama general

```
                                CLOUDWAYS                                  AZURE                              ON-PREMISE (SER-DATA)
┌─────────────────────────────────────────────┐  ┌──────────────────────────────────────────────────────┐  ┌────────────────────────────────────┐
│                                             │  │                                                      │  │                                    │
│   ┌─────────────────────────────┐           │  │   ┌──────────────────────────────────────────┐       │  │                                    │
│   │  Glass Agent UI (chat.js)   │           │  │   │  Millet.Api (.NET 9, App Service)        │       │  │                                    │
│   └─────────────────────────────┘           │  │   │                                          │       │  │                                    │
│              │                              │  │   │  ┌────────────────────────────────────┐  │       │  │                                    │
│              ▼                              │  │   │  │ Endpoint POST                      │  │       │  │                                    │
│   ┌─────────────────────────────┐           │  │   │  │ /api/v1/integraciones/aw/          │  │       │  │                                    │
│   │  ajax.php                   │           │  │   │  │   cotizaciones                     │  │       │  │                                    │
│   │  (PHP, Anthropic API,       │           │  │   │  │  - Valida Entra Bearer + appid     │  │       │  │                                    │
│   │   18 tools, EDI gen)        │           │  │   │  │  - Valida Idempotency-Key          │  │       │  │                                    │
│   └─────────────────────────────┘           │  │   │  │  - Persiste + emite Outbox event   │  │       │  │                                    │
│              │                              │  │   │  │  - Retorna 202 + quote_ref + ETag  │  │       │  │                                    │
│              │  glass_erp_submit_edi()      │  │   │  └────────────────┬───────────────────┘  │       │  │                                    │
│              │  (functions_erp.php)         │  │   │                   │                      │       │  │                                    │
│              │                              │  │   │                   ▼                      │       │  │                                    │
│              │  1. Obtiene token Entra      │  │   │  ┌────────────────────────────────────┐  │       │  │                                    │
│              │  2. POST con Bearer +        │  │   │  │  integraciones_aw.entidad_externa  │  │       │  │                                    │
│              │     Idempotency-Key          │  │   │  │  integraciones_aw.envio            │  │       │  │                                    │
│              │                              │  │   │  │  integraciones_aw.outbox           │  │       │  │                                    │
│              ▼                              │  │   │  └────────────────┬───────────────────┘  │       │  │                                    │
│         HTTPS ─────────────────────────────────────────┐                │  (en Postgres)         │       │  │                                    │
│                                             │  │      │                ▼                      │       │  │                                    │
│              ▲ (callback de estado)         │  │      │ ┌──────────────────────────────────┐  │       │  │                                    │
│              │ (vía SignalR o polling)      │  │      │ │ OutboxPublisherWorker            │  │       │  │                                    │
│              │                              │  │      │ │ (HostedService, ya existe)       │  │       │  │                                    │
│   ┌─────────────────────────────┐           │  │      │ └──────────────┬───────────────────┘  │       │  │                                    │
│   │  MySQL local (BD millet)    │           │  │      │                ▼                      │       │  │                                    │
│   │  - glass_orders (MOD)       │           │  │      │ ┌──────────────────────────────────┐  │       │  │                                    │
│   │  - glass_edi_outbox (NEW)   │           │  │      │ │ Service Bus Topic                │  │       │  │                                    │
│   │  - glass_chat_log           │           │  │      │ │  integraciones-aw-events         │  │       │  │                                    │
│   │  - glass_orders.aw_doc_id   │           │  │      │ │                                  │  │       │  │                                    │
│   └─────────────────────────────┘           │  │      │ │  Subscriptions:                  │  │       │  │                                    │
│                                             │  │      │ │   - drop-subscription            │  │       │  │                                    │
└─────────────────────────────────────────────┘  │      │ │   - correlation-trigger          │  │       │  │                                    │
                                                 │      │ └──────────────┬───────────────────┘  │       │  │                                    │
                                                 │      │                ▼                      │       │  │                                    │
                                                 │      │ ┌──────────────────────────────────┐  │       │  │                                    │
                                                 │      │ │ AwDropWorker (HostedService)     │  │       │  │                                    │
                                                 │      │ │ - Consume drop-subscription      │  │       │  │                                    │
                                                 │      │ │ - HTTP POST al drop service      │  │       │  │                                    │
                                                 │      │ │   vía Hybrid Connection          │  │       │  │                                    │
                                                 │      │ └──────────────┬───────────────────┘  │       │  │                                    │
                                                 │      │                │                      │       │  │                                    │
                                                 │      │                ▼                      │       │  │  ┌──────────────────────────────┐  │
                                                 │      │ ┌─────────────────────────────────┐   │       │  │  │ HCM (Microsoft Hybrid        │  │
                                                 │      │ │ Azure Relay namespace           │   │       │  │  │   Connection Manager)        │  │
                                                 │      │ │  relay-millet-{env}-mxc-01      │   │       │  │  │ Servicio Windows             │  │
                                                 │      │ │                                 │   │       │  │  │ Conexión HTTPS saliente      │  │
                                                 │      │ │ Hybrid Connections:             │◄──┼──────────┼──┤  hacia Azure Relay            │  │
                                                 │      │ │  - hc-aw-drop-service           │   │       │  │  │ (puerto 443)                 │  │
                                                 │      │ │      → SER-DATA:5000            │   │       │  │  └──────────┬───────────────────┘  │
                                                 │      │ │  - hc-aw-business-sql           │   │       │  │             │                      │
                                                 │      │ │      → SER-DATA:1433            │   │       │  │             ▼                      │
                                                 │      │ └─────────────────────────────────┘   │       │  │  ┌──────────────────────────────┐  │
                                                 │      │                                       │       │  │  │ Drop service                 │  │
                                                 │      │ ┌─────────────────────────────────┐   │       │  │  │ (.NET 8 Windows Service)     │  │
                                                 │      │ │ AwCorrelationWorker             │   │       │  │  │ localhost:5000               │  │
                                                 │      │ │  (HostedService, polling 30s)   │   │       │  │  │ - Recibe POST /drop-edi      │  │
                                                 │      │ │ - Query SQL AWBUSINESS:         │   │       │  │  │ - Valida X-API-Key            │  │
                                                 │      │ │   pool_auftrag                  ├───┼───────┼──┼──┼─→ Escribe a carpeta A+W      │  │
                                                 │      │ │   WHERE auftragsnummer_kunde    │   │       │  │  └──────────┬───────────────────┘  │
                                                 │      │ │     IN (<pendientes>)           │   │       │  │             │                      │
                                                 │      │ │ - Si match → publish event      │   │       │  │             ▼                      │
                                                 │      │ │   PedidoAwCorrelacionado        │   │       │  │  ┌──────────────────────────────┐  │
                                                 │      │ └─────────────────┬───────────────┘   │       │  │  │ Carpeta de import A+W        │  │
                                                 │      │                   │                   │       │  │  │ <TODO: ruta>                 │  │
                                                 │      │                   │                   │       │  │  └──────────┬───────────────────┘  │
                                                 │      │                   ▼                   │       │  │             │                      │
                                                 │      │ ┌─────────────────────────────────┐   │       │  │             │ batch cada 2 min     │
                                                 │      │ │ ComprasHub SignalR              │   │       │  │             ▼                      │
                                                 │      │ │ (o IntegracionesAwHub nuevo)    │   │       │  │  ┌──────────────────────────────┐  │
                                                 │      │ │ Notifica al frontend / agent    │───┼───┐   │  │  │ A+W ERP                      │  │
                                                 │      │ └─────────────────────────────────┘   │   │   │  │  │ - Procesa EDI                │  │
                                                 │      │                                       │   │   │  │  │ - Crea pedido en pool_auftrag│  │
                                                 │      │ ┌─────────────────────────────────┐   │   │   │  │  │ - Customización escribe      │  │
                                                 │      │ │ Application Insights            │   │   │   │  │  │   quote_ref en               │  │
                                                 │      │ │ (traces, logs, métricas)        │   │   │   │  │  │   auftragsnummer_kunde       │  │
                                                 │      │ └─────────────────────────────────┘   │   │   │  │  └──────────┬───────────────────┘  │
                                                 │      │                                       │   │   │  │             │                      │
                                                 │      └───────────────────────────────────────┘   │   │  │             ▼                      │
                                                 │                                                  │   │  │  ┌──────────────────────────────┐  │
                                                 │                                                  │   │  │  │ SQL Server AWBUSINESS        │  │
                                                 │                                                  │   │  │  │ Puerto 1433 (estático)       │  │
                                                 │                                                  │   │  │  │ - pool_auftrag               │  │
                                                 └──────────────────────────────────────────────────┘   │  │  │ - pool_pos                   │  │
                                                                                                        │  │  │ - pool_kunde                 │  │
                                                                            (HCM tunnel)                │  │  └──────────────────────────────┘  │
                                                                                                        │  │                                    │
                                                                                                        │  └────────────────────────────────────┘
                                                                                                        │
                                                                                                        │
                                                                                                        └─── (eventualmente: WebSocket SignalR
                                                                                                              hacia Glass Agent o el frontend
                                                                                                              que muestra el chat)
```

### 5.1 Lectura del diagrama

- **Flecha gruesa** (HTTPS al ERP, Hybrid Connection, polling) = comunicación síncrona en tiempo real.
- **Flecha por Service Bus** = comunicación asíncrona desacoplada.
- **HCM hace UNA conexión saliente** (puerto 443) que multiplexa todas las Hybrid Connections.

### 5.2 Por qué Service Bus en medio

Sin Service Bus, el endpoint `POST /cotizaciones` tendría que esperar a que Hybrid Connection responda, el drop service escriba, A+W procese (batch 2 min), y el polling confirme. Eso son 1-3 minutos de espera HTTP — inaceptable.

Con Service Bus:
1. El endpoint responde en <100ms (solo persiste y emite evento).
2. Si el worker se cae, los eventos se acumulan y procesan al volver.
3. Si el drop service no responde, Service Bus reintenta automáticamente con backoff.
4. Si una correlación falla, el evento va a dead-letter queue para revisión manual.

Esto **no es over-engineering**: el patrón ya existe en el ERP (`OutboxPublisherWorker<ComprasDbContext>` publica a `sb-millet-dev-mxc-01`). Solo se replica para el nuevo módulo.

---

## 6. Responsabilidades por componente

### 6.1 Glass Agent (PHP)

**Hace:**
- Conversación natural con vendedor.
- Function calling con 18 tools (catálogo, BOM, restricciones, pricing, etc.).
- Generación del archivo EDI vía `AWEdiGenerator` (queda intacto, no se porta a C#).
- Persistencia local del archivo en `orders/` (auditoría).
- POST del contenido EDI al ERP nuevo con Bearer token + Idempotency-Key.
- Manejo de reintentos vía `glass_edi_outbox`.
- Mostrar al vendedor el estado: "Enviado, esperando confirmación A+W (~2 min)" → "Confirmado, A+W #12345".

**NO hace:**
- ❌ Conexión directa a SQL Server, SMB, ni servidor on-prem.
- ❌ Lógica de A+W (formato EDI lo sigue generando, pero su entrega es responsabilidad del ERP).
- ❌ Persistencia de pedidos como source of truth (eso es del ERP).
- ❌ Cualquier consulta directa a `pool_*` o tablas A+W.

### 6.2 Millet ERP — Módulo `Millet.Integraciones.Aw`

**Hace:**
- Expone endpoints REST para todos los dominios A+W (cotizaciones, pedidos, clientes, articulos, inventario).
- Valida Bearer tokens de service principals via Entra ID.
- Persiste cada operación en esquema `integraciones_aw`.
- Emite Outbox events que se publican a Service Bus.
- Hospeda `AwDropWorker` y `AwCorrelationWorker` como `HostedService`.
- Mantiene el mapeo de correlación entre IDs internos del ERP y IDs de A+W.
- Notifica vía SignalR cuando hay correlación nueva.

**NO hace:**
- ❌ Lógica de negocio del Glass Agent (sigue siendo PHP).
- ❌ Generación del EDI (recibe contenido ya generado por el Glass Agent en fase 1).
- ❌ Comunicación directa con A+W sin pasar por Hybrid Connection.

### 6.3 Drop service on-prem (.NET 8 Windows Service)

**Hace:**
- Escucha en `localhost:5000`.
- Recibe `POST /drop-edi` con archivo en body + header `X-Filename` + header `X-API-Key`.
- Valida API key.
- Escribe archivo a carpeta de import de A+W (`File.WriteAllBytesAsync`).
- Devuelve 200 con confirmación de bytes escritos.
- Logging local (Windows Event Log + archivo).

**NO hace:**
- ❌ Acceso a internet, ningún tipo de tráfico saliente.
- ❌ Modificar el contenido del archivo.
- ❌ Llamar a A+W o SQL Server.
- ❌ Cualquier otra cosa que no sea recibir bytes y escribirlos.

### 6.4 HCM (Hybrid Connection Manager)

**Hace:**
- Mantiene conexión HTTPS saliente al Azure Relay namespace.
- Multiplex todas las Hybrid Connections configuradas.
- Forwards TCP entrante (desde Azure) a `localhost:<puerto>` correspondiente.

**NO hace:**
- ❌ Cualquier lógica de aplicación.
- ❌ Autenticación de aplicación (eso lo hace cada Hybrid Connection con su key).

### 6.5 A+W (sin cambios mayores)

**Hace:**
- Procesa archivos EDI de la carpeta de import (batch cada 2 min).
- Crea pedidos en `pool_auftrag`.
- **Customización agregada**: persistir el `quote_reference` recibido en el record FH del EDI en algún campo consultable de `pool_auftrag` (típicamente `auftragsnummer_kunde`).

**NO hace:**
- ❌ Llamar al ERP (no es push). El ERP es quien consulta a A+W (pull).

---

## 7. Flujos críticos

### 7.1 Submit de cotización (happy path)

```
T+0.0s    Vendedor envía cotización final en chat
T+0.1s    Glass Agent genera EDI con AWEdiGenerator
T+0.2s    Glass Agent solicita token Entra (cache hit, 0 latencia)
T+0.3s    POST https://app-millet-prod-mxc-01.azurewebsites.net/api/v1/integraciones/aw/cotizaciones
            Authorization: Bearer <entra_token>
            Idempotency-Key: <uuid-v4>
            Body: { quote_reference, edi_content, customer_tax_id, metadata }
T+0.4s    Millet.Api valida token, idempotency, body
T+0.5s    Persiste en integraciones_aw.entidad_externa + outbox
T+0.5s    Retorna 202 Accepted { quote_reference, status: "submitted", aw_doc_id: null }
T+0.6s    Glass Agent actualiza glass_orders.status="submitted_to_erp"
T+0.6s    Glass Agent muestra al vendedor: "Enviado a A+W, esperando confirmación..."

T+1.0s    OutboxPublisherWorker recoge el evento y publica a Service Bus
T+1.5s    AwDropWorker consume el evento desde drop-subscription
T+1.6s    POST a https://relay-millet-prod-mxc-01.servicebus.windows.net/hc-aw-drop-service
            X-API-Key: <key>
            X-Filename: cot_Q-2026-451.edi
            Body: <edi content>
T+1.7s    Relay forwarding via HCM → drop service localhost:5000
T+1.8s    Drop service escribe a <ruta A+W>/cot_Q-2026-451.edi
T+1.9s    AwDropWorker marca envio.status = "delivered" en BD

T+2-120s  A+W batch corre cada 2 min. Eventualmente procesa el archivo.
          Crea pool_auftrag con auftragsnummer=12345 y auftragsnummer_kunde="Q-2026-451"

T+30s,60s,90s,...   AwCorrelationWorker hace polling cada 30s:
                    SELECT auftragsnummer FROM pool_auftrag
                    WHERE auftragsnummer_kunde IN (<pendientes>)

T+(~60-90s post-batch)  Worker encuentra match → actualiza correlacion_aw
                        → publica PedidoAwCorrelacionado event al Service Bus

T+(~60-90s post-batch)  ComprasHub SignalR (o nuevo IntegracionesAwHub) recibe el event
                        → emite a Glass Agent / frontend
                        → Glass Agent muestra: "A+W confirmó: pedido #12345"

(Si Glass Agent no tiene canal SignalR, hace polling cada 5s a
 GET /api/v1/integraciones/aw/cotizaciones/{quote_ref}
 que devuelve aw_doc_id cuando ya está correlacionado.)
```

### 7.2 Reintento por fallo del drop service

```
AwDropWorker recibe evento → POST a drop service → TIMEOUT o 5xx
  → marca envio.status = "drop_failed"
  → incrementa retry_count
  → message vuelve a Service Bus con delay (10s, 30s, 2min, 10min)
  → max 5 reintentos
  → si todos fallan → dead-letter queue → alerta a operador
```

### 7.3 Reintento por correlación expirada

```
AwCorrelationWorker corre cada 30s:
  → busca registros con submitted_at > NOW - 15min y aw_doc_id IS NULL
  → si no encuentra match en SQL → no hace nada, vuelve a intentar siguiente ciclo
  → si lleva más de 15 min sin match → marca como "failed_correlation"
    → publica evento CorrelacionAwExpirada
    → alerta a operador (correo + dashboard)
    → Glass Agent recibe notificación: "Cotización no se procesó en A+W. Equipo técnico revisando."
```

### 7.4 Fallo de token Entra

```
Glass Agent intenta refrescar token (expired o primer call):
  → POST https://login.microsoftonline.com/<tenant>/oauth2/v2.0/token (client_credentials)
  → si error de red → reintenta 3 veces con backoff
  → si error de credenciales (401, invalid_client) → no reintenta, alerta inmediata
  → si error de scope → log + alerta
```

### 7.5 Endpoint idempotency

```
Glass Agent envía submit con Idempotency-Key = K1
  → Millet.Api ya tiene una operación con K1 en cache → devuelve respuesta original (200 o 202)
  → si K1 está en flight (procesando) → devuelve 409 Conflict
  → si K1 nuevo → procesa, guarda respuesta asociada a K1 con TTL 24h
Garantiza que reintentos del Glass Agent no creen pedidos duplicados.
```

---

## 8. Decisiones de plataforma alineadas a ADRs existentes

Esta integración NO inventa convenciones nuevas. Sigue lo que ya está estándar en el ERP:

| Aspecto | ADR | Cómo se aplica aquí |
|---|---|---|
| Eventos hacia el exterior | ADR-0009 (Outbox) | `integraciones_aw.outbox` se publica a Service Bus por `OutboxPublisherWorker<IntegracionesAwDbContext>` (instanciación nueva del worker existente) |
| Idempotency | ADR-0020 | `[RequireIdempotencyKey]` en `POST /cotizaciones`. Middleware existente. |
| Concurrencia optimista | ADR-0012 | `GET /cotizaciones/{ref}` devuelve ETag. `PATCH` (cuando exista) exige If-Match. |
| Errores HTTP | ADR-0010 | `IExceptionHandler` global. Problem Details RFC 7807. Códigos `validation_failed`, `correlation_expired`, `aw_unreachable`. |
| Versionado | ADR-0021 | `/api/v1/integraciones/aw/...`. Cambios breaking → `/api/v2/...`. |
| Autorización | ADR-0007 | Permisos nuevos en `PermisosCanonicos`: `integraciones.aw.cotizaciones.crear`, `.consultar`, `.administrar`, etc. |
| Multi-DbContext | ADR-0030 | `IntegracionesAwDbContext` con esquema propio `integraciones_aw`. Migraciones independientes. |
| Stubs temporales | ADR-0031 | Minimizados (decisión D2). Solo se contemplan donde hay dependencia real on-prem y la pieza on-prem aún no está lista. |

---

## 9. Fronteras y anti-scope

Reglas duras de qué va dónde, para evitar drift entre proyectos:

| Layer | NUNCA hace | SIEMPRE hace |
|---|---|---|
| Glass Agent PHP | Conexión SQL/SMB a on-prem. Persistir pedidos como source of truth. Lógica de auth Entra a mano. | Generar EDI, llamar al ERP, mostrar al vendedor el estado. |
| Millet.Api endpoint | Lógica de glass-specific quoting. Lógica de SAT/CFDI (eso es otro módulo). Llamar HTTP al on-prem directo (debe ser via worker + Service Bus). | Validar contratos, persistir, emitir eventos. |
| AwDropWorker | Lógica de negocio. Decidir si una cotización es válida. | Drop confiable con reintentos. |
| AwCorrelationWorker | Modificar A+W. Escribir a `pool_*`. | Read-only sobre A+W, correlación, publicación de eventos. |
| Drop service on-prem | Cualquier acceso a internet. Lógica de A+W. Validación de contenido EDI. | Recibir bytes, validar API key, escribir archivo, devolver OK. |
| HCM | Cualquier cosa que no sea tunelar TCP. | Tunelar TCP. |

---

## 10. PLATFORM-TODOs esperados (deuda controlada)

A pesar de la decisión "Módulo completo" (D2), hay algunas piezas que **inevitablemente** son stubs temporales mientras se coordinan dependencias humanas. Cada uno se marca con `// PLATFORM-TODO(<id>): ...` en el código y se documenta aquí:

| ID | Componente | Estado inicial | Cierre |
|---|---|---|---|
| `<EntraIdResolverServicio>` | `IEntraIdResolverPort.ResolveServicePrincipalAsync` | Devuelve `UsuarioServicio` virtual hardcoded para el `appid` de Glass Agent | Cuando se integre Microsoft Graph para SPs |
| `<AwImportFolderPath>` | Drop service config | Path placeholder `C:\AW\Import\` | Cuando Millet confirme ruta real |
| `<AwCorrelationField>` | `AwCorrelationWorker` query | Asume `auftragsnummer_kunde`. | Cuando se confirme con A+W de Millet |
| `<AwCustomizationCallback>` | Customización A+W | No existe — se asume que A+W persiste el `quote_reference` post-import | Cuando se desarrolle la modificación A+W |

Cada uno tiene un ticket o issue asociado. Ninguno bloquea el desarrollo del módulo `Millet.Integraciones.Aw`, pero todos bloquean la primera puesta en producción.

---

## 11. Glosario

| Término | Definición |
|---|---|
| **A+W** | ERP industrial específico para fabricantes de vidrio. Provider: A+W Software GmbH. Reside on-premise en SQL Server. |
| **AWBUSINESS** | Instancia SQL Server que aloja la BD comercial de A+W (clientes, cotizaciones, pedidos cabecera). |
| **AWPRODUCTION** | Instancia SQL Server para datos de producción (cortes, materiales). Fuera de scope inicial. |
| **AWSOA** | Instancia SQL Server para SOA/integración. Fuera de scope inicial. |
| **EDI** | Electronic Data Interchange. En este contexto, el formato de archivo .edi específico de A+W (records de 512 chars FH/K1/K2/P1/S1/B1/T1). |
| **Hybrid Connection** | Servicio de Azure (parte de Azure Relay) que permite a apps de Azure App Service alcanzar recursos on-prem sin abrir puertos del firewall del cliente. |
| **HCM** | Hybrid Connection Manager. Servicio Windows de Microsoft que corre on-prem y sostiene el túnel HTTPS hacia Azure Relay. |
| **doc_id (A+W)** | `auftragsnummer` en `pool_auftrag` — identificador interno del pedido en A+W. |
| **quote_reference** | Identificador del lado Millet ERP que se inyecta en el EDI (record FH) para correlación. Formato `Q-YYYY-NNNN`. |
| **Service principal (Entra ID)** | App Registration en Entra ID que representa una aplicación (no un humano). Usa client credentials flow. |
| **Outbox pattern** | Patrón para emitir eventos hacia un message broker garantizando consistencia con la transacción de BD. Implementado en SharedKernel. |
| **PLATFORM-TODO** | Convención del repo ERP (ADR-0031) para marcar stubs temporales en código. Buscables con `rg "PLATFORM-TODO" backend/src`. |
| **Drop service** | Mini-servicio C# que vamos a instalar on-prem (~150 líneas). Recibe EDI vía HTTP y lo escribe a carpeta A+W. |
| **SER-DATA** | Hostname del servidor on-prem de Millet donde residen las instancias SQL Server A+W, el HCM, y donde se instalará el drop service. |

---

## 12. Cross-references

- **`01-api-contract.md`** — schema completo de cada endpoint, ejemplos request/response, códigos de error.
- **`02-edi-correlation.md`** — detalle del polling, eventos Service Bus, lógica de retry y dead-letter, cómo se reconcilia un timeout.
- **`03-deployment.md`** — cambios al Bicep, secrets nuevos en Key Vault, instalación del drop service, configuración del HCM, scripts de migración.
- **`docs/modulos/integraciones-aw/00-levantamiento.md`** (solo en repo ERP) — diseño formal del módulo siguiendo la convención de los demás módulos del back-office.
- **CLAUDE.md de Glass Agent** — actualizado para incluir las reglas de no-cruzar fronteras hacia A+W.
- **CLAUDE.md de Millet ERP** — actualizado para incluir `Millet.Integraciones.Aw` en la lista de módulos.

---

## CHANGELOG

### v1.0.0 — 2026-05-15
- Versión inicial.
- Captura las 5 decisiones tomadas (D1-D5).
- Incorpora hallazgos de on-prem: server `SER-DATA`, instancia `AWBUSINESS`, Windows Server 2012 R2, sin proxy.
- HCM instalado on-prem (paso completado).
- Pendientes marcados con `<!-- TODO -->`: ruta carpeta A+W, decisión SQL login, confirmación de campo de correlación.
