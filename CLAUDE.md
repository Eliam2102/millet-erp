# CLAUDE.md — Contexto para Claude Code

Este archivo orienta a Claude Code en futuras sesiones sobre este monorepo.
Léelo completo antes de proponer cambios significativos.

---

## Qué es el proyecto

**Millet ERP** es un sistema ERP back-office interno para **Millet**, empresa
mexicana de vidrio de valor agregado. Reemplaza progresivamente las funciones
de SAP en las áreas de back-office: facturación CFDI, cuentas por cobrar,
compras de no-producción, almacén de no-producción, cuentas por pagar,
activos fijos, contabilidad y reportes/BI.

El sistema **no** maneja la operación comercial ni de producción; eso lo
sigue haciendo el sistema externo **A+W**, que se integra con este ERP. Un
sistema externo de **requisiciones** también se integra (módulo de compras).

Owner del proyecto: **Eduardo Paredes** — `eduardo.paredes@tiglass.net`.

---

## Decisiones arquitectónicas

- **Monolito modular** desplegado como una sola unidad, organizado en
  módulos independientes con fronteras claras.
- **Arquitectura hexagonal (puertos y adaptadores)** dentro de cada módulo,
  con **CQRS** para separar comandos y consultas.
- **MediatR** como mediador para casos de uso (commands/queries/handlers).
- **PostgreSQL único** (Flexible Server en Azure) con **esquemas separados
  por módulo**; cada módulo es dueño de su esquema y nadie más escribe en él.
- **Azure Service Bus** para integración asíncrona entre módulos y con
  sistemas externos (A+W, requisiciones).
- **Microsoft Entra ID** para identidad y autorización; **no hay Active
  Directory local** ni federación con AD on-premises.
- **Strangler Fig** como estrategia de reemplazo de SAP: módulo por módulo,
  sin un corte total. SAP sigue operando en paralelo hasta que cada módulo
  del ERP esté listo y validado.

---

## Módulos del back-office (10)

1. **Identidad y acceso** — transversal; gestión de usuarios, roles y
   permisos sobre Entra ID.
2. **Integración A+W** (`Millet.Integraciones.Aw`) — consume facturación,
   inventario y datos comerciales del sistema externo A+W (on-prem).
   **Diseño activo** — ver
   [`docs/integration/00-system-overview.md`](docs/integration/00-system-overview.md)
   y la sección "Integración A+W" más abajo. Primer consumidor: **Glass
   Agent** (sistema externo en PHP). El sub-namespace `Integraciones` queda
   reservado para integraciones futuras (SAT, bancos, etc.).
3. **Facturación** — emisión de CFDI 4.0, timbrado, cancelación, notas de
   crédito.
4. **Cuentas por Cobrar** — cartera de clientes, cobranza, aplicación de
   pagos, antigüedad de saldos.
5. **Compras** — compras de no-producción; integra con el sistema externo
   de requisiciones; órdenes de compra, recepciones.
6. **Almacén** (`Millet.Almacen`) — inventario físico bajo responsabilidad
   del Jefe Almacén: insumos/refacciones + materiales directos de producción
   no-vidrio (interlayer, silicones, sellantes, pinturas). **Solo el vidrio
   crudo queda fuera** (continúa en A+W). Reemplaza al Portal Millet
   (`PortalSap`) completo: Requisiciones (ya implementado), Salidas de
   mercancía, Reportes. Cinco submódulos: Entradas (dos variantes — factura
   para insumos, packing list para materiales directos), Salidas (normal +
   vale urgente), Inventario físico, Devoluciones (interna 8.A + a proveedor
   8.B con CxP), Reportes. Ver
   [`docs/modulos/almacen/00-levantamiento.md`](docs/modulos/almacen/00-levantamiento.md).
7. **Cuentas por Pagar** (`Millet.CuentasPorPagar`) — proveedores, ciclo
   del pasivo, conciliación con OC, anticipos, notas de cargo, TC
   empresariales. Ver
   [`docs/modulos/cuentas-por-pagar/00-levantamiento.md`](docs/modulos/cuentas-por-pagar/00-levantamiento.md).
   No ejecuta pagos (Tesorería) ni administra préstamos a empleados (RH).
8. **Activos Fijos** — alta, depreciación, bajas, reporte fiscal.
9. **Contabilidad** — plan de cuentas, pólizas, cierre mensual, balanza,
   estados financieros.
10. **Reportes y BI** — reportes operativos y financieros, dashboards,
    integración con herramientas de BI.

