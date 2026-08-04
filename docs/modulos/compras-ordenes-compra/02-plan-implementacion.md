# Plan de implementación — Submódulo Órdenes de Compra (Compras)

> **Construido sobre:** [01-diseno.md](01-diseno.md) (Rev. 0.2) y
> el código backend del módulo `Millet.Compras` ya en `main` (cierre
> de Requisiciones, UF8-PR1, soft-lock real con SignalR).
>
> **Estado:** propuesta de plan para revisión con el equipo. Sizing en
> bandas (XS/S/M/L/XL) — calibrar contra capacidad real del equipo.
>
> **Fecha:** 2026-05-11.

---

## 0. Cómo leer

- Sizing en bandas:
  - **XS** ≈ 1–2 días
  - **S** ≈ 3–5 días
  - **M** ≈ 1–2 semanas
  - **L** ≈ 3–4 semanas
  - **XL** > 1 mes
- Cada fase produce algo **deployable y validable** — no son
  entregables internos del equipo.
- Las fases son secuenciales por dependencia técnica, pero dentro de
  cada fase hay paralelismo posible (anotado como "‖").
- **Reuso primero (regla del proyecto):** cada fase indica qué pieza
  hereda de Requisiciones o del shell antes de listar lo nuevo.

---

## 1. Resumen ejecutivo

**Objetivo:** entregar v1 del submódulo Órdenes de Compra del módulo
Compras del nuevo ERP, alineado con el diseño aprobado en `01-diseno.md`.

**Estrategia:**

1. **Reuso máximo** del módulo Compras ya implementado para
   Requisiciones. OC vive en el **mismo proyecto .NET**
   (`Millet.Compras`), el **mismo schema** (`compras`), el **mismo
   DbContext** (`ComprasDbContext`). No hay nuevo módulo ni nuevo
   bounded context. Foundation se reduce a ~1 día.
2. **Reemplazar progresivamente los stubs cross-port** que hoy
   `Millet.Compras` tiene contra OC. Los contratos `IGenerarSolicitudCompraPort`,
   `OcRecepcionRegistradaEvent` y `OcCerradaEvent` **ya existen**
   como interfaces en `Compras.Domain.Ports.OrdenCompra/`. El
   submódulo de OC implementa la cara real de esos puertos.
3. **Walking skeleton primero** (crear y leer una OC vacía con folio),
   antes que features ricos.
4. **Iterativo:** cada fase suma capacidad sobre el agregado
   `OrdenCompra`. Borrador → autorización → creación desde RQ →
   sub-estados → PDF → reportes.
5. **Stubs cross-BC** para Almacén/CxP/Tesorería/PDF/Notificaciones
   mientras esos módulos no existen. Patrón ya validado con RQ.
6. **Catálogos via seeds + endpoints GET read-only** durante todo el
   MVP. **No hay importación desde SAP ni pantallas CRUD en este
   alcance** — son bloque separado post-MVP (mapa funcional §10
   "Decisiones resueltas").

**Pendientes que NO bloquean arranque:**

- Definición de servicio PDF (PDFsharp, QuestPDF, etc.) — Fase 6.
- Existencia de módulos Almacén/CxP/Tesorería para wireup real de
  Outbox + Service Bus — Fase 8. Hasta entonces, listeners in-process.

---

## 2. Prerrequisitos — audit del repo (2026-05-11)

Auditado contra `c:\Users\UserSP\Desktop\Project_Millet_ERP\backend\`.
Estado real:

| Prerrequisito | Estado | Detalle / plan B |
|---|---|---|
| Proyecto `Millet.Compras` | ✅ existe | RQ ya está implementado; OC se agrega como sub-namespace dentro del mismo proyecto. |
| `ComprasDbContext` + schema `compras` | ✅ existe | OC agrega `DbSet<OrdenCompra>`, `DbSet<LineaOrdenCompra>`, etc. No nuevo DbContext. **No aplica** el checklist de "DbContext nuevo" (Program.cs + deploy-app-dev.yml). |
| `BaseEntity` con `Version IsConcurrencyToken` (ADR 0012 Capa 1) | ✅ existe | `OrdenCompra` hereda. |
| `BaseDbContext` con interceptors (Audit, Empresa, Metadata) | ✅ existe | Auditoría e idempotencia funcionan gratis. |
| EF Core + migraciones por esquema (ADR 0005) | ✅ existe | OC agrega migraciones bajo `Compras/Infrastructure/Migrations/`, secuenciales con las de RQ. |
| Identidad + RBAC granular + Entra ID | ✅ existe | Permisos canónicos `compras.ordenes.*` se agregan a `PermisosCanonicos.Todos`. |
| `RequirePermissionAttribute` + handler | ✅ existe | Endpoints OC se anotan igual que RQ. |
| Problem Details (ADR 0010) | ✅ existe | Errores de OC siguen el mismo formato. |
| `Money`, `Moneda`, `Empresa` | ✅ existe | VOs reusados en OC. |
| Soft delete vía `IFiscalmenteRelevante` | ✅ existe | `OrdenCompra` lo implementa. |
| Health checks + `MigrationsAppliedHealthCheck` | ✅ existe | Migraciones de OC se cubren automáticamente. |
| Serilog + masking | ✅ existe | Logging estructurado gratis. |
| `IClock`, `ICurrentUserContext`, `ICurrentEmpresaContext` | ✅ existe | Inyectados en handlers. |
| **Puertos a OC ya definidos como contratos** | ✅ existe | `Compras.Domain.Ports.OrdenCompra/IGenerarSolicitudCompraPort.cs`, `OcRecepcionRegistradaEvent.cs`, `OcCerradaEvent.cs`, `LineaSaldo.cs`. **OC implementa la cara real**. |
| **Stubs InMemory de OC en RQ** | ✅ existe | `InMemoryGenerarSolicitudCompraPort`, `OcBorradorStub`. Se desactivan progresivamente cuando OC implementa el puerto real. |
| Listeners de eventos de OC en RQ | ✅ existe | `OcRecepcionRegistradaListener`, `OcCerradaListener`. OC los emite, RQ los consume — el wiring ya está. |
| Catálogo `MotivoRechazo` + `MotivoRechazoAplicaA` bitmask | ✅ existe | OC extiende el seed agregando bit `OrdenCompra = 8` al bitmask. |
| Patrón `Folio` + secuencia atómica | ✅ existe (`compras.folio_secuencias` para RQ) | OC crea `compras.folio_secuencias_oc` siguiendo el mismo patrón. |
| `IConsultarStockPort` | ✅ existe | Reusado para "Existencia actual" en captura de línea de OC. |
| **`CollaborationHub` SignalR (ADR 0012 Capa 2)** | ✅ existe (soft-lock real, UF8-PR1 mergeado) | OC declara `OrdenCompra` en la lista de entidades con awareness colaborativo. Sin código adicional. |
| **Outbox + Service Bus (ADR 0009)** | ⏳ pendiente | Solo en README. Plan B: `IIntegrationEventPublisher` `NoOp` heredado; eventos in-process vía MediatR `INotification`. Wireup real en Fase 8 cuando los módulos consumidores (Notificaciones, Almacén, CxP) lo requieran. |
| Módulos Almacén-Recepción, CxP, Tesorería | ⏳ no existen | Stubs cross-BC siguiendo el patrón de RQ. |
| Servicio PDF | ⏳ no existe | Stub `IGenerarPdfOrdenCompraPort` retorna placeholder. Wireup real en Fase 6 cuando se elija librería (PDFsharp / QuestPDF / Aspose). |
| Módulo Notificaciones | ⏳ no existe | `IIntegrationEventPublisher` `NoOp` heredado; emails se conectan cuando Notificaciones exista (Fase 8). |
| OpenAPI + tipos TS (ADR 0017) | ✅ existe | OC se agrega al pipeline automáticamente. |

**Conclusión del audit: cero bloqueantes para arrancar Fase 0.** Toda
la plataforma compartida ya está construida (incluyendo soft-lock real,
ya mergeado). Las brechas (Outbox, módulos consumidores, servicio PDF)
son habilitadores transversales que se cubren con stubs hasta que los
tickets de plataforma cierren.

> **Tracking del debt (ADR-0031):** los `NoOp` heredados y los nuevos
> (servicio PDF, listeners cross-BC) se documentan en §12 del
> `01-diseno.md` y llevan `// PLATFORM-TODO(<id>): ...` en código.

---

## 3. Dependencias con otros módulos

| Módulo | Necesidad | Estado | Plan |
|---|---|---|---|
| **Compras / Requisiciones** (mismo BC) | consumir RQs autorizadas, marcarlas como comprometidas, liberar al cancelar | ✅ existe | **integración in-process** vía MediatR. Se amplía el agregado `Requisicion` con `comprometida_en_oc_id` (Fase 4). |
| **Identidad** | usuarios, roles, permisos `compras.ordenes.*` | ✅ existe | extensión de `PermisosCanonicos` (Fase 0). |
| **Almacén / Recepción** | consumir `OcRecepcionRegistradaEvent`, `OcDevolucionRegistradaEvent` | ⏳ no existe | listener stub `Application/Eventos/OcRecepcionRegistradaListener` ya wireado en RQ; OC lo replica para sus líneas. Evento se emite hoy desde stub `OcBorradorStub` y se moverá al módulo real cuando exista. |
| **Cuentas por Pagar (CxP) / Factura proveedor** | consumir `FacturaProveedorRegistradaEvent`, `NotaCreditoProveedorRegistradaEvent` | ⏳ no existe | contratos se definen en `Compras.Domain.Ports.Cxp/` como interfaces stub; listeners no se disparan hasta que CxP exista. |
| **Tesorería** | consumir `PagoFacturaProveedorEvent` | ⏳ no existe | mismo patrón que CxP. |
| **Notificaciones** | enviar emails por evento (envío a autorización, autorizada, rechazada) | ⏳ no existe | eventos se publican al Outbox y se consumen cuando Notificaciones esté listo. |
| **Servicio PDF** | generar PDF institucional al autorizar | ⏳ no existe | `IGenerarPdfOrdenCompraPort` con stub que devuelve PDF placeholder. Wireup real en Fase 6. |
| **Datos Maestros** | catálogos de proveedores, artículos, condiciones de pago, monedas, almacenes, departamentos, sucursales, incoterms, transportistas, tipos de documento OC | ⏳ no existe (solo seeds) | **seeds versionados + endpoints GET read-only** durante MVP. Las pantallas de CRUD y la migración inicial desde SAP **NO forman parte del scope de OC v1** — son bloque separado post-MVP. |
| **A+W** | catálogo de clientes (para "cliente final destinatario") | externo | **DIFERIDO a Fase 2** (decisión §10.4 cerrada). No participa en MVP. |
| **Frontend OC** (UI consumidora) | endpoints estables de cada fase backend; el plan de UI ([06-frontend-plan-implementacion.md](06-frontend-plan-implementacion.md) Rev. 1) consume las APIs en sincronía | ⏳ frontend espera fase backend correspondiente | **camino crítico cruzado** — ver §6.bis abajo. |

### 3.bis Camino crítico cruzado backend ↔ frontend

El release v1 de OC depende del **máximo** de los dos caminos. Cada
fase de UI espera la fase backend correspondiente; un atraso en
cualquiera empuja la fecha de release. Tabla de alimentación:

| Fase backend (este plan) | Alimenta a fase frontend ([06](06-frontend-plan-implementacion.md)) | Endpoints / piezas clave |
|---|---|---|
| **F0** Foundation + permisos | UF0 Foundation OC UI (paralelizable) | 10 permisos canónicos `compras.ordenes.*` |
| **F1** Walking skeleton | UF1 Read-only de OC (lectura básica) | `POST /ordenes`, `GET /ordenes/{id}` |
| **F2** Borrador completo (líneas + adjuntos + logística) | UF2 Captura básica + UF3 Información (parcial) | comandos de líneas y cabecera; estructura `LineaOrdenCompra`, `AdjuntoOC` |
| **F3** Workflow autorización | UF1 (mostrar autorizaciones en read-only) + UF4 Workflow autorización | `EnviarAAutorizacion`, `AutorizarOrdenCompra`, `RechazarOrdenCompra`, sub-tabs de líneas |
| **F4** Creación desde RQ + compromiso exclusivo | UF2 (Sheet con 3 modos + selector consolidación) | endpoint `requisiciones-disponibles`, `CrearOrdenCompraDesdeRequisicion`, `AgregarLineaDesdeRequisicion` |
| **F5-PR3** Reemplazo stub OC + **F5-PR4** Cancelar con recepciones | UF5 Cancelar + Duplicar | comando `CancelarOrdenCompra` con doble firma |
| **F6-PR1** PDF stub + listener + **F6-PR2** Duplicar | UF5 (Duplicar) + UF6 (PDF) | `DuplicarOrdenCompraCommand`, `IGenerarPdfOrdenCompraPort` stub, endpoint `GET /ordenes/{id}/pdf` |
| **F6-PR3** PDF real (QuestPDF) + bandejas | UF6 PDF real + UF1 bandejas mejoradas | `QuestPdfOrdenCompraImpl`, `ListarOrdenesCompraQuery`, `ListarPendientesAutorizacionOcQuery` |
| **F7-PR1** Partidas abiertas | UF7 P9 Partidas abiertas (tabla) | `ListarPartidasAbiertasQuery` + endpoint |
| **F7-PR2** Árbol documentos | UF7 P10 Árbol documentos | `ObtenerArbolDocumentosQuery`, `<ArbolDocumentos>` cross-módulo |
| **F7-PR3** Historial + bandejas + KPIs | UF7 (tab Historial completo + aside list hermanas + KPI cards P9) + UF1 (presets de bandeja) | `ObtenerHistoricoOrdenCompraQuery`, `ListarOcsHermanasDuplicadasQuery`, `ObtenerKpisPartidasAbiertasQuery`, `ListarUltimas100ComprasMaterialQuery` |
| **F8** Eventos integración + Notificaciones | (no alimenta UI directamente; los emails llegan a usuarios externos) | — |
| **F9** Catálogos seed completos | UF3 Información + Adjuntos (selectores) + UF6 PDF real | catálogos `Incoterm`, `Transportista`, `RegimenFiscal`, `CondicionesPago` via endpoints GET read-only |
| **F10** Hardening + UAT | UF8 Hardening + UAT (sincronía en UAT con ambos equipos) | — |

**Implicación para coordinación**: el dev backend y el dev UI alinean
agenda al inicio de cada fase. Si una fase backend se atrasa, la UI
correspondiente se mueve al siguiente sprint. Si una fase UI termina
antes que su backend, espera (no avanzar a la siguiente fase UI sin
backend listo, para evitar refactor cuando llegue el DTO real).

---

## 4. Fases

### Fase 0 — Foundation OC + permisos canónicos (S)

Crear la estructura de namespaces y los permisos canónicos. **Sin
features funcionales todavía.** Toda la infraestructura compartida ya
existe.

**Heredado:** proyecto `Millet.Compras`, `ComprasDbContext`,
`BaseEntity`, MediatR, FluentValidation, Mapster, RBAC, Entra ID,
Problem Details, Serilog, Health checks, OpenAPI — todo wired.