---

## Triada Compras ↔ Almacén ↔ Cuentas por Pagar (three-way match)

Los tres módulos están alineados por contratos de eventos (Outbox +
Service Bus, ADR-0009). **Antes de tocar cualquiera de los tres**, leer
los tres levantamientos como conjunto:

- [`docs/modulos/compras-ordenes-compra/01-diseno.md`](docs/modulos/compras-ordenes-compra/01-diseno.md)
- [`docs/modulos/cuentas-por-pagar/00-levantamiento.md`](docs/modulos/cuentas-por-pagar/00-levantamiento.md) §11.6 (contratos canónicos)
- [`docs/modulos/almacen/00-levantamiento.md`](docs/modulos/almacen/00-levantamiento.md) §5, §8 y §11

**Convención canónica de naming:** `{Agregado}{Verbo}Event` con sufijo `Event` y prefijo del agregado de origen. Originada en Compras-OC §8.5–8.6 (primer doc de diseño cerrado). Aplica a todos los módulos del back-office.

**Eventos canónicos en la triada:**

| Evento | Publica | Suscribe | Propósito |
|---|---|---|---|
| `OrdenCompraAutorizadaEvent` | Compras | Almacén, CxP | Habilita OC para recepción y conciliación de factura |
| `OrdenCompraCanceladaEvent` | Compras | Almacén, CxP, Requisiciones | Cancelación de OC; libera RQs comprometidas |
| `OrdenCompraCerradaEvent` | Compras | Almacén (informativo), Requisiciones (cubrimiento) | Cuando los 3 sub-estados (Recepción + Facturación + Pago) llegan a Completa. Compras calcula el cierre — no lo emiten ni Almacén ni CxP |
| `OcRecepcionRegistradaEvent` | Almacén | Compras (sub-estado Recepción de OC), CxP (conciliación) | Recepción contra OC en firme; con flag `factura_pendiente=true` para variante B (materiales directos) |
| `OcDevolucionRegistradaEvent` | Almacén (sub-flujo 8.B) | Compras (decrementa `CantidadRecibida`), CxP (genera `NotaCargo`) | Devolución física al proveedor |
| `FacturaProveedorRegistradaEvent` | CxP | Compras (sub-estado Facturación), Almacén (variante B: concilia recepción pendiente) | Captura de factura con OC en estado `Capturada` |
| `FacturaProveedorRechazadaPorToleranciaEvent` | CxP | Compras (corrige OC) | Conciliación falla por exceder tolerancia |
| `FacturaProveedorAutorizadaEvent` | CxP | Compras (informativo), Contabilidad | Pasa a `Autorizada` |
| `FacturaProveedorCanceladaEvent` | CxP | Compras (decrementa `CantidadFacturada`), Contabilidad | Cancelación de factura |
| `NotaCreditoProveedorRegistradaEvent` | CxP | Compras (decrementa `CantidadFacturada`), Contabilidad | NC general del proveedor (relación CFDI tipo 01, 03 o 07) |
| `NotaCreditoFiscalDevolucionRecibidaEvent` | CxP | Almacén | NC fiscal por devolución (relación CFDI tipo 03 específicamente); cierra la devolución 8.B con flag `ConciliadaConNcFiscal` |
| `CfdiRecibidoIngresadoEvent` | CxP | Almacén | CFDI entra al repositorio (SAT/mailbox/carga manual); enlace diferido de recepciones variante A registradas con folio fiscal a mano (`cfdi_uuid_fiscal`) |
| `DiferenciaPrecioFacturaDetectadaEvent` | CxP | Almacén (ajusta costo), Compras (informativo) | Diferencia dentro de tolerancia entre OC y factura en variante B |
| `PasivoAutorizadoParaPagoEvent` | CxP | Tesorería | Pasivo listo para pago con datos bancarios y evidencias |
| `PagoFacturaProveedorEvent` | Tesorería | Compras (sub-estado Pago), CxP (proyección) | Pago aplicado a una factura |

**Reglas de oro de la triada:**