**Nuevo:**

- [ ] Crear sub-namespace `Millet.Compras.Domain.Oc`,
      `Millet.Compras.Application.Oc`, `Millet.Compras.Infrastructure.Oc`,
      `Millet.Api.Endpoints.Compras.Oc`.
- [ ] Agregar 10 constantes a
      `Identidad/Domain/PermisosCanonicos.cs` (namespace GUIDs
      `00000004-...`):
  - `compras.ordenes.leer`
  - `compras.ordenes.crear`
  - `compras.ordenes.crear.sin_rq` (FOC11 — caso especial §4.3)
  - `compras.ordenes.adjuntar`
  - `compras.ordenes.logistica`
  - `compras.ordenes.autorizar.nivel1`
  - `compras.ordenes.autorizar.nivel2`
  - `compras.ordenes.cancelar`
  - `compras.ordenes.cancelar.doble`
  - `compras.ordenes.reportes.partidas_abiertas`
- [ ] Migración EF Core de seed (`IdentidadDbContext` — auto-detectada
      por delta de `HasData`).
- [ ] Endpoint dummy `GET /api/v1/compras/ordenes/smoke` con
      `[RequirePermission("compras.ordenes.leer")]` para validar el
      wiring de permisos.
- [ ] Tests negativos (403 sin permiso, 200 con permiso).
- [ ] Coordinar con el cliente la asignación inicial de permisos a
      roles (mapeo tentativo en §9 del 01-diseño).

**Criterio de aceptación:** migración aplica; los 10 permisos
aparecen en `identidad.permisos`; smoke test verifica 403/200. El
módulo Compras sigue compilando y desplegando sin regresiones.

---

### Fase 1 — Walking skeleton OC (S)

Crear y leer una OC vacía en estado `Borrador`. Sin líneas, sin
adjuntos, sin autorización. Solo prueba que el plumbing del agregado
funciona end-to-end.

**Heredado:** patrón de agregado raíz, ETag pattern, idempotency-key.

**Nuevo:**

- [ ] Agregado raíz `OrdenCompra` con campos mínimos de cabecera
      (`EmpresaId`, `Folio`, `ProveedorId`, `SucursalDestinoId`,
      `AlmacenDestinoDefaultId`, `Moneda`, `CondicionesPagoId`,
      `UsoPrincipalId`, `CompradorTitularId`, `Estado = Borrador`,
      flags `SinRequisicionPrevia`, `EsImportacion`).
- [ ] Enum `EstadoOrdenCompra` (7 valores: Borrador,
      EnAutorizacionJefeCompras, EnAutorizacionDireccion, Autorizada,
      Cerrada, Cancelada, Rechazada).
- [ ] Enum `SubEstadoRecepcion`, `SubEstadoFacturacion`,
      `SubEstadoPago` (con default en cero para Borrador).
- [ ] VO `Folio` (parametrizado, prefijo `OC-`).
- [ ] Tabla `compras.ordenes_compra` con índices del §10.1 del diseño.
- [ ] Secuencia `compras.folio_secuencias_oc` (patrón de RQ).
- [ ] Migración EF Core.
- [ ] Comando `CrearOrdenCompraVaciaCommand` + handler MediatR +
      validator FluentValidation. Folio atómico vía `nextval`.
- [ ] Query `ObtenerOrdenCompraPorIdQuery` + handler.
- [ ] Endpoints `POST /api/v1/compras/ordenes` y
      `GET /api/v1/compras/ordenes/{id}` con permisos
      `compras.ordenes.crear` / `compras.ordenes.leer`.
- [ ] DTO con `Version` expuesto en `ETag`.
- [ ] Tests unitarios del agregado (crear, invariantes).
- [ ] Tests de integración happy path (POST → GET → ETag).

**Criterio de aceptación:** `curl POST` crea una OC vacía con folio
formateado (`OC-MID2026-000001`); `GET` la recupera con su ETag; 404
ProblemDetails para id inexistente.

---

### Fase 2 — Borrador completo (M)

Líneas + adjuntos + información logística + información de importación.
La OC se puede capturar y editar libremente sin todavía enviar a
autorización.

‖ paralelizable: el frontend puede arrancar pantallas de bandeja read-only.

**Heredado:** patrón de entidad hija con `ON DELETE CASCADE`,
`MotivoRechazo` catálogo (sin tocarlo todavía), `IConsultarStockPort`.

**Nuevo:**

- [ ] Entidad `LineaOrdenCompra` con todos los campos del §4.2 del
      diseño (incluyendo `cantidad_recibida`, `cantidad_facturada` en 0).
- [ ] Tabla `compras.orden_compra_lineas` con `ON DELETE CASCADE` a la
      cabecera + índices.
- [ ] Comandos `AgregarLineaManualCommand`, `ActualizarLineaCommand`,
      `EliminarLineaCommand` (solo en `Borrador` / `Rechazada`).
- [ ] Comando `ActualizarCabeceraCommand` (solo en
      `Borrador` / `Rechazada`).
- [ ] VOs `InformacionLogistica`, `InformacionImportacion` y comandos
      asociados (`ActualizarInformacionLogisticaCommand`,
      `ActualizarInformacionImportacionCommand`).
- [ ] Entidad `AdjuntoOC` + tabla `compras.orden_compra_adjuntos`.
- [ ] Catálogo `compras.tipos_documento_oc` con seed (cotizacion,
      ficha_tecnica, correo_autorizacion, pedimento,
      factura_proveedor_extranjero, packing_list, otro).
- [ ] Comandos `AdjuntarDocumentoCommand` + `RemoverAdjuntoCommand`
      (este último solo en `Borrador`).
- [ ] Stub `IAlmacenarBlobPort` con implementación local (filesystem
      `/tmp` o blob de dev) — wireup real con ADR 0024 en Fase 11.
- [ ] VO `TotalesOC` (computed) — recalcular en cada cambio de líneas
      o cabecera. Cálculo de IVA simplificado v0 (tasa fija 16% sobre
      base gravable); el motor de regímenes fiscales completo entra
      en Fase 3.
- [ ] VOs `DescuentoGlobal`, `DescuentoLinea`, `ContactoProveedor`,
      `ReferenciaProveedor`.
- [ ] Validators FluentValidation completos para cabecera y líneas.
- [ ] Tests unitarios del agregado (cada invariante).
- [ ] Tests de integración: capturar OC completa con N líneas, N
      adjuntos, logística + importación.

**Criterio de aceptación:** captura completa de una OC en `Borrador`
funciona. Adjuntar/quitar documentos en `Borrador` OK. Totales
calculan con tasa fija. Tests pasan.

---

### Fase 3 — Workflow de autorización (M)

Enviar a autorización, autorizar N1, autorizar N2, rechazar, cancelar.
La OC alcanza estado `Autorizada` sin todavía conectarse a RQs ni
emitir PDF.

**Heredado:** patrón de `Autorizacion` y `MotivoRechazo` de RQ,
`NivelAutorizacion` enum, motor de validación de motivos
(`permite_texto_libre`).

**Nuevo:**

- [ ] Extender `MotivoRechazoAplicaA` bitmask con
      `OrdenCompra = 8`. Migración aditiva al seed; los motivos
      existentes (RECH-DUP, RECH-INSUF, etc.) se marcan también para
      OC si aplica.
- [ ] Entidad `AutorizacionOC` + tabla
      `compras.orden_compra_autorizaciones` con índice único
      `(orden_compra_id, nivel) WHERE resultado = 1`.
- [ ] Comando `EnviarAAutorizacionCommand` con validaciones del §7.1
      del diseño:
  - al menos 1 línea con cantidad > 0 y precio > 0.
  - proveedor activo (C10).
  - cotización adjunta **o** `CotizacionExcepcionada == true` con
    adjunto `correo_autorizacion` (C11).
  - si `EsImportacion`: ficha técnica adjunta.
  - si `SinRequisicionPrevia`: motivo + correo autorización.
- [ ] Comandos `AutorizarOrdenCompraCommand` (con `Nivel = Nivel1` o
      `Nivel2`) y `RechazarOrdenCompraCommand` con
      `motivoId` + `motivoTexto?`.
- [ ] Transiciones de state machine: Borrador →
      EnAutorizacionJefeCompras → EnAutorizacionDireccion →
      Autorizada. Rechazos en cualquier nivel → Rechazada.
- [ ] Setear `FechaContabilizacion = now()` al autorizar N2.
- [ ] Comando `CancelarOrdenCompraCommand` (en Borrador o no terminal,
      sin recepciones todavía — la liberación parcial entra en Fase 5).
- [ ] Endpoints REST correspondientes.
- [ ] Eventos in-process: `OrdenCompraEnviadaAAutorizacionEvent`,
      `OrdenCompraAutorizadaEvent`, `OrdenCompraRechazadaEvent`,
      `OrdenCompraCanceladaEvent` (sin Outbox todavía).
- [ ] Listeners de logging para cada evento (`*LoggingHandler` patrón
      de RQ).
- [ ] **Concurrencia (ADR 0012)**:
  - Capa 1: `OrdenCompra` hereda de `BaseEntity` con `Version`
    `IsConcurrencyToken`. EF Core la incrementa automáticamente.
  - Capa 2: declarar `OrdenCompra` en la lista de entidades con soft
    lock del módulo Compras (la infraestructura ya existe — solo se
    agrega a la lista).
- [ ] **Motor de impuestos v1 (C3):**
  - Tabla `compras.regimenes_fiscales_articulo` (seed con tasas).
  - Lookup por (régimen proveedor, régimen artículo) → IVA aplicable
    + retención ISR si aplica.
  - Reemplazar el cálculo simplificado v0 de Fase 2 por el motor real.
- [ ] Tests integration: captura → enviar → N1 → N2 → Autorizada.
      Rechazo en N1 → Rechazada. Rechazo en N2 → Rechazada (motivo
      registrado). Cancelar desde no terminal sin recepciones.

**Criterio de aceptación:** ciclo de autorización completo funciona;
los 4 estados intermedios (Borrador, EnAutorizacionJefeCompras,
EnAutorizacionDireccion, Autorizada) operan correctamente. Rechazos y
cancelaciones registran motivo. Soft lock muestra "X está editando"
cuando dos compradores abren la misma OC.

---

### Fase 4 — Creación desde RQ + compromiso exclusivo (M)

Flujos §4.1 (1:1 desde bandeja de RQ) y §4.2 (N:1 consolidación) del
mapa funcional. Una RQ se compromete exclusivamente a una OC activa;
se libera al cancelar.

**Heredado:** agregado `Requisicion`, query de listado de RQs.

**Nuevo:**

- [ ] Ampliar agregado `Requisicion`: agregar campo
      `ComprometidaEnOcId : OrdenCompraId?` + métodos
      `ComprometerEnOc(ocId)` y `LiberarDeOc()`.
- [ ] Migración EF Core: `ALTER TABLE compras.requisiciones ADD
      COLUMN comprometida_en_oc_id uuid REFERENCES compras.ordenes_compra(id)`
      + 2 índices parciales (`ix_requisiciones_comprometida`,
      `ix_requisiciones_disponibles`).
- [ ] Query `ListarRequisicionesDisponiblesParaConsolidarQuery`:
      filtro por `estado = Autorizada AND comprometida_en_oc_id IS
      NULL AND sucursal_id = :sucursalOc` (restricción §10.5 cerrada).
- [ ] Comando `CrearOrdenCompraDesdeRequisicionCommand` (flujo §4.1):
      crea OC pre-llenada con cabecera de la RQ + líneas con FK a
      `RequisicionId` y `LineaRequisicionId`. Emite
      `RqComprometidaEnOcEvent` que actualiza la RQ.
- [ ] Comando `AgregarLineaDesdeRequisicionCommand` (flujo §4.2):
      agrega líneas adicionales a una OC en Borrador desde RQs del
      selector. Valida que todas tengan misma sucursal que la OC.
- [ ] Política de líneas en consolidación (§3.bis.4 del diseño): si
      mismo `articulo_id` viene de varias RQs, se preservan líneas
      separadas (no se suman).
- [ ] Ampliar `EliminarLineaCommand`: si la línea viene de una RQ,
      emite `LineaRqLiberadaEvent` que limpia `comprometida_en_oc_id`
      en la RQ (si era la última línea de esa RQ en la OC).
- [ ] Ampliar `CancelarOrdenCompraCommand`: emite
      `OrdenCompraCanceladaEvent` que libera **todas** las RQs
      comprometidas (sólo válido si no hay recepciones — la
      liberación parcial entra en Fase 5).
- [ ] Comando `AgregarLineaManualCommand` (flujo §4.3): solo
      permitido si `SinRequisicionPrevia == true`.
- [ ] Restricción: una OC no puede mezclar líneas con
      `SinRequisicionPrevia = false` (vinculadas a RQ) y
      `SinRequisicionPrevia = true` (manuales) — flag es a nivel
      cabecera, fijo desde la creación.
- [ ] Listener `RqComprometidaEnOcListener` en el agregado
      `Requisicion` (mismo BC, in-process MediatR).
- [ ] Tests integration:
  - Crear OC desde RQ → RQ pasa a comprometida.
  - Crear OC vacía → seleccionar 3 RQs de la misma sucursal → líneas
    importadas, RQs comprometidas.
  - Seleccionar RQ de otra sucursal → 422 (validación).
  - Quitar línea → RQ regresa al pool si era la última línea suya.
  - Cancelar OC en Borrador → todas las RQs regresan al pool.

**Criterio de aceptación:** los dos flujos de creación funcionan;
el selector respeta la restricción de sucursal; el compromiso
exclusivo se mantiene; cancelar libera correctamente.

---

### Fase 5 — Sub-estados + listeners cross-BC + cierre automático (M)

Sub-estados materializados (Recepción / Facturación / Pago) que avanzan
independientemente vía listeners. Cierre automático cuando los tres
llegan a Completa/Completa/Pagada. Cancelación con liberación parcial
de RQs.

‖ paralelizable: equipo de Almacén-Recepción y CxP pueden leer los
contratos.

**Heredado:** patrón listener (`*LoggingHandler` de RQ), evento
`OcRecepcionRegistradaEvent` y `OcCerradaEvent` ya definidos como
contratos en `Compras.Domain.Ports.OrdenCompra/`.

**Nuevo:**

- [ ] Definir contratos faltantes en
      `Compras.Domain.Ports.OrdenCompra/`:
  - `OcDevolucionRegistradaEvent`
  - `FacturaProveedorRegistradaEvent` (con
    `lineasFacturadas : LineaFacturada[]`)
  - `NotaCreditoProveedorRegistradaEvent`
  - `PagoFacturaProveedorEvent`
- [ ] **Reemplazar el stub `InMemoryGenerarSolicitudCompraPort`**
      (hoy emite `OcBorradorStub`) por el handler real de
      `CrearOrdenCompraDesdeBifurcacionRqCommand`. La RQ emite
      `IGenerarSolicitudCompraPort.GenerarAsync(...)` al bifurcar; OC
      ahora crea una OC en Borrador real (heredando proveedor sugerido
      si lo hay, o quedándola sin proveedor para que el comprador
      complete en Fase 4 flow §4.1).