- **Cero acceso directo a tablas de otro módulo.** Solo puertos de lectura (`I*ReadPort`) o eventos.
- **Almacén NO crea `FacturaProveedor` en CxP.** En variante A solo referencia el `CfdiRecibido`; CxP captura la factura por su flujo normal.
- **Precio de inventario = OC, en ambas variantes A y B.** La diferencia con factura se resuelve aguas arriba (corrección de OC) o vía `DiferenciaPrecioFacturaDetectadaEvent`.
- **Tres tolerancias ortogonales:** cantidad por línea de OC (Compras), cantidad por material (Almacén/`DatosMaestros`), monto por proveedor (CxP/`DatosMaestros`). Ver `almacen/00-levantamiento.md` §10.7.
- **Devoluciones tienen dos semánticas:** interna (Almacén 8.A, no cruza con CxP) y a proveedor (Almacén 8.B, sí cruza con CxP). No confundir.
- **Cierre de OC lo evalúa Compras, no Almacén ni CxP.** Almacén emite `OcRecepcionRegistradaEvent`; CxP emite `FacturaProveedorAutorizadaEvent` y `PagoFacturaProveedorEvent` (proxy desde Tesorería); Compras combina los 3 sub-estados y emite `OrdenCompraCerradaEvent`.
- **Idempotencia en consumidores de eventos** correlacionando por `recepcion_id`, `devolucion_a_proveedor_id`, `factura_id`, `nota_cargo_id`.
- **Acumulados calculados por el publisher.** Eventos que actualizan sub-estados por línea de OC (`FacturaProveedorRegistradaEvent` con `LineasAcumuladasOc[]`) llevan el **acumulado total post-evento** ya calculado en el módulo dueño del dato. Compras lo aplica directo en `OrdenCompra.RegistrarFacturacionLinea` sin proyecciones espejo. Mismo patrón aplica a otros eventos cuantitativos de la triada (cantidad recibida, monto pagado).

**Estado de los workers de la triada (Service Bus):**