- [ ] Cleanup: deprecar tabla `compras.oc_borrador_stub` y migración
      de borrado tras dos releases con OC real funcionando.
- [ ] Listener `OcRecepcionRegistradaListener` (real, no stub):
      actualiza `CantidadRecibida` por línea, recalcula
      `SubEstadoRecepcion`, transiciona a `Cerrada` si las 3
      dimensiones cierran (idempotente: `cantidad_recibida` es un set
      al valor del payload, no incremento).
- [ ] Listener `FacturaProveedorRegistradaListener`: actualiza
      `CantidadFacturada` por línea, recalcula `SubEstadoFacturacion`.
- [ ] Listener `NotaCreditoProveedorRegistradaListener`: decrementa
      `CantidadFacturada`.
- [ ] Listener `PagoFacturaProveedorListener`: actualiza
      `SubEstadoPago`.
- [ ] Listener `OcDevolucionRegistradaListener` (stub interno hasta
      que Recepción exista): decrementa `CantidadRecibida`, recalcula
      `SubEstadoRecepcion`. Si la OC estaba en `Cerrada`, vuelve a
      `Autorizada` con sub-estado `Parcial`.
- [ ] Eventos emitidos: `OrdenCompraCerradaEvent` cuando las 3
      dimensiones llegan a Completa/Completa/Pagada (también
      satisface el contrato `OcCerradaEvent` ya consumido por RQ
      desde `OcCerradaListener`).
- [ ] Ampliar `CancelarOrdenCompraCommand` para soportar cancelación
      con recepciones parciales:
  - Requiere doble autorización (`compras.ordenes.cancelar.doble`
    + `.autorizar.nivel1` + `.autorizar.nivel2`).
  - Las cantidades ya recibidas permanecen en la OC.
  - Las RQs regresan al pool **solo por la cantidad no recibida**
    (libera parcial vía `LineaRqLiberadaEvent` con campo
    `cantidad_liberada`).
- [ ] Tests integration:
  - Ciclo completo: capturar → autorizar → recepción parcial →
    factura parcial → pago parcial → recepción completa → factura
    completa → pago completo → Cerrada (automático).
  - Cancelar con recepciones parciales: requiere doble auth, las RQs
    se liberan parcialmente.
  - Devolución: OC regresa de Cerrada a Autorizada con
    `SubEstadoRecepcion = Parcial`.
  - Idempotencia: aplicar `OcRecepcionRegistradaEvent` dos veces no
    incrementa dos veces.

**Criterio de aceptación:** los tres sub-estados avanzan
independientemente; el cierre automático funciona; la cancelación
con recepciones parciales libera RQs correctamente; las devoluciones
revierten estado.

---

### Fase 6 — PDF al proveedor + duplicar OC (M)

Generación de PDF institucional al autorizar, con agrupación por
artículo. Comando `DuplicarOrdenCompra` (C4 cerrado como cancelar +
recrear) que pre-llena una OC nueva desde una cancelada/rechazada.

**Heredado:** patrón evento → listener síncrono (autorizar dispara
PDF, similar a cómo RQ autorizar dispara bifurcación).

**Nuevo:**

- [ ] Definir puerto `IGenerarPdfOrdenCompraPort` en
      `Compras.Domain.Ports.Pdf/`.
- [ ] Implementación stub `LocalPdfOrdenCompraStub` que devuelve PDF
      placeholder con texto plano (sin librería gráfica todavía).
- [ ] Listener `OrdenCompraAutorizadaListener` (síncrono): llama
      `IGenerarPdfOrdenCompraPort.GenerarAsync(ocId)` y persiste el
      blob URL en `compras.orden_compra_pdf` (nueva tabla con FK 1:1
      a `ordenes_compra` + columna `blob_url`).
- [ ] Servicio `GenerarPdfOrdenCompraService` que:
  - Lee la OC con líneas y cabecera.
  - **Agrupa líneas por artículo** sumando cantidades (§3.bis.4 del
    diseño).
  - Oculta campos internos (`departamento_solicitante_id`,
    `requisicion_id`).
  - Renderiza el PDF con la librería elegida.
- [ ] **Decisión de librería PDF: QuestPDF** (cerrada — Rev. 2).
      Implementación real reemplaza el stub. NuGet `QuestPDF`,
      community edition (gratis para Millet por revenue).
- [ ] Endpoint `GET /api/v1/compras/ordenes/{id}/pdf` que devuelve el
      PDF (stream o redirect a blob storage).
- [ ] **Comando `DuplicarOrdenCompraCommand`** que pre-llena una OC
      nueva en `Borrador` desde una OC en estado terminal
      `Cancelada` o `Rechazada`:
  - Copia cabecera (proveedor, sucursal, moneda, condiciones de pago,
    uso principal, banderas, observaciones, info logística).
  - Copia líneas manuales (sin FK a RQ — las RQs fueron liberadas al
    cancelar; el comprador re-selecciona si quiere consolidar).
  - **No copia**: adjuntos (cotización debe ser nueva), autorizaciones,
    eventos, sub-estados.
  - Setea `oc_origen_id` para trazabilidad.
  - Emite `OrdenCompraDuplicadaEvent`.
  - Endpoint `POST .../ordenes/{ocOrigenId}/duplicar` con permiso
    `compras.ordenes.crear`.
- [ ] Query `ObtenerOrdenCompraOrigenQuery` para navegar desde una OC
      a su origen (si fue duplicada).
- [ ] Bandejas básicas necesarias (mis borradores, pendientes
      autorización, autorizadas pendientes recepción) — read models
      planos vía proyección EF, sin views todavía.
- [ ] Tests integration:
  - Autorizar OC → PDF generado y descargable.
  - Consolidada con 5 líneas, 3 del mismo artículo desde RQs
    distintas → PDF muestra 3 líneas (suma) en lugar de 5.
  - Cancelar OC autorizada → duplicar → nueva OC en Borrador con
    cabecera y líneas pre-llenadas, sin adjuntos, sin autorizaciones,
    `oc_origen_id` apuntando a la cancelada.
  - Duplicar una OC en estado `Borrador` (no terminal) → 422.

**Criterio de aceptación:** PDF se genera al autorizar; agrupación
por artículo se ve en el output; duplicación funciona end-to-end
preservando trazabilidad bidireccional vía `oc_origen_id`.

---

### Fase 7 — Reportes operativos (M)

Las tres vistas críticas del mapa funcional §8: partidas abiertas,
árbol de documentos, últimas 100 compras del material. Más bandejas
detalladas.

**Heredado:** patrón `Listar...Query` + `PagedResponse<T>` de RQ;
el frontend ya tiene patrón master-detail + filtros server-side.

**Nuevo:**

- [ ] Query `ListarPartidasAbiertasQuery` con filtros: estado,
      sub-estados, proveedor, comprador, fecha, contenedor, ruta,
      semana, importe, días atrasados (calculado contra
      `fecha_entrega_esperada`).
- [ ] Endpoint `GET /api/v1/compras/ordenes/partidas-abiertas` con
      permiso `compras.ordenes.reportes.partidas_abiertas`.
- [ ] **Evaluar performance contra dataset seed de 5k OCs:** si la
      query P95 > 500ms, promover a vista materializada
      `compras.vw_partidas_abiertas`. Decisión basada en benchmark
      real, no preventiva.
- [ ] Query `ObtenerArbolDocumentosQuery` que recibe un id de cualquier
      documento del ciclo (RQ, OC, Recepción, Factura, Pago) y
      construye el árbol bidireccional. **Promover como cross-módulo**
      desde el inicio: vive en `Compras.Application.Trazabilidad` y se
      diseña para que CxP, Recepción y Tesorería lo consuman cuando
      existan.
- [ ] Endpoint `GET /api/v1/compras/trazabilidad/arbol-documentos`
      con `?desde=oc|rq|recepcion|factura|pago&id=...`.
- [ ] Query `ListarUltimas100ComprasMaterialQuery` con filtros
      (proveedor, fecha, cantidad mínima, tipo de documento). Reusa
      la lógica existente en RQ (`ListarUltimasComprasMaterial` ya
      implementada para "historial de compras al seleccionar
      proveedor en RQ", si existe; si no, se construye aquí y se
      promueve).
- [ ] **Query `ObtenerHistoricoOrdenCompraQuery`** (brecha §14.1 del
      05 — confirmada P0): arma timeline cronológico leyendo
      `core.audit_log` filtrado por `entity_type='OrdenCompra'` y
      `entity_id`, agrupa eventos por tipo (creación, transmisión,
      autorizaciones, recepciones, facturas, pagos, cancelación,
      duplicación) y los devuelve ordenados. Endpoint
      `GET /api/v1/compras/ordenes/{id}/historico`. Alimenta tab
      "Historial" de P3 del frontend.
- [ ] **Query `ListarOcsHermanasDuplicadasQuery`** (brecha §14.2 del
      05): filtro `WHERE oc_origen_id = :ocId`. Endpoint
      `GET /api/v1/compras/ordenes/{id}/duplicadas`. Alimenta aside
      list de P3 con OCs hermanas.
- [ ] **Query `ObtenerKpisPartidasAbiertasQuery`** (brecha §14.4 del
      05 — FOC10 cerrado): 4 agregados (monto pendiente recibir,
      pendiente facturar, pendiente pago, count atrasadas) reactivos
      a los filtros de F7-PR1. Endpoint
      `GET /api/v1/compras/ordenes/partidas-abiertas/kpis`. Alimenta
      KPI cards arriba de P9 del frontend.
- [ ] Bandejas adicionales:
  - "OCs autorizadas pendientes de recepción" (filtra por estado +
    sub_recepcion).
  - "OCs recibidas pendientes de factura".
  - "OCs facturadas pendientes de pago".
  - "OCs canceladas o rechazadas" (auditoría).
- [ ] Reportes mensuales con filtros: servicios de mantenimiento
      mensual, servicios de transporte mensual (mismo endpoint
      `ListarOrdenesCompraQuery` con preset).
- [ ] Query `ListarOrdenesCompraDuplicadasQuery` (OCs con
      `oc_origen_id != NULL` agrupadas por comprador y período;
      KPI de uso de "cancelar + duplicar").
- [ ] Tests integration con dataset seed: P95 < 500ms en partidas
      abiertas y árbol de documentos.

**Criterio de aceptación:** las 3 vistas críticas funcionan con
performance aceptable; el árbol de documentos navega bidireccionalmente
desde cualquier nodo.

---

### Fase 8 — Eventos de integración + Notificaciones (S)

Conectar Outbox + Service Bus para que módulos externos consuman
eventos. Notificaciones por email cuando esos módulos existan.

**Heredado:** patrón Outbox de RQ (cuando se conecte; hoy `NoOp`).

**Nuevo:**

- [ ] Wireup real de `IIntegrationEventPublisher` (no `NoOp`) con
      Outbox + Service Bus (ADR 0009). Coordinar con el ticket de
      plataforma `<Outbox>`.
- [ ] Publicar eventos de integración cross-BC:
  - `compras.orden-compra.enviada-a-autorizacion.v1`
  - `compras.orden-compra.autorizada.v1`
  - `compras.orden-compra.rechazada.v1`
  - `compras.orden-compra.cancelada.v1`
  - `compras.orden-compra.cerrada.v1`
  - `compras.orden-compra.reabierta.v1`
- [ ] Versionar eventos (`.v1`) para evolución futura sin breaking.
- [ ] Coordinar con módulo Notificaciones (cuando exista) para
      crear plantillas de email:
  - "Tienes una OC pendiente de autorización" (a N1, luego a N2).
  - "Tu OC fue autorizada y enviada al proveedor" (al comprador).
  - "Tu OC fue rechazada con motivo X" (al comprador).
- [ ] Tests E2E con consumer dummy que verifica publicación y orden.
- [ ] Documentación de los eventos publicados (schemas) para
      consumidores externos.

**Criterio de aceptación:** los eventos llegan al Service Bus en dev;
los consumers (cuando existan) los reciben en orden; las plantillas
están definidas aunque el envío real esté pendiente.

---

### Fase 9 — Catálogos seed completos para OC (M)

Completar los catálogos seed que OC necesita y que **no existen** o
**vienen incompletos** del trabajo previo de Requisiciones. **No
incluye importación desde SAP ni pantallas de CRUD** (ambos diferidos
post-MVP — mapa funcional §10 "Decisiones resueltas").

‖ paralelizable: F9 puede ejecutarse en paralelo con F8 (eventos de
integración) ya que tocan capas distintas.

**Heredado:** patrón de tablas catálogo de RQ
(`compartido.proveedores`, `compartido.articulos`,
`compartido.sucursales`, `compartido.almacenes`, etc.) y patrón de
seed via `HasData` + `IHostedService` que se autoexcluye en
`Production`.

**Nuevo:**

- [ ] Tablas de catálogos compartidos en schema `compartido` que no
      existen todavía:
  - `compartido.condiciones_pago` (si no existe; revisar — algunas
    necesidades pueden estar cubiertas por RQ).
  - `compartido.incoterms` (nuevo — solo importaciones).
  - `compartido.transportistas` (nuevo).
  - `compartido.regimenes_fiscales` (nuevo o completar — coordinar
    con CxP y Contabilidad para validar tasas v1).
- [ ] Tabla `compras.tipos_documento_oc` con seed (cotizacion,
      ficha_tecnica, correo_autorizacion, pedimento,
      factura_proveedor_extranjero, packing_list, otro).
      *(Nota: esta tabla y su seed ya se crean en F2-PR4; F9 solo
      verifica que el seed esté completo para Production-like.)*
- [ ] Seeds versionados con `HasData` o `IHostedService` (se
      autoexcluye en `Production` igual que el patrón existente).
- [ ] Endpoints `GET /api/v1/catalogos/incoterms`,
      `/transportistas`, `/regimenes-fiscales`,
      `/condiciones-pago` (read-only, sin POST/PATCH/DELETE) con
      permiso `compartido.catalogos.leer`.
- [ ] Tests integration: cada endpoint devuelve la lista seed; un
      usuario sin el permiso recibe 403.

**Criterio de aceptación:** los catálogos requeridos por OC están
disponibles via endpoints `GET` read-only en dev y QA. Los flujos
end-to-end de captura de OC (Fases 2–7) operan contra los catálogos
seed sin caer a stubs hardcoded.

> **Diferido post-MVP (bloque separado, no parte de OC v1):**
> pantallas de CRUD para administración de catálogos + importación
> inicial desde SAP B1 (proveedores, artículos, condiciones de pago,
> regímenes fiscales) + migración de OCs históricas si el cliente la
> requiere. El modelo de OC soporta migración aditiva cuando se reabra
> (ver §11 del 01-diseño).

---

### Fase 10 — Hardening + UAT (M)

Lo que falta para release.

**Heredado:** patrón de hardening de RQ (Fase 8 de su plan).

**Nuevo:**