| Worker | Topic | Subscription | Estado |
|---|---|---|---|
| `ComprasEventListenerWorker` (en CxP) | `compras-events` | `cuentas-por-pagar-subscription` | ✅ #285 — inbound Compras → CxP |
| `AlmacenEventListenerWorker` (en CxP) | `almacen-events` | `cuentas-por-pagar-almacen-sub` | ✅ F6-PR3 — recepciones + devoluciones |
| `TesoreriaEventListenerWorker` (en CxP) | `tesoreria-events` | `cuentas-por-pagar-tesoreria-sub` | ✅ F9-PR1 — pagos + REPP |
| `CxpEventListenerWorker` (en Almacén) | `cuentas-por-pagar-events` | `almacen-subscription` | ✅ F3-PR1 — variante B + diferencia precio |
| `CxpEventListenerWorker` (en Compras) | `cuentas-por-pagar-events` | `compras-subscription` | ✅ #288 — sub-estado Facturación de OC; #613/#614 — sub-estado Pago vía `factura.pago-aplicado.v1` (acumulado por OC calculado por CxP) + cierre/reapertura automática de la OC |
| `AlmacenEventListenerWorker` (en Compras) | `almacen-events` | `compras-subscription-almacen` | ✅ sub-estado Recepción; #614 — devoluciones 8.B decrementan `CantidadRecibida` (GAP-5) y entregas por salida RQ (ADR-0043) |
| ~~`TesoreriaEventListenerWorker` (en Compras)~~ | — | — | ✅ Resuelto **sin** listener de Tesorería (#613/#614): CxP re-publica el pago como `cuentas_por_pagar.factura.pago-aplicado.v1` y lo consume el `CxpEventListenerWorker`. PLATFORM-TODO `<TesoreriaEventListenerCompras>` cerrado |

> El listener `NotaCreditoProveedorRegistradaListener` (en Compras) sigue huérfano: la NC en CxP no tiene granularidad por línea de OC. PLATFORM-TODO `<NcGranularidadLineaOc>`. Mientras tanto, `cuentas_por_pagar.nota-credito.registrada.v1` se loggea como informativo.

---

## Reportería

[ADR-0036](docs/decisiones/0036-estrategia-de-reporteria.md) define la estrategia transversal de reportería del ERP:

- **Motor nativo** basado en componentes React + endpoints JSON + exportación client-side. **No Crystal embebido**, no SSRS, no engines externos.
- **PDF** con `@react-pdf/renderer`; **Excel** con `exceljs`; **Impresión** con CSS print + `data-print="hidden"`.
- **Plantilla = código React** versionado por git, no archivos binarios opacos.
- **Backend devuelve JSON estructurado** (`{ titulo, generadoEn, filtrosAplicados, columnas, filas, totales? }`); frontend renderiza, filtra y exporta.
- **Reportes operativos** viven en cada módulo (`frontend/src/components/reportes/<modulo>/`); **BI analítico** queda para el módulo 10 (Reportes y BI) con su propio motor (Power BI Embedded u otro) en fase posterior.

Antes de agregar un reporte nuevo, leer el ADR-0036 y reusar `<ReporteShell>` + utilitarios compartidos.

---

## Integración A+W (módulo `Millet.Integraciones.Aw`)

**Contexto obligatorio antes de tocar este módulo:**
[`docs/integration/00-system-overview.md`](docs/integration/00-system-overview.md).
Ese documento es el ancla de toda la integración (mapa funcional, topología
on-prem, contratos con A+W y con el Glass Agent). El levantamiento del
módulo vive en
[`docs/modulos/integraciones-aw/`](docs/modulos/integraciones-aw/) siguiendo
la misma numeración del resto (`00-levantamiento.md`, `01-diseno.md`, etc.).

**El módulo aloja DOS flujos independientes que NO se mezclan (ADR-0048):**
flujo 1 = **cotizaciones** del Glass Agent (saliente: EDI + drop service +
correlación `aw_doc_id`, docs 00–03); flujo 2 = **pedidos en firme para
facturación** (entrante: tabla-puente `aw_solicitud_pedido` + vistas en la BD
on-prem `MILLET_INTEGRACION`, sub-área `Pedidos/`, ver
[`docs/integration/04-ingesta-pedidos-facturacion.md`](docs/integration/04-ingesta-pedidos-facturacion.md)).
Solo comparten la Hybrid Connection y las convenciones del módulo.

**Patrón:** el módulo replica el exemplar establecido por **Compras**
(arquitectura hexagonal + CQRS con MediatR, Application/Domain/Infrastructure,
DbContext propio con esquema separado por ADR-0030). Reutiliza las piezas
transversales ya existentes:

- **Outbox** para emisión de eventos de integración (ADR-0009).
- **HostedServices** dentro de `Millet.Api` para workers de fondo, mismo
  patrón que `OutboxPublisherWorker<ComprasDbContext>`.
- **Idempotency-Key** en mutaciones HTTP (ADR-0020).
- **Versionado** `/api/v1/integraciones/aw/...` (ADR-0021).
- **RBAC granular** con permisos canónicos en
  `Identidad.Domain.PermisosCanonicos` (ADR-0007).
- **Problem Details** RFC 7807 vía `IExceptionHandler` (ADR-0010).
- **ETag/If-Match** para concurrencia optimista (ADR-0012).
- **PLATFORM-TODO** en cualquier stub temporal (ADR-0031).

### Decisiones cerradas (D1–D5)

- **D1 — Adaptador A+W general.** El módulo expone una capa adaptadora
  hacia A+W de propósito general; **no** es un módulo "Glass Agent". Glass
  Agent es sólo el primer consumidor; otros consumidores (CFDI, Almacén,
  CxC) usarán los mismos puertos.
- **D2 — Módulo completo antes de merge.** Se desarrolla el módulo
  end-to-end y **se espera a tener Azure Hybrid Connection establecida**
  hacia el servidor on-prem `SER-DATA` (HCM ya instalado) antes de mergear
  a `main`. Hasta entonces vive en rama feature con stubs.
- **D3 — Auth del Glass Agent vía service principal de Entra ID.** Se
  introduce el concepto de **"usuario de servicio"** en el módulo
  `Identidad` (un `Usuario` con un flag/tipo que lo identifica como
  service principal, asignable a `Empresa` y `Rol` igual que un humano).
  El Glass Agent autentica con client credentials contra Entra ID, obtiene
  un token y lo intercambia por un JWT del API por `POST /api/auth/sesion`,
  igual que un humano hoy.
- **D4 — Workers como `IHostedService` dentro de `Millet.Api`.** Los
  workers `AwDropWorker` (drena drops del Glass Agent al adaptador A+W) y
  `AwCorrelationWorker` (correlaciona respuestas asíncronas de A+W con el
  evento originador) corren en-proceso dentro del App Service, mismo
  patrón que `OutboxPublisherWorker`, `SoftLockExpirationWorker` y
  `IdempotencyKeysCleanupJob`. **No** se introducen Azure Functions ni
  Container Apps Jobs en esta fase.
- **D5 — Nombre del módulo:** `Millet.Integraciones.Aw`. El sub-namespace
  `Integraciones` se reserva para integraciones futuras de la misma
  naturaleza (SAT, bancos, paqueterías, etc.). Esquema Postgres:
  `integraciones_aw`.

### Anti-scope (lo que el módulo NO hace)

- **NO genera EDI.** El Glass Agent (PHP) es quien construye el EDI y lo
  envía al ERP como contenido binario opaco en fase 1; el módulo sólo lo
  recibe, lo persiste y lo enruta a A+W cuando corresponda.
- **NO modifica datos en A+W vía SQL.** El módulo accede a A+W **sólo
  en modo lectura** (consultas vía SQL Server / vistas / SPs que Millet
  exponga del lado on-prem). Cualquier mutación en A+W queda fuera del
  alcance de este módulo.
- **NO conoce glass-specific quoting** (medidas de vidrio, cálculos de
  área/perímetro, optimización de corte, reglas de templado, etc.). Toda
  esa lógica vive en el Glass Agent. El ERP trata los payloads como
  opacos a nivel de negocio del vidrio.

---

## Convenciones de código C#

- **Nullable reference types** habilitados en todos los proyectos.
- **`async`/`await`** siempre que haya I/O (DB, HTTP, Service Bus, archivos).
  Nada de `.Result` ni `.Wait()`.
- **`record`** para DTOs y value objects (inmutables por defecto).
- **`sealed`** por defecto en clases concretas; abrir herencia solo cuando
  haya razón explícita.
- **FluentValidation** para validación de comandos y queries —
  **NO** Data Annotations.
- **Mapster** para mapeo entre DTOs/entidades — **NO** AutoMapper.
- **Serilog** para logging estructurado, con sinks a Application Insights
  en Azure y consola en local.

---

## Convenciones de Bicep / Azure

- **Patrón de nombres:** `{tipo}-{proyecto}-{ambiente}-{región}-{secuencia}`
  - Ejemplo: `rg-millet-dev-mxc-01`, `psql-millet-prod-mxc-01`.
  - `tipo`: prefijo del recurso (`rg`, `psql`, `kv`, `st`, `sb`, etc.).
  - `proyecto`: `millet`.
  - `ambiente`: `dev`, `qa`, `prod`.
  - `región`: `mxc` (México Central).
  - `secuencia`: `01`, `02`, ...
- **Storage Accounts:** solo letras minúsculas y dígitos (Azure no permite
  guiones ni mayúsculas en el nombre).
- **Tags consistentes** en todos los recursos:
  `project=millet`, `environment={dev|qa|prod}`, `owner=eduardo.paredes@tiglass.net`,
  `costCenter`, `managedBy=bicep`.
- **Parametrización por ambiente** en archivos `.bicepparam` separados
  (`infra/parameters/dev.bicepparam`, `prod.bicepparam`, etc.). Nunca
  valores hardcodeados específicos de ambiente en los módulos.
- **Secretos jamás** en código ni en parámetros — usar **Azure Key Vault**
  con referencias `getSecret()` desde Bicep.

---

## Comandos comunes de despliegue

Todos los comandos asumen que estás autenticado con `az login` y que la
suscripción correcta está seleccionada (`az account set --subscription <id>`).

**Validar plantilla (sintaxis y referencias):**
```bash
az deployment sub validate \
  --location mexicocentral \
  --template-file infra/main.bicep \
  --parameters infra/parameters/dev.bicepparam
```

**What-if (previsualizar cambios sin aplicarlos):**
```bash
az deployment sub what-if \
  --location mexicocentral \
  --template-file infra/main.bicep \
  --parameters infra/parameters/dev.bicepparam
```

**Deploy real:**
```bash
az deployment sub create \
  --location mexicocentral \
  --template-file infra/main.bicep \
  --parameters infra/parameters/dev.bicepparam
```

**Regla:** SIEMPRE correr `what-if` antes de `create` en cualquier ambiente
distinto a un sandbox personal. En `prod` es obligatorio revisar el output
con un segundo par de ojos.

**Password de PostgreSQL:** `postgresAdminPassword` se resuelve solo, vía
`az.getSecret()` en el `.bicepparam` contra el secreto
`postgres-admin-password` del Key Vault del ambiente — los comandos de
arriba NO llevan (ni deben llevar) la password. Solo en el **bootstrap**
de un ambiente nuevo (el KV aún no existe) se pasa por CLI:
`--parameters postgresAdminPassword='<PASSWORD>'` (ver `infra/README.md`).
OJO: `what-if` NO detecta cambios de password (`administratorLoginPassword`
es write-only); antes del getSecret, un deploy sin override reseteaba el
password real de `pgadmin` al placeholder y tumbaba la app (incidente
2026-07-07, 28P01 en loop).

---

## Deuda de plataforma y stubs `NoOp`

Cuando un módulo de negocio se construye antes de que cierta
infraestructura compartida exista (`CollaborationHub` SignalR, Outbox,
motor de notificaciones, etc.), usa una implementación temporal `NoOp`
o stub. Para que esa deuda no quede invisible:

1. **Sección "Dependencias de plataforma pendientes"** en el documento
   de diseño del módulo (`docs/modulos/<modulo>/01-diseno.md`),
   con tabla *Pieza · Ticket · NoOp en uso · Cómo se wirea*.
2. **Comentario `// PLATFORM-TODO(<identificador>): ...`** en cada
   `NoOp` o stub temporal del código. Buscable con
   `rg "PLATFORM-TODO" backend/src`. El `<identificador>` es el ID
   del sistema de tickets si existe (`MIL-1234`, `#42`); si no, un
   nombre descriptivo entre `<>` consistente con la tabla del módulo
   (`<CollaborationHub>`, `<Outbox>`).
3. **Checklist "Módulos consumidores"** en el ticket de plataforma. El
   ticket no se cierra hasta que todos los módulos del checklist están
   wired.

Detalle, plantillas y proceso de cierre en **ADR-0031**.

Esta convención aplica a **todos los módulos** del back-office desde el
primero (Compras / Requisiciones).

---

## Patrones de UI del frontend (cross-módulo)

El módulo Compras Requisiciones es el **exemplar** de los patrones de UI
del back-office. La estructura de ventanas, navegación y forms se
documenta en
[`frontend/docs/patrones-compras.md`](frontend/docs/patrones-compras.md)
y debe replicarse en cada módulo nuevo (CxC, OC, CxP, Activos Fijos,
Contabilidad, BI). Resumen:

- **Master-detail** con lista compacta 320px sticky a la izquierda +
  panel detalle a la derecha; mobile drill-down. Rutas
  `/<modulo>/<recurso>` (bandeja tabular) + `/<modulo>/<recurso>/$id`
  (master-detail).
- **Sheet (slide-from-right)** para forms de "Nueva ..."; provider a
  nivel shell con `useNuevaXxx().abrir()`; confirm al cerrar con
  `isDirty`; `Force: true` en success.
- **Inline forms** (sin modal) para editar/agregar items de un master:
  border dashed primary (agregar) vs amber (editar).
- **Topbar global**: search contextual por ruta con debounce 200ms,
  Quick Create popover por módulo, ayuda contextual.
- **Sub-topbar** del detalle sticky con `data-print="hidden"`; aside
  master también `data-print="hidden"` para impresión limpia.

Cuando llegue el siguiente módulo, abre primero esa doc y copia el
patrón del más cercano a tu caso (P1 = bandeja general, P2 = bandeja
filtrada server-side, P3 = detalle, P4 = nueva con sheet). Las
diferencias entre módulos viven en el query/schema/columnas, no en la
estructura de ventanas.

---

## Notas para Claude Code

- **Abre VS Code en la raíz del monorepo**, no en una subcarpeta. Si lo
  abres en `infra/` o `backend/`, los analizadores y configuraciones del
  monorepo no funcionan correctamente.
- **Antes de cambios grandes**, revisa el `README.md` de la subcarpeta
  correspondiente (`infra/README.md`, `backend/README.md`, etc.). Cada
  subcarpeta tiene su propio contexto y prerequisitos.
- **Para `infra/`:** SIEMPRE validar con `what-if` antes de aplicar
  cualquier cambio. Nunca hagas `az deployment sub create` directo sin
  haber visto el what-if primero.
- **Secretos JAMÁS en código.** Ni en archivos `.bicepparam`, ni en
  `appsettings.json`, ni en commits. Si necesitas un secreto, va a
  **Azure Key Vault** y se referencia desde ahí.
- **No commitear sin permiso explícito** del owner. Por defecto, después
  de hacer cambios, reporta qué hiciste y espera instrucción para commitear.
- **Idioma:** documentación, comentarios de negocio y nombres de módulos
  en **español**. Código, comandos y nombres técnicos en inglés.
- **Convenciones de Git:** ramas con prefijo `feature/`, `fix/`, `chore/`,
  `docs/`. Nunca trabajar directo en `main`.