- [ ] Cobertura de tests integration > 80% del flujo principal.
- [ ] Idempotencia HTTP (ADR 0020) verificada en todos los POST de
      creación/transición.
- [ ] Problem Details en todas las rutas de error (ADR 0010).
- [ ] Observabilidad: traces distribuidos (Serilog → App Insights,
      ADR 0006). Verificar correlación entre evento de RQ (bifurcación)
      → creación de OC → autorización → PDF.
- [ ] Performance: medir P50/P95 de bandejas y partidas abiertas con
      dataset real (post-migración).
- [ ] Documentar OpenAPI con descripciones (ADR 0017).
- [ ] Runbook de operación: cómo cancelar OC con factura asociada
      (proceso manual de CxP primero), cómo duplicar, cómo manejar
      conflictos 409.
- [ ] UAT con grupo piloto del cliente (Rodrigo + 1 comprador + 1
      autorizador de Dirección). Casos de prueba: las 12 funcionalidades
      del §12 del 01-diseño.
- [ ] Verificar wireup real de `<AdjuntosManager>` con ADR 0024 (blob
      storage Azure) — reemplazar `LocalPdfOrdenCompraStub` y
      `IAlmacenarBlobPort` filesystem por implementaciones reales.

**Criterio de aceptación:** el cliente firma el UAT; los 12
indicadores de éxito del §12 del 01-diseño se cumplen.

---

### Fase 11 (post-v1) — Items diferidos

- **Cliente final destinatario (C5):** desbloquear cuando A+W
  confirme contrato de master y se cierren sub-decisiones (§10.4 del
  mapa funcional).
- **Multi-moneda con servicio T/C automático (C1):** integración con
  Banxico u otro servicio si el cliente la pide.
- **Contratos marco / OCs abiertas** para servicios recurrentes
  (mapa funcional §9.4).
- **Portal de proveedores** (cerrado como fuera de scope del MVP).
- **Bulk operations** (autorización masiva, edición masiva).
- **Promoción de componentes cross-módulo** (`<AdjuntosManager>`,
  `<ArbolDocumentos>`, `<SubEstadosBar>`) a `components/erp/` y
  `SharedKernel` cuando aparezca el segundo consumidor (CxP,
  Recepción).
- **Vista materializada de partidas abiertas** si Fase 7 mostró
  degradación de performance bajo carga real.

---

## 5. Riesgos y mitigaciones

| Riesgo | Probabilidad | Impacto | Mitigación |
|---|---|---|---|
| Almacén-Recepción / CxP / Tesorería tardan más de lo esperado | alta | alto | Stubs desde Fase 5; los listeners cross-BC operan in-process. El cierre automático de la OC funciona con stubs simulando recepciones, facturas y pagos. |
| Algún catálogo seed (regímenes fiscales, condiciones de pago) llega incompleto a Fase 3 | media | medio | Coordinar con CxP y Contabilidad la validación del seed inicial. Si llega tarde, los flujos básicos siguen operando con seed mínimo (16% IVA general) y se enriquecen en Fase 9. |
| Servicio PDF requiere más decisiones (formato, branding, firmas digitales) | media | medio | Iniciar evaluación de librería en Fase 5 para tener decisión cerrada al llegar a Fase 6. Stub placeholder mientras tanto. |
| Compromiso exclusivo de RQ (§3.bis.1) crea races bajo concurrencia | baja | medio | Capa 1 de concurrencia + UNIQUE parcial en BD (`comprometida_en_oc_id IS NULL` no es UNIQUE; el race se previene por la transacción + check de la columna). Test con dos POST simultáneos seleccionando la misma RQ. |
| "Cancelar + duplicar" (C4) se usa para evitar planificación | baja | medio | KPI mensual de OCs duplicadas por comprador (query F7-PR3). Revisión de adherencia al workflow si supera umbral. |
| Catálogo de regímenes fiscales (C3) incorrecto produce cálculos mal | media | alto | Validación contra catálogo SAT al cargar seed inicial; revisión con CxP y Contabilidad antes de Fase 3. |
| `cotizacion_excepcionada` se activa sin adjunto correo (C11) | baja | medio | Invariante en el agregado; UNIQUE en DB (`CHECK cotizacion_excepcionada = false OR EXISTS adjunto correo`). Si la validación es costosa, validar a nivel comando solamente. |
| Performance de partidas abiertas con sub-estados materializados (C7) | media | bajo | Benchmark en Fase 7; promover a vista materializada solo si P95 > 500ms. |
| Conflictos de versión optimista en bandeja del autorizador | baja | bajo | UX `<ConflictResolutionDialog>` ya existe (heredado de RQ); funciona out-of-the-box. |
| Cambios en el diseño durante implementación | media | medio | Diseño sellado (Rev. 0.2). Cambios se anotan como Rev. 0.3+ y se evalúan contra trabajo en curso. |

---

## 6. Sizing total y dependencias

| Fase | Sizing | Bloquea a |
|---|---|---|
| 0 — Foundation + permisos canónicos | S | Todas |
| 1 — Walking skeleton OC | S | 2 |
| 2 — Borrador completo (cabecera + líneas + adjuntos + logística) | M | 3 |
| 3 — Workflow de autorización | M | 4, 6 |
| 4 — Creación desde RQ + compromiso exclusivo | M | 5 |
| 5 — Sub-estados + listeners cross-BC + cierre automático | M | 6, 7 |
| 6 — PDF al proveedor + duplicar OC | M | 10 |
| 7 — Reportes operativos | M | 10 |
| 8 — Eventos de integración + Notificaciones | S | 10 (parcial) |
| 9 — Catálogos seed completos para OC | M | 10 (release) |
| 10 — Hardening + UAT | M | release v1 |
| 11 — Diferidos | — | post-v1 |

**Camino crítico hasta release v1:**
**0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 9 → 10 ≈ 4 a 5 meses con 2
desarrolladores backend**, asumiendo:

- Prerrequisitos confirmados (§2) — ya están.
- Servicio PDF elegido antes de Fase 6.
- Coordinación con Almacén-Recepción, CxP, Tesorería en paralelo
  (los módulos pueden empezar a leer los contratos desde Fase 5).
- Seeds de regímenes fiscales y condiciones de pago validados con CxP
  y Contabilidad antes de Fase 3.

Fase 8 (eventos de integración) se puede mover a paralelo de Fase 9
sin afectar el crítico, **siempre que** los consumidores reales
(Notificaciones, Almacén-Recepción, CxP) no estén listos para el go-live.

> **Comparación con RQ:** el plan de RQ es 0→9→8 ≈ 4-5 meses. OC sigue
> en la misma banda porque hereda casi todo (foundation S, no L), pero
> agrega 3 fases nuevas (sub-estados, PDF, reportes complejos) que
> compensan los meses ahorrados.

### 6.bis Camino crítico cruzado backend ↔ frontend

El release v1 de OC depende del **máximo** entre el plan backend
(este doc, 4–5 meses con 2 backend devs) y el plan frontend
([06](06-frontend-plan-implementacion.md), 3–4 meses con 1 dev UI).
Asumiendo arranque sincronizado y que cada fase UI espera su fase
backend correspondiente, el camino crítico real es el backend (es el
más largo).

**Implicaciones para el cronograma**:

- Si backend va en tiempo, frontend cierra ~3 semanas antes de
  release v1 — esas semanas son colchón para UAT.
- Si backend se atrasa (en particular **F7-PR3** que alimenta UF7
  con 5 queries), UF7 frontend se atrasa y empuja release v1.
- **F5-PR3 backend** (reemplazo del stub `OcBorradorStub`) es el
  punto crítico cross-BC del backend que ya bloquea UF5 frontend
  (cancelar + duplicar). Coordinación obligatoria.

**Riesgos específicos del cruce**: ver §5 de este doc (riesgo
"Backend OC tarda más de lo planeado") + §5 del 06 (mismo riesgo
desde la perspectiva de UI).

---

## 7. Decisiones operativas pendientes

- [ ] Confirmar que módulos Almacén-Recepción, CxP, Tesorería no van
      a estar listos antes que OC (caso típico) y por tanto los stubs
      son necesarios.
- [ ] Coordinar con el cliente la generación de los **seeds iniciales**
      de catálogos faltantes (incoterms, transportistas, regímenes
      fiscales, condiciones de pago) en Fase 0–3 — basta una hoja
      Excel o lista; **no se importa desde SAP en MVP**.
- [ ] Pedir al cliente la matriz de autorizadores concretos para OC
      (quién es Jefe de Compras y quién es Director en cada empresa,
      con suplencias).
- [ ] **Sincronización backend ↔ frontend** (camino crítico cruzado,
      §3.bis + §6.bis): coordinar agenda al inicio de cada fase con
      el dev UI para alinear los **10 checkpoints de alimentación**:
  - **F0 → UF0** (paralelizable): permisos canónicos.
  - **F1 → UF1**: walking skeleton.
  - **F2 → UF2 + UF3 parcial**: borrador completo.
  - **F3 → UF1 (lectura autorización) + UF4**: workflow.
  - **F4 → UF2 (Sheet + selector consolidación)**: creación desde RQ.
  - **F5-PR3 + F5-PR4 → UF5**: cancelar con recepciones + reemplazo
    stub (punto crítico cross-BC).
  - **F6-PR1+PR2 → UF5 (duplicar) + UF6 (PDF stub)**.
  - **F6-PR3 → UF6 (PDF real) + UF1 (bandejas mejoradas)**.
  - **F7-PR1+PR2 → UF7 (partidas abiertas + árbol docs)**.
  - **F7-PR3 → UF7 (historial + hermanas + KPIs) + UF1 (presets)** —
    el más sensible, alimenta 5 piezas de UI.
  - **F9 → UF3 (catálogos selectores) + UF6 (PDF real con catálogos)**.
- [ ] Validar con CxP y Contabilidad el catálogo de regímenes
      fiscales antes de Fase 3.

---

## 8. Cambios respecto a versiones previas

### Rev. 5 — sincronización backend↔frontend explícita (2026-05-11)

Tras cierre del 06-frontend Rev. 1, se documenta el camino crítico
cruzado entre planes:

- **§3 dependencias**: agregada fila "Frontend OC" + **§3.bis nueva**
  con tabla de alimentación (fase backend → fase frontend que consume).
- **§6.bis nueva**: explicación del camino crítico cruzado (release
  v1 = max(camino backend, camino frontend)). Backend es el más
  largo, frontend cierra ~3 semanas antes (colchón para UAT).
- **§7 decisiones operativas**: ítem de coordinación con frontend
  reemplazado por **10 checkpoints de sincronización** explícitos.
- F7-PR3 sigue M con nota de monitoreo (partir en 3a/3b solo si
  cruza 800 líneas netas).

### Rev. 4 — endpoints para frontend incluidos en F7 (2026-05-11)

Tras cierre del 05-frontend Rev. 2, las 3 brechas backend confirmadas
se incluyen en el plan ahora que el backend está en desarrollo activo
(no como tickets posteriores):

- **§4 Fase 0**: lista de permisos canónicos ampliada con
  `compras.ordenes.crear.sin_rq` (FOC11) — 10 permisos en total.
- **§4 Fase 7**: extendida con 3 queries/endpoints nuevos:
  - `ObtenerHistoricoOrdenCompraQuery` + `GET /ordenes/{id}/historico`
    (alimenta tab Historial de P3 frontend — brecha §14.1).
  - `ListarOcsHermanasDuplicadasQuery` + `GET /ordenes/{id}/duplicadas`
    (aside list de P3 — brecha §14.2).
  - `ObtenerKpisPartidasAbiertasQuery` + `GET /ordenes/partidas-abiertas/kpis`
    (KPI cards de P9 — brecha §14.4 / FOC10).
- §6 sizing — Fase 7 mantiene M (3 queries adicionales son lectura
  trivial sobre tablas existentes + audit_log).

### Rev. 3 — cierre de decisiones backend (2026-05-11)

Tras 3 rondas de decisiones con owner, se cierran las asunciones del
01-diseño:

- **C4 — Modificación post-autorización**: cerrada como **"cancelar
  + recrear"** (descartada reapertura). Fase 6 se reescribe como "PDF
  al proveedor + duplicar OC". El comando `DuplicarOrdenCompraCommand`
  reemplaza la reapertura. Eliminados entidad `VersionOC`, estado
  `EnReautorizacion`, tabla `compras.orden_compra_versiones`. F7
  ahora trackea OCs duplicadas (KPI) en lugar de versiones.
- **C7 sub-estados materializados, C9 RQ comprometida bloqueada,
  C10 validez proveedor en cada transición, C11 cotización en
  agregado**: confirmadas como ya estaban en el plan.
- **Librería PDF = QuestPDF**: decisión cerrada. F6-PR3 implementa
  directamente sin evaluación adicional.
- **Idempotency-key desde el inicio de cada PR**: F10-PR1 se reduce
  a auditoría de cobertura.
- **C1 multimoneda, C2 folio, C6 catálogo adjuntos**: confirmadas
  como ya estaban.
- §4 Fase 6 — reescrita.
- §5 — riesgo "Reapertura usada como atajo" → "Cancelar + duplicar
  usado como atajo".
- §6 — Fase 6 mantiene M; sizing sin cambio.
- §7 — eliminada "Elegir librería PDF".
- Permisos canónicos en §4 F0: eliminado `compras.ordenes.reabrir`
  (de 10 a 9 permisos).

### Rev. 2 — catálogos sin migración SAP en MVP (2026-05-11)

Correcciones tras feedback del owner: la migración inicial desde SAP
y las pantallas de CRUD de catálogos **no forman parte del scope de
OC v1**. MVP opera con seeds versionados + endpoints `GET` read-only.

- §1 — punto 6 reformulado para reflejar la política de catálogos del
  MVP; eliminado el ítem "Alcance de migración SAP" de los pendientes
  que no bloquean.
- §3 — fila de Datos Maestros explicita read-only en MVP y bloque
  post-MVP separado.
- §4 Fase 9 — **reescrita completamente**: de "Catálogos reales +
  migración SAP (L)" a "Catálogos seed completos para OC (M)". Quitadas
  las opciones A/B/C, el script de extracción T-SQL, el parseo
  heurístico de logística, la reconstrucción de adjuntos, la validación
  spot-check y el read-only de SAP. Se preserva solo el trabajo de
  catálogos seed que OC necesita.
- §5 — eliminado el riesgo de "Decisión §10.3 no se cierra a tiempo";
  reemplazado por riesgo de seeds incompletos.
- §6 — Fase 9 baja de L a M; camino crítico se mantiene en 4–5 meses
  (Fases 0–8 dominan).
- §7 — eliminada "Cerrar §10.3"; el export SAP de catálogos se
  reemplaza por la generación de seeds desde una lista/Excel del
  cliente.

### Rev. 1 — versión inicial (2026-05-11)

Plan inicial basado en el diseño Rev. 0.2 y el audit del backend
contra `main` (cierre de Requisiciones + UF8-PR1 soft-lock real
mergeado). 11 fases productivas + post-v1 diferidos. Camino crítico
estimado 4–5 meses con 2 backend devs, equivalente a Requisiciones.
