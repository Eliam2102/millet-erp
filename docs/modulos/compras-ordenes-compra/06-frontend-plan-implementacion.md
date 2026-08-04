# Plan de implementación de frontend — Submódulo Órdenes de Compra (Compras)

> **Construido sobre:** [05-frontend-diseno.md](05-frontend-diseno.md) (Rev. 2).
> Granularidad de PRs en [07-frontend-pr-breakdown.md](07-frontend-pr-breakdown.md)
> y la base del módulo cerrado en backend
> ([01-diseno.md](01-diseno.md) Rev. 0.5,
> [02-plan-implementacion.md](02-plan-implementacion.md) Rev. 4,
> [03-pr-breakdown.md](03-pr-breakdown.md) Rev. 4,
> [04-cuidados-infra.md](04-cuidados-infra.md) Rev. 2).
>
> **Hereda contexto de:** [`docs/modulos/compras-requisiciones/06-frontend-plan-implementacion.md`](../compras-requisiciones/06-frontend-plan-implementacion.md)
> (Rev. 6). La Fase 0 ya está cerrada por RQ (cliente HTTP, UX kit,
> ConflictDialog, useCollaboration con hub real, soft-lock UF8-PR1
> mergeado). OC arranca con la plataforma del módulo madura.
>
> **Estado:** propuesta de plan para revisión con el owner. Sizing en
> bandas (XS/S/M/L/XL) — calibrar contra capacidad real del equipo de
> UI. Sigue el mismo formato que el plan de backend de OC.
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
- Cada fase produce algo **deployable y validable**.
- Las fases son secuenciales por dependencia técnica, pero dentro de
  cada fase hay paralelismo posible (anotado como "‖").
- Las dependencias hacia el backend están marcadas en negrita
  (**dep backend §X del 05**) y se resuelven con tickets contra ese
  equipo. Para OC, **todas las brechas están incluidas en el plan
  backend** (no requieren tickets externos — ver §14 del 05).

---

## 1. Resumen ejecutivo

**Objetivo:** entregar v1 de la UI del submódulo Órdenes de Compra
del módulo Compras del nuevo ERP, alineado con el diseño aprobado y
contra la API real implementada por el equipo backend en F1–F10.

**Estrategia:**

1. **Reutilización masiva del shell del módulo Compras** ya construido
   por RQ. El cliente HTTP enriquecido, ConflictResolutionDialog,
   useCollaboration (con hub real), UX kit, EstadoBadge, MoneyDisplay,
   permission codes y patrones master-detail + Sheet + inline forms
   **ya viven en `main`**. La Fase 0 de OC es liviana — extensiones
   puntuales (no foundation completa como en RQ).
2. **Read-only antes de mutación**: bandeja + detalle se entregan
   read-only en Fase 1. Esto valida shape de DTOs reales contra el
   código backend y desbloquea la mayor parte de pantallas.
3. **Captura iterativa**: Sheet básico (UF2) → información
   complementaria (UF3) → workflow (UF4) → cancelar/duplicar (UF5) →
   PDF + bandejas pendientes (UF6).
4. **Reportes al final** (UF7): partidas abiertas, árbol de documentos,
   historial. Estas pantallas dependen de endpoints backend que se
   entregan en F7-PR3 del 03-PR (alineado en Rev. 4 del 02-plan).
5. **Promoción cross-módulo desde el inicio**: `<AdjuntosManager>`
   y `<ArbolDocumentos>` viven en `components/erp/` desde su primer
   PR (FOC2 + FOC6 cerrados — CxP, Recepción, Activos los consumirán).

**Pendientes que NO bloquean arranque pero SÍ bloquean release v1:**

- Ninguno crítico. Las decisiones backend (C4, librería PDF, etc.) y
  frontend (FOC1–FOC16) están todas cerradas. Las 3 brechas backend
  (§14.1 historial, §14.2 OCs hermanas, §14.4 KPIs partidas abiertas)
  están incluidas en F7-PR3 del backend → llegan junto con UF7.

**Pendientes que NO bloquean arranque:**

- Codegen ADR-0017 (`api-types.ts` desde OpenAPI). UI OC mantiene
  DTOs mirroreados manualmente en `features/compras/ordenes/api/types.ts`
  hasta que plataforma cierre el ticket. Cuando llegue, sustitución
  trivial.

**Pendientes que SÍ bloquean fases específicas:**

- ~~`<ProveedorComboBox>` cross-módulo~~ — **VERIFICADO 2026-05-11**:
  `<ProveedorSelector>` ya existe en
  [`components/erp/selectors/ProveedorSelector.tsx`](../../../frontend/src/components/erp/selectors/ProveedorSelector.tsx)
  (cross-módulo desde RQ). OC lo reusa tal cual. UF2 agrega solo
  `<HistorialComprasProveedor>` como componente complementario que se
  renderiza al lado cuando se elige proveedor.
- **Endpoints backend de OC** (todos los de F1–F10 del 03 del backend).
  La UI arranca cuando F1 esté en main; cada fase de UI espera que la
  fase backend correspondiente esté lista.

---

## 2. Prerrequisitos — audit del repo (2026-05-11)

Auditado contra
[c:\Users\UserSP\Desktop\Project_Millet_ERP\frontend\](../../../frontend/)
y la salida de RQ. Estado real:

| Prerrequisito | Estado | Detalle / plan B |
|---|---|---|
| React 19 + TypeScript 6 + Vite 8 | ✅ existe | sin cambio |
| Tailwind v4 + shadcn/ui | ✅ existe | RQ ya copió primitives (Dialog, Select, Command, Form, Toast, Calendar, Card, Badge, Skeleton, Alert) |
| TanStack Router + Query | ✅ wireado | sin cambio |
| MSAL Entra ID | ✅ wireado | sin cambio |
| **Cliente HTTP enriquecido** (`apiRequest`, `ApiError`, ProblemDetails, ETag, Idempotency, retry 409) | ✅ existe (UF0-PR1 de RQ) | OC lo reusa tal cual |
| **`useFormIdempotencyKey()`** + `applyServerErrors()` | ✅ existen | sin cambio |
| **Permisos canónicos frontend** | ✅ los 10 de RQ + 9 de compartido en `permission-codes.ts` | OC extiende con 10 de `compras.ordenes.*` (FOC11 cerrado: 10º es `crear.sin_rq`) |
| **UX kit `components/erp/feedback/`** (EmptyState, ErrorState, TableSkeleton, DomainTermTooltip) | ✅ existe (UF0-PR2 de RQ) | OC lo reusa |
| **`<Breadcrumbs>`, `useUnsavedChangesGuard`** | ✅ existen | sin cambio |
| **`<ConflictResolutionDialog>` con preserve-form-state** | ✅ existe (UF0-PR2 de RQ) | OC lo reusa para todas las mutaciones |
| **`useCollaboration` real con hub** | ✅ wireado (UF8-PR1 de RQ mergeado) | OC declara `OrdenCompra` en lista de entidades con soft lock |
| **`<EstadoBadge>`** | ✅ existe (genérico parametrizado) | OC extiende con 7 enums propios |
| **`<MoneyDisplay>`, `<DateTimeDisplay>`** | ✅ existen | sin cambio |
| **`<MotivoRechazoSelector>` (de RQ)** | ✅ existe | OC reusa; backend extiende seed con `aplicaA = OrdenCompra` bitmask |
| **Sidebar `nav.ts` con grupo "Compras"** | ✅ activo con item "Requisiciones" | OC agrega item "Órdenes de compra" |
| **`<CollaborationIndicator>`** | ✅ existe (activo con hub real) | OC lo wirea para `orden-compra` |
| **`features/compras/`** carpeta del módulo | ✅ existe (subcarpeta `requisiciones/`) | OC crea subcarpeta `ordenes/` y `trazabilidad/` (cross-módulo) |
| **`features/compras/ordenes/`** | ❌ no existe | UF0 crea estructura |
| **`components/erp/adjuntos/`** | ❌ no existe | UF3 lo crea (cross-módulo desde el inicio — FOC2) |
| **`components/erp/trazabilidad/`** | ❌ no existe | UF7 lo crea (cross-módulo desde el inicio — FOC6) |
| **`<ProveedorSelector>` cross-módulo** | ✅ existe en `components/erp/selectors/ProveedorSelector.tsx` (heredado de RQ) | OC lo reusa tal cual. Sin trabajo adicional. |
| **8 selectores cross-módulo más** (Articulo, Almacen, Departamento, Sucursal, Usuario, CatalogoEagerCombobox) | ✅ existen en `components/erp/selectors/` | OC los consume tal cual donde aplique. |
| **`api-types.ts` codegen** | ❌ no existe (deuda heredada) | OC sigue con DTOs mirror; cuando plataforma cierre, sustitución trivial |
| **Endpoints backend de OC (F1–F10)** | ⏳ depende del avance del backend | Cada fase de UI espera la fase backend correspondiente. UF1 espera F1-F3 backend (CRUD básico + workflow). |

**Conclusión del audit:** **plataforma del módulo Compras madura**.
La Fase 0 de OC se reduce a extensiones puntuales (permission codes +
estructura de carpetas + extensión EstadoBadge). Sin bloqueantes para
arrancar UF0.

> **Tracking de deuda de plataforma** (ADR-0031): heredado de RQ.
> Sin nuevas deudas que OC introduzca; las existentes (codegen, etc.)
> ya tienen sus tickets.

---

## 3. Dependencias con otros equipos / módulos

| Equipo | Necesidad | Estado | Plan |
|---|---|---|---|
| **Backend OC** | endpoints estables de F1–F10 del 03-PR backend | ⏳ en desarrollo | UF1 arranca cuando F1-F3 estén en main. Cada fase de UI espera su fase backend correspondiente. |
| **Backend Compras-RQ** | endpoint `GET /requisiciones?disponibles_para_consolidar&sucursalId=...` (UF2 selector consolidación) | ✅ derivable de F4-PR1 backend de OC (ALTER `requisiciones` con `comprometida_en_oc_id`) | usar tal cual cuando F4 esté en main |
| **Backend Identidad** | nuevos roles para autorizadores N1/N2 de OC (Jefe Compras, Director) | ✅ derivable del seed inicial de F0-PR1 del backend OC | UI lee los roles existentes |
| **Backend Catálogos** | catálogos seed de Incoterms, Transportistas, Regímenes fiscales, Condiciones de pago | ⏳ disponibles tras F9-PR1 backend OC (catálogos seed) | UF3 (información logística/importación) espera F9 backend |
| **Plataforma — Codegen ADR-0017** | `api-types.ts` desde OpenAPI | ⏳ pendiente | UF1 mirroring manual; sustitución trivial cuando llegue |
| **Plataforma — `CollaborationHub`** | hub real para soft locks | ✅ wireado por UF8-PR1 de RQ | sin acción adicional |
| **UX / Diseño** | revisión de mockups de pantallas OC (especialmente P3 detalle, P9 partidas abiertas, P10 árbol documentos) | pendiente | sesiones de design review al cierre de UF1, UF3 y UF7 |
| **Cliente (Rodrigo + Director)** | UAT del flujo OC end-to-end | pendiente | sesión inicial antes de UF0; UAT al cierre de UF8 |

---

## 4. Fases

### Fase 0 — Foundation OC UI (S)

Plumbing específico de OC. **Sin features visibles al usuario final**.

‖ paralelizable: este PR puede ejecutarse en paralelo con UF1 backend
de OC.

- [ ] Agregar 10 permisos canónicos `compras.ordenes.*` al mirror
      [permission-codes.ts](../../../frontend/src/lib/auth/permission-codes.ts)
      (FOC11 cerrado: incluye `crear.sin_rq` como 10º).
- [ ] Crear estructura de carpetas:
  - `features/compras/ordenes/{api,components,lib,pages,routes}/`
  - `components/erp/adjuntos/` (con `.gitkeep`, se popula en UF3)
  - `components/erp/trazabilidad/` (con `.gitkeep`, se popula en UF7)
- [ ] **Extender `<EstadoBadge>`** con 7 enums de OC (Borrador,
      EnAutorizacionJefeCompras, EnAutorizacionDireccion, Autorizada,
      Cerrada, Cancelada, Rechazada) con colores y tooltips de glosario.
      Verificar contraste WCAG AA (4.5:1).
- [ ] **Extender `glosario.ts`** con términos OC (sub-estado, partida
      abierta, consolidación, duplicar OC, cotización excepcionada,
      OC origen, contenedor, ruta, semana, pedimento, incoterm).
- [ ] **Activar item "Órdenes de compra"** en
      [nav.ts](../../../frontend/src/lib/nav.ts) bajo grupo Compras,
      gateado por `compras.ordenes.leer`. Ruta placeholder
      `routes/_app/compras/ordenes/index.tsx` que redirige a `/compras/ordenes` (bandeja, vacía hasta UF1).
- [ ] **Search contextual del topbar** (brecha §14.5 del 05 — incluida
      en MVP): el topbar global ya reconoce ruta; agregar reconocimiento
      de `/compras/ordenes/...` y pasar `entityType=orden-compra` al
      endpoint de search. Backend ya tiene índices (§10.3 del 01-diseño).

**Criterio de aceptación:** click en "Órdenes de compra" del sidebar
lleva a un placeholder vacío. `<EstadoBadge state="Autorizada" />`
renderiza con color correcto. Tooltips de glosario activos. Search
contextual desde `/compras/ordenes/...` pasa contexto al backend.

**Sin riesgo de romper main**: cambios aditivos.

---

### Fase 1 — Read-only de OC (M)

Bandeja general (P1) + detalle (P3 read-only). Antes de cualquier
mutación, validamos los DTOs reales del backend.

> **Dependencia backend**: F1 + F2 + F3 backend OC en main (walking
> skeleton + borrador completo + workflow autorización).

‖ paralelizable: un dev arranca P1, otro P3.

- [ ] **Mirror de DTOs** en `features/compras/ordenes/api/types.ts`:
  - `OrdenCompraListItemResponse`, `OrdenCompraDetalleResponse`,
    `LineaOrdenCompraResponse`, `AdjuntoOcResponse`,
    `AutorizacionOcResponse`, `InformacionLogisticaResponse`,
    `InformacionImportacionResponse`, `TotalesOCResponse`.
  - `PagedResponse<T>`.
  - Enums: `EstadoOrdenCompra` (7), `SubEstadoRecepcion`,
    `SubEstadoFacturacion`, `SubEstadoPago`, `NivelAutorizacion`,
    `ResultadoAutorizacion`, `DescuentoTipo`, `TipoDocumentoOc`.
- [ ] **Hooks de read** en `features/compras/ordenes/api/`:
  - `useOrdenesCompra(filtros)` — bandeja con offset+limit + filtros.
    Query key estructurada.
  - `useOrdenCompra(id)` — detalle, extrae `etag`.
  - `useRequisicionDeLinea(lineaId)` — para mostrar info de RQ origen
    si la línea viene de una RQ.
- [ ] **`<SubEstadosBar>`** (FOC1 cerrado) — 3 barras horizontales
      segmentadas (Recepción / Facturación / Pago) con segmentos por
      línea + colores + patrón + números visibles + tooltip. WCAG AA
      verificado. Vive en `features/compras/ordenes/components/`.
- [ ] **`<StepperAutorizacionOc>`** (FOC9 cerrado) — stepper visual
      ⚪ Borrador → 🟡 N1 → 🟡 Dirección → 🟢 Autorizada. Tooltips con
      nombre del autorizador.
- [ ] **P1 — Bandeja general** (`routes/_app/compras/ordenes/index.tsx`):
  - **Search params Zod-validados** (FOC12): estado, sub-estados,
    proveedorId, comprador, fechaDesde/Hasta, contenedor, ruta,
    semana, q, offset, limit.
  - Tabla con columnas: Folio, Fecha, Proveedor, Comprador, Líneas
    count, Total, Estado, Sub-estados (3 badges compactos), Acciones.
  - **Presets** (chips): Mis borradores, Pendientes recepción,
    Pendientes factura, Pendientes pago, Cerradas (mes), Canceladas/Rechazadas.
  - Paginación offset (50 default, máx 200).
  - Estados loading/empty/error con `<TableSkeleton>` /
    `<EmptyState>` (CTA "Nueva OC" gateado por `.crear`) /
    `<ErrorState>`.
  - Click en fila → P3.
  - Breadcrumbs: `Compras / Órdenes de compra`.
- [ ] **P3 — Detalle (read-only)** (`routes/_app/compras/ordenes/$id.tsx`):
  - Layout master-detail con aside list (RQs upstream + OCs hermanas
    si duplicada — UF7 la activa cuando llegue endpoint backend).
  - Sub-topbar con folio + ReferenciaProveedor + `<EstadoBadge>`.
  - `<SubEstadosBar>` arriba.
  - Tabs: Líneas | Información | Adjuntos | Autorización | Historial.
  - **Tab Líneas**: tabla densa con `<NaturalezaBadge>` heredado de
    artículo (FOC15 cerrado), badge "Desde RQ-..." con enlace si
    aplica, cantidades, precios, subtotal, sub-estado por línea.
  - **Tab Información**: 3 sub-tabs anidados (FOC8 cerrado) —
    Logística / Importación (oculto si no aplica) / Financiera. Cada
    uno **read-only** en esta fase; edición inline llega en UF3.
  - **Tab Adjuntos**: lista de adjuntos read-only (sin upload todavía).
  - **Tab Autorización**: `<StepperAutorizacionOc>` + tabla de
    autorizaciones registradas.
  - **Tab Historial**: stub con autorizaciones del detalle (UF7
    amplía con endpoint `/historico`).
  - `<CollaborationIndicator>` activo (hub real).
  - 403/404 con páginas dedicadas.
  - Breadcrumbs: `Compras / Órdenes de compra / <folio>`.

**Criterio de aceptación:** un usuario con permiso entra a
`/compras/ordenes`, ve OCs paginadas, abre el detalle de una.
Cualquier discrepancia entre DTO real y mirror se detecta acá
(TypeScript falla en build).

**Riesgo:** medio. Primera pantalla con datos reales de OC backend;
si el shape difiere del mirror, se ajusta en el mismo PR.

---

### Fase 2 — Captura básica (M)

Sheet "Nueva OC" + editor de líneas básico. Activa el flow del
capturador hasta listo-para-info-complementaria.

> **Dependencia backend**: F4 backend OC en main (creación desde RQ
> + consolidación + compromiso exclusivo).

‖ paralelizable: el dev que cierra UF1 sigue con el Sheet; otro
arranca el editor de líneas.

- [ ] **Reusar `<ProveedorSelector>`** existente en
      `components/erp/selectors/` (heredado de RQ — verificado
      2026-05-11). Sin trabajo de creación/promoción.
- [ ] **`<HistorialComprasProveedor>`** (componente nuevo,
      complementario al selector) en `features/compras/ordenes/components/`.
      Se renderiza al lado del `<ProveedorSelector>` en el Sheet de
      Nueva OC; cuando hay proveedor seleccionado, llama
      `useUltimas100ComprasMaterial({ proveedorId })` y muestra tabla
      compacta de últimas compras. Promover a `components/erp/` solo
      si CxP lo necesita.
- [ ] **`<SelectorRequisicionesConsolidacion>`** (FOC4 cerrado) en
      `features/compras/ordenes/components/`. Modal multi-select con
      filtros server-side (departamento, requisitante, fecha, búsqueda
      por folio). Restricción sucursal aplicada automáticamente
      (input read-only).
- [ ] **Schemas Zod** (`features/compras/ordenes/schemas/`):
  - `crear-orden-compra-desde-requisicion.ts`
  - `crear-orden-compra-vacia.ts`
  - `agregar-linea-desde-requisicion.ts`
  - `agregar-linea-manual.ts`
  - `actualizar-linea.ts`
  - `actualizar-cabecera.ts`
- [ ] **Hooks de mutación**:
  - `useCrearOrdenCompraDesdeRequisicion()` — POST con
    `Idempotency-Key` (decisión: desde el inicio de cada PR).
  - `useCrearOrdenCompraVacia()` — POST.
  - `useAgregarLineaDesdeRequisicion()` — POST.
  - `useAgregarLineaManual()` — POST.
  - `useActualizarLinea()` — PATCH (sin Idempotency-Key, ver §7.6
    del 05 de RQ).
  - `useEliminarLinea()` — DELETE.
  - `useActualizarCabecera()` — PATCH.
- [ ] **P4 — Sheet "Nueva OC"** (`routes/_app/compras/ordenes/nueva.tsx`
      o overlay sobre P1):
  - **3 modos** (FOC3 cerrado): detecta según entry point. Toggle
    explícito visible para cambiar entre 1:1, Consolidación, Sin RQ
    previa (este último gateado por `compras.ordenes.crear.sin_rq`
    — FOC11).
  - Form: Proveedor (combobox), Sucursal, Almacén destino default,
    Moneda + Tipo cambio (si ≠ MXN), Condiciones de pago, Uso
    principal, Bandera `EsImportacion`, Observaciones.
  - **Modo Consolidación**: botón "Agregar requisiciones" → abre
    P5 (selector multi-select). Lista de RQs seleccionadas con
    preview de líneas.
  - **Modo Sin RQ previa**: motivo obligatorio + adjuntar correo
    autorización (componente simple en este PR; AdjuntosManager
    completo en UF3).
  - **Draft protection**: `useUnsavedChangesGuard` + borrador en
    localStorage con key
    `compras:oc:draft:nueva:<userId>:<empresaId>`.
  - Submit → redirige a P3 con OC en `Borrador`.
  - Breadcrumbs: `Compras / Órdenes de compra / Nueva`.
- [ ] **P6 (editor de líneas embebido en P3)** — read-only en F1
      ahora habilita add/edit/delete inline en `Borrador`/`Rechazada`:
  - Borde dashed primary (agregar) / amber (editar) — patrón exemplar.
  - Líneas desde RQ: cantidad/artículo bloqueados; precio editable.
  - Líneas manuales (solo si `SinRQ` = true): todo editable.
  - Recalcular totales en tiempo real al cambiar precio o cantidad.
  - Múltiples líneas mismo artículo desde RQs distintas: entries
    separadas (C8) con tooltip.
  - Tabla con add/edit/delete inline; cancelación con confirm.

**Criterio de aceptación:** un comprador crea OC desde una RQ
autorizada (modo 1:1). Otro comprador consolida 3 RQs de la misma
sucursal en una OC. Captura líneas, edita, elimina. Las OCs aparecen
en P1 en estado `Borrador`. **Sin transmitir todavía** — eso es UF4.

**Riesgo:** medio. P4 Sheet con 3 modos es la pieza más compleja
de UI nueva. UF7-PR4 (selector consolidación) es separable si
acelera entrega.

---

### Fase 3 — Información complementaria + Adjuntos (M)

Tabs Logística / Importación / Financiera con edición inline. Manager
completo de adjuntos.

> **Dependencia backend**: F2 backend OC + F9-PR1 backend OC en main
> (catálogos seed: Incoterms, Transportistas, Regímenes fiscales,
> Condiciones de pago).

- [ ] **Catálogos cross-módulo** (componentes selectores):
  - `<IncotermSelector>`, `<TransportistaSelector>`,
    `<CondicionesPagoSelector>`, `<RegimenFiscalSelector>` — viven
    en `components/erp/selectors/` (cross-módulo: CxP los consumirá).
    Combobox simple con cache de 1h (catálogos read-only).
- [ ] **`<InformacionLogisticaForm>`** (FOC8) — form con dirección,
      transportista (combobox o texto libre), número guía,
      instrucciones envío. Edición inline con border dashed amber.
      Editable hasta `Autorizada` inclusive (logística post-aut sin
      re-auth — §4.6 del 01-diseño).
- [ ] **`<InformacionImportacionForm>`** (condicional `EsImportacion`):
      incoterm, país origen, contenedor, ruta, código ruta, semana
      embarque, pedimento. **`NumeroPedimento` editable post-aut**
      sin re-auth (los demás bloqueados post-aut).
- [ ] **`<TotalesFinancierosForm>`**: descuento global, gastos
      adicionales, redondeo. Cálculo de totales en tiempo real del
      backend (POST de update devuelve `TotalesOCResponse` recalculado).
- [ ] **`<AdjuntosManager>`** (FOC2 cerrado — **promovido cross-módulo
      desde el inicio**) en `components/erp/adjuntos/`:
  - Drag-and-drop + file picker fallback (accesibilidad WCAG).
  - Lista de adjuntos con miniatura (PDFs primera página, imágenes
    thumbnail) — usar `<embed>` nativo para PDFs (FOC5).
  - Selector tipo de documento (`<TipoDocumentoOcSelector>`).
  - Upload con progress bar (XHR + onUploadProgress).
  - Validaciones: ≤ 20MB por archivo, MIME types whitelist.
  - **Diseñar con props parametrizadas** para no acoplar a OC: prop
    `endpoint`, `tipos`, `obligatorios`. CxP/Activos lo consumirán
    con sus propios endpoints.
  - Tests unitarios + snapshot.
- [ ] Hooks de mutación:
  - `useActualizarInformacionLogistica()` — PATCH.
  - `useActualizarInformacionImportacion()` — PATCH.
  - `useActualizarTotalesFinancieros()` — PATCH.
  - `useUploadFile(endpoint)` — generic helper en `lib/hooks/`.
  - `useAdjuntarDocumento()` — wrapper de `useUploadFile` para
    endpoint específico de OC.
  - `useRemoverAdjunto()` — DELETE (solo en Borrador).
- [ ] **Wirear los 3 sub-tabs** en P3 Tab "Información" + Tab
      "Adjuntos" con `<AdjuntosManager>`.

**Criterio de aceptación:** un comprador captura información logística
completa (transportista, guía, instrucciones). Para una OC de
importación, captura contenedor, ruta, semana. Adjunta cotización
+ ficha técnica (importación). Todos los catálogos seed cargan
correctamente.

**Riesgo:** medio (multipart upload + promoción cross-módulo del
AdjuntosManager).

---

### Fase 4 — Workflow autorización (M)

Transmitir + N1 + N2 + Rechazar + P2 bandeja pendientes.

> **Dependencia backend**: F3 backend OC en main (autorización
> completa) — ya cumplida si UF1 está en main.

‖ paralelizable: bandeja P2 y acciones N1/N2 pueden ir en paralelo.

- [ ] **Hooks**:
  - `useEnviarAAutorizacion()` — POST.
  - `useAutorizarOrdenCompra()` — POST con `{ Nivel, Resultado, Notas? }`.
  - `useRechazarOrdenCompra()` — POST con `{ Nivel, MotivoId, MotivoTexto? }`.
  - `usePendientesAutorizacionOc(nivel)` — GET filtrado.
- [ ] **Schemas Zod**: `enviar-a-autorizacion.ts`, `autorizar.ts`,
      `rechazar.ts`.
- [ ] **P2 — Bandeja pendientes de autorización**
      (`routes/_app/compras/ordenes/pendientes-autorizacion.tsx`):
  - Filtro automático por permiso (N1 → `EnAutorizacionJefeCompras`;
    N2 → `EnAutorizacionDireccion`; ambos → tab switcher).
  - Columnas: Folio, Proveedor, Comprador, Líneas, Total, Días
    esperando, "Ver detalle" (botón único).
  - **Sin acciones inline de aprobar/rechazar (FOC16 cerrado)**: la
    fila solo navega al detalle. Anti-firmas-sin-revisar.
  - Breadcrumbs.
- [ ] **Acciones en P3 detalle**:
  - Botón **"Transmitir"** en `Borrador` (gateado por `crear`,
    deshabilitado si invariantes pre-auth fallan con tooltip).
  - Botón **"Aprobar Nivel1"** en `EnAutorizacionJefeCompras`
    (gateado por `autorizar.nivel1`).
  - Botón **"Aprobar Nivel2"** en `EnAutorizacionDireccion`
    (gateado por `autorizar.nivel2`).
  - Botón **"Rechazar"** en cualquier `EnAutorizacion*` (gateado por
    `autorizar.nivelX` correspondiente).
  - Cada acción → confirm dialog estándar (Aprobar muestra resumen
    "Vas a aprobar como Jefe de Compras la OC ..."; Rechazar abre
    modal P7 motivo).
- [ ] **P7 — Modal de motivos** (reusa `<MotivoRechazoSelector>` de
      RQ con filtro `aplicaA = OrdenCompra`).
- [ ] Manejo de 409 → `<ConflictResolutionDialog>` (heredado).

**Criterio de aceptación:** comprador transmite. Jefe Compras (N1)
ve en su bandeja, abre detalle, aprueba. OC pasa a
`EnAutorizacionDireccion`. Director (N2) ve en su bandeja, aprueba.
OC pasa a `Autorizada`. PDF se genera (validación visual en UF6).
Rechazo en cualquier nivel funciona.

**Riesgo:** medio. Bandeja del autorizador es UX crítica.

---

### Fase 5 — Cancelar y duplicar (M)

Cancelar 1 firma + cancelar doble firma (con recepciones) + duplicar
OC (C4 cerrado).

> **Dependencia backend**: F5-PR4 backend OC en main (cancelar con
> recepciones parciales) + F6-PR2 backend (duplicar).

- [ ] **Hooks**:
  - `useCancelarOrdenCompra()` — POST con `{ MotivoId, MotivoTexto? }`
    (1 firma).
  - `useCancelarOrdenCompraConRecepciones()` — POST con
    `{ MotivoId, MotivoTexto?, UsuarioAutorizadorN1Id, UsuarioAutorizadorN2Id }`
    (doble firma).
  - `useDuplicarOrdenCompra()` — POST `/ordenes/{ocOrigenId}/duplicar`.
- [ ] **Schemas Zod**: `cancelar.ts`, `cancelar-doble-firma.ts`,
      `duplicar.ts`.
- [ ] **Acción "Cancelar" en P3**:
  - **Visible en `Borrador` / `EnAutorizacion*` / `Rechazada`**: abre
    P7 con `aplicaA = Cancelacion` (1 firma).
  - **Visible en `Autorizada` sin recepciones**: abre P7 (1 firma).
  - **Visible en `Autorizada` con recepciones parciales** (gateado
    por `cancelar.doble`): abre dialog especial con **doble firma**.
- [ ] **`<DobleFirmaDialog>`** (componente nuevo):
  - Stepper de 3 pasos: (1) motivo, (2) firma N1 con usuario+notas,
    (3) firma N2 con usuario+notas (debe ser distinto del N1).
  - Validator del payload: verifica usuarios distintos, permisos
    correspondientes.
  - Submit → mutation con feedback de cada paso.
- [ ] **Acción "Duplicar OC" en P3**:
  - Visible solo en `Cancelada` / `Rechazada` (gateado por `crear`).
  - Abre **`<ConfirmDuplicarDialog>`** (FOC7 cerrado): preview de qué
    se copia (cabecera + líneas, sin adjuntos/autorizaciones/RQs).
  - Confirm → POST → redirige a P3 de la nueva OC en `Borrador` con
    `oc_origen_id` apuntando a la origen.
- [ ] **Aside list de P3** ampliado: si la OC tiene `oc_origen_id`,
      mostrar enlace "Origen: OC-..."; si la OC es origen de
      duplicaciones, mostrar lista de hermanas (consume endpoint
      `/duplicadas` de F7-PR3 backend — verificar disponibilidad).

**Criterio de aceptación:** cancelar OC en Borrador con 1 firma OK.
Cancelar OC autorizada con recepciones parciales requiere 2
usuarios distintos con permisos correctos. Duplicar desde una OC
cancelada produce nueva OC en Borrador con cabecera y líneas,
sin adjuntos ni autorizaciones, vinculada via `oc_origen_id`.

**Riesgo:** medio (DobleFirmaDialog es UX nueva; edge cases de
permiso).

---

### Fase 6 — PDF + bandejas pendientes adicionales (S)

PDF embedded + bandejas filtradas predefinidas adicionales.

> **Dependencia backend**: F6-PR1 backend OC en main (PDF generation
> con stub) + F6-PR3 backend (PDF real con QuestPDF).

- [ ] **Hook** `usePdfOrdenCompra(id)` — devuelve URL del blob (no
      carga el PDF). El componente lo embebe.
- [ ] **Tab "PDF" en P3**:
  - `<embed>` nativo apuntando a la URL del blob (FOC5 cerrado).
  - Botón "Descargar" arriba.
  - Si la OC no está autorizada todavía, banner "PDF disponible
    después de la autorización Nivel 2".
- [ ] **Presets de bandejas adicionales en P1** (refinamiento de
      UF1):
  - "OCs autorizadas pendientes de recepción"
  - "OCs recibidas pendientes de factura"
  - "OCs facturadas pendientes de pago"
  - "OCs canceladas o rechazadas" (auditoría)
  - "Mis duplicadas" (filtro `oc_origen_id != null AND comprador_titular = me`)
- [ ] **Hover state visual** en filas de P1 mostrando sub-estados
      compactos con tooltip detallado al hover (mejora UX para
      compradores que escanean bandeja).

**Criterio de aceptación:** un comprador autoriza OC; el PDF aparece
embebido en P3 tab "PDF". Descarga funciona. Los 5 presets de
bandeja devuelven OCs correctas.

**Riesgo:** bajo (PDF nativo del browser, presets sobre query
existente).

---

### Fase 7 — Reportes operativos (M)

Partidas abiertas + Árbol documentos + Historial completo + Últimas
100 compras del material.

> **Dependencia backend crítica**: **F7-PR3 backend OC en main**
> (queries `ObtenerHistoricoOrdenCompraQuery`,
> `ListarOcsHermanasDuplicadasQuery`, `ObtenerKpisPartidasAbiertasQuery`,
> `ListarPartidasAbiertasQuery`, `ListarUltimas100ComprasMaterialQuery`,
> `ObtenerArbolDocumentosQuery`). Esta es la fase con mayor dependencia
> backend.

- [ ] **Hooks de read**:
  - `usePartidasAbiertas(filtros)` — bandeja de la vista crítica.
  - `useKpisPartidasAbiertas(filtros)` — 4 agregados reactivos.
  - `useArbolDocumentos(tipo, id)` — vista grafo.
  - `useHistoricoOrdenCompra(id)` — timeline completo (alimenta tab
    Historial).
  - `useUltimas100ComprasMaterial(articuloId, filtros)`.
  - `useOcsHermanasDuplicadas(ocOrigenId)`.
- [ ] **`<KpiCardsPartidasAbiertas>`** (FOC10 cerrado) — 4 cards
      arriba de la tabla: monto pendiente recibir, pendiente facturar,
      pendiente pago, count atrasadas. Reactivas a filtros aplicados.
- [ ] **`<DiasAtrasadosBadge>`** — verde/amarillo/rojo según umbral
      contra `fecha_entrega_esperada`. Con ícono adicional para
      accesibilidad color-blind.
- [ ] **P9 — Partidas abiertas**
      (`routes/_app/compras/ordenes/partidas-abiertas.tsx`):
  - KPI cards arriba.
  - Tabla densa con filtros sticky lateral (estado, sub-estados,
    proveedor, comprador, contenedor, ruta, semana, importe).
  - Columna calculada de días atrasados con `<DiasAtrasadosBadge>`.
  - Permiso: `compras.ordenes.reportes.partidas_abiertas`.
- [ ] **`<ArbolDocumentos>`** (FOC6 cerrado — **promovido cross-módulo
      desde el inicio**) en `components/erp/trazabilidad/`:
  - Vista grafo bidireccional (RQ ← OC ← Recepción ← Factura ← Pago).
  - Cada nodo con folio + fecha + monto + click navega al detalle.
  - Layout responsive (vertical en mobile, horizontal en desktop).
  - Props parametrizadas (`tipoDocumento`, `id`) — sin acoplar a OC.
- [ ] **P10 — Árbol documentos** (`routes/_app/compras/trazabilidad/oc/$id.tsx`):
      wrapper de `<ArbolDocumentos>` para OC. Cuando CxP/Recepción/
      Tesorería existan, agregar wrappers análogos.
- [ ] **Tab "Historial" en P3 ampliado**: reemplazar stub de UF1 por
      timeline completo con todos los eventos
      (`useHistoricoOrdenCompra`).
- [ ] **P11 — Últimas 100 compras del material**
      (`routes/_app/compras/articulos/$id/historial-compras.tsx`):
      tabla con filtros (proveedor, fecha, cantidad mínima, tipo doc).
- [ ] **Aside list de P3 con OCs hermanas** (UF5 dejó pendiente
      esto): wirear con `useOcsHermanasDuplicadas`.

**Criterio de aceptación:** reporte de partidas abiertas con KPI
cards funciona y filtros reactivos. Árbol de documentos navega
bidireccionalmente. Tab Historial muestra timeline cronológico
completo desde creación hasta pago. P11 reporte de últimas 100
compras útil para comprar nuevamente un mismo material.

**Riesgo:** medio. La fase con más componentes nuevos y endpoints
backend dependientes.

---

### Fase 8 — Hardening v1 + UAT (M)

Lo que falta para release.

- [ ] **Tests con Vitest + React Testing Library**:
  - Componentes ERP nuevos: `<SubEstadosBar>`,
    `<StepperAutorizacionOc>`, `<KpiCardsPartidasAbiertas>`,
    `<DiasAtrasadosBadge>`, `<ConfirmDuplicarDialog>`,
    `<DobleFirmaDialog>`, `<AdjuntosManager>`, `<ArbolDocumentos>`.
  - `acciones-disponibles.ts` parametrizado contra tabla §6.1 del 05
    (20 acciones × 7 estados).
  - Hooks de mutación con MSW.
  - Coverage objetivo: > 70% de `features/compras/ordenes/`.
- [ ] **E2E con Playwright** (4 flujos críticos):
  1. Crear OC desde RQ → autorizar N1 → autorizar N2 → PDF generado.
  2. Crear OC consolidada N:1 → transmitir → rechazo N1 → editar →
     re-transmitir → aprobar.
  3. Cancelar OC con recepciones parciales (doble firma).
  4. Cancelar + Duplicar OC → modificar líneas → autorizar.
- [ ] **Accessibility audit con axe-core**:
  - Pasar axe en cada pantalla.
  - Validar contraste de `<SubEstadosBar>`, `<EstadoBadge>` (OC), y
    `<DiasAtrasadosBadge>` ≥ 4.5:1.
  - Tab order razonable en P3 con tabs.
  - `<ArbolDocumentos>` navegable por teclado.
- [ ] **Performance**:
  - P1 bandeja con 5k OCs seed: P95 render < 1s.
  - P9 partidas abiertas con 5k OCs activas: P95 query < 500ms.
  - Lighthouse ≥ 90.
- [ ] **Documentación de patrones** en `frontend/docs/patrones-compras.md`:
      sección "Patrones de UI de OC" con master-detail + Sheet con
      modos + AdjuntosManager.
- [ ] **Página de ayuda** ampliada en `/compras/ayuda` o
      `/compras/ordenes/ayuda`: glosario de términos OC, diagrama
      del ciclo de vida (7 estados + 3 sub-estados), FAQ.
- [ ] **Stylesheet de impresión** para P3 detalle de OC: `@media print`
      que oculta tabs/sidebar/topbar/acciones, formatea cabecera +
      líneas + información + autorización en A4 portrait.
- [ ] **UAT con grupo piloto** del cliente (Rodrigo + 1 comprador
      adicional + 1 Director).

**Criterio de aceptación:** UAT firmado. Tests verde en CI. Sin
issues bloqueantes de accesibilidad. Listo para release.

**Riesgo:** medio. UAT puede revelar ajustes UX no anticipados,
especialmente en flujos de duplicación y doble firma.

---

### Fase 9 (post-v1) — Items diferidos

- **Cliente final destinatario (C5)** cuando Fase 2 se desbloquee.
- **Bulk operations** (autorización masiva, edición masiva).
- **Servicio T/C automático Banxico** (C1 alternativo).
- **Contratos marco / OCs abiertas** para servicios recurrentes.
- **Portal de proveedores** (fuera de scope cerrado).
- **`react-pdf` viewer** si surge necesidad de anotar PDFs.
- **Vista materializada** de partidas abiertas si UF8 muestra
  degradación bajo carga real.
- **Codegen ADR-0017** cuando plataforma cierre — sustituir DTOs
  mirror.
- **CRUD de catálogos** (cuando módulo Datos Maestros lo entregue
  post-MVP).
- **Importación inicial desde SAP** (cuando bloque post-MVP llegue).
- **Notificaciones in-app** (badge en topbar con contador) — depende
  de extensión del módulo Notificaciones.

---

## 5. Riesgos y mitigaciones

| Riesgo | Probabilidad | Impacto | Mitigación |
|---|---|---|---|
| Backend OC tarda más de lo planeado | media | alto | UI espera por fase. Si F1-F3 backend están en main, UI puede arrancar. Si no, UF0 se ejecuta como adelantado (sin dependencia). |
| Shape de DTOs reales difiere del mirror manual | media | medio | UF1 lo cataliza: TypeScript falla en compile y se ajusta en mismo PR. Codegen ADR-0017 cuando llegue elimina el riesgo. |
| `<AdjuntosManager>` promovido cross-módulo agrega complejidad | media | bajo | Diseñar con props parametrizadas (`endpoint`, `tipos`, `obligatorios`). Tests con mock backend genérico. Refactor a futuro si CxP requiere variaciones inesperadas. |
| `<DobleFirmaDialog>` UX confusa en UAT | media | medio | Sesión de design review al cierre de UF5 con Director (consumidor primario). Iterar si feedback negativo. |
| Promoción cross-módulo de `<ArbolDocumentos>` resulta sobre-ingeniería si solo OC lo consume | baja | bajo | Si CxP/Recepción tardan > 6 meses, mantener en `features/compras/ordenes/` y mover después. Decisión a 6 meses post-release. |
| Brechas backend §14.1/§14.2/§14.4 no llegan en F7-PR3 | baja | medio | Tickets ya incluidos en plan backend (Rev. 4 del 02-plan + 03-PR). Si se atrasan, UF7 cierra con tab Historial reducido y aside list sin hermanas. |
| Mobile P2 no se prueba hasta UF8 | media | bajo | Patrón responsive heredado de RQ (UF7-PR3 mobile cards). Validar al cierre de UF4. |
| Performance de P9 partidas abiertas con dataset real | media | medio | Backend ya tiene índice combinado (`ix_oc_partidas_abiertas`). UF8 mide y promueve vista materializada si degrada. |
| Diferencias de adopción del flujo "cancelar + duplicar" entre compradores | media | bajo | KPI mensual de duplicaciones por comprador (P11 reporte ya cubre). Revisión post-release. |
| Cambios de UX durante UAT que requieren rework | alta | medio | Sesiones de design review con cliente al cierre de UF1, UF3 y UF7. No esperar a UF8. |

---

## 6. Sizing total y dependencias

Conteo de PRs por fase coherente con
[07-frontend-pr-breakdown.md](07-frontend-pr-breakdown.md) (cuando
se produzca).

| Fase | PRs estimados | Sizing fase | Bloquea a | Depende de |
|---|---|---|---|---|
| UF0 — Foundation OC UI | 1 | S | Todas | — |
| UF1 — Read-only de OC | 2 | M | UF2, UF4 | UF0, F1-F3 backend |
| UF2 — Captura básica | 3 | M-L | UF3, UF4 | UF1, F4 backend |
| UF3 — Información + Adjuntos | 2 | M | UF4 (parcial) | UF2, F9 backend (catálogos seed) |
| UF4 — Workflow autorización | 2 | M | UF5, UF6 | UF1 (lectura), F3 backend |
| UF5 — Cancelar + Duplicar | 2 | M | UF6 | UF4, F5-PR4 + F6-PR2 backend |
| UF6 — PDF + bandejas adicionales | 1 | S | UF7 (parcial) | UF4, F6 backend |
| UF7 — Reportes operativos | 3 | M | UF8 | UF1, **F7-PR3 backend** |
| UF8 — Hardening + UAT | 2 | M | release v1 | todas las anteriores |
| UF9 — Diferidos | — | — | — | post-v1 |
| **Total v1** | **18** | | | |

**Camino crítico hasta release v1:**
**UF0 → UF1 → UF2 → UF3 → UF4 → UF5 → UF6 → UF7 → UF8 ≈
3 a 4 meses con 1 dev UI + 1 revisor**, asumiendo:

- Backend OC entrega sus fases en sincronía con UI (F1-F3 antes de
  UF1, F4 antes de UF2, F9 antes de UF3, F5+F6 antes de UF5/UF6,
  F7-PR3 antes de UF7).
- `<ProveedorSelector>` cross-módulo ya existe (verificado).
- Sin sorpresas grandes en UAT.

> **Comparación con RQ:** plan de RQ es UF0–UF8 ≈ 2-3 meses con 16 PRs.
> OC es 3-4 meses con 18 PRs porque agrega 4 sub-fases nuevas
> (información complementaria UF3, cancelar+duplicar UF5,
> PDF+bandejas UF6, reportes UF7). Pero hereda toda la foundation
> del shell — UF0 es S, no M.

---

## 7. Decisiones operativas pendientes

**Cerradas en sesiones de validación 2026-05-11** (Rev. 2 del 05):

- [x] FOC1 — sub-estados como 3 barras horizontales segmentadas.
- [x] FOC2 — `<AdjuntosManager>` promovido a `components/erp/adjuntos/` desde el inicio.
- [x] FOC3 — Sheet único con 3 modos detectados por entry point.
- [x] FOC4 — Selector RQs modal con multi-select + filtros server-side.
- [x] FOC5 — PDF `<embed>` nativo del browser.
- [x] FOC6 — `<ArbolDocumentos>` promovido a `components/erp/trazabilidad/`.
- [x] FOC7 — ConfirmDuplicarDialog con preview.
- [x] FOC8 — 3 sub-tabs Logística / Importación / Financiera.
- [x] FOC9 — Stepper visual N1 → N2.
- [x] FOC10 — KPI cards en partidas abiertas + endpoint backend dedicado.
- [x] FOC11 — Permiso especial `compras.ordenes.crear.sin_rq` (10º).
- [x] FOC12 — Filtros en URL search params Zod.
- [x] FOC13 — EstadoBadge extender con 7 estados.
- [x] FOC14 — Sin real-time updates en bandeja.
- [x] FOC15 — NaturalezaBadge heredado en líneas con RQ origen.
- [x] FOC16 — P2 sin acciones inline (siempre obligar abrir detalle).
- [x] §14.1, §14.2, §14.4, §14.5 brechas backend incluidas en F7-PR3
      backend (no requieren tickets externos).

**Pendientes de coordinación inmediata:**

- [x] **VERIFICADO 2026-05-11**: `<ProveedorSelector>` ya existe en
      `components/erp/selectors/` cross-módulo (heredado de RQ). OC
      lo reusa tal cual. **+8 selectores adicionales** ya promovidos:
      ArticuloSelector, AlmacenSelector, DepartamentoSelector,
      SucursalSelector, UsuarioSelector, CatalogoEagerCombobox. OC
      consume todos sin trabajo de creación.
- [ ] Sesión de design review con UX/cliente al cierre de UF1
      (validar P1+P3 read-only).
- [ ] Sesión de design review tras UF3 (información logística +
      adjuntos manager).
- [ ] Sesión de design review tras UF7 (reportes — partidas abiertas
      y árbol documentos).
- [ ] Coordinar timing de fases backend ↔ frontend con dev backend
      para evitar bloqueos.

**Comunicación al cliente antes de release v1:**

- [ ] El flujo de modificación post-autorización es "Cancelar +
      Duplicar" (decisión C4 cerrada). Comunicar a compradores y
      autorizadores en sesión de UAT para que entiendan el flujo.
- [ ] La doble firma para cancelar con recepciones parciales requiere
      N1 y N2 distintos en la misma sesión. Comunicar el flujo
      operativo (Rodrigo + Director firman juntos).

---

## 8. Cambios respecto a versiones previas

### Rev. 2 — selectores cross-módulo verificados (2026-05-11)

Auditado el repo: **9 selectores cross-módulo ya existen** heredados
de RQ en `components/erp/selectors/` (`ProveedorSelector`,
`ArticuloSelector`, `AlmacenSelector`, `DepartamentoSelector`,
`SucursalSelector`, `UsuarioSelector`, `CatalogoEagerCombobox`).
OC los reusa todos.

- §1 ítem "pendientes que SÍ bloquean fases específicas":
  ~~`<ProveedorComboBox>` cross-módulo~~ tachado, ya existe.
- §2 audit: fila actualizada con `<ProveedorSelector>` y 8 selectores
  más confirmados como existentes.
- §4 UF2: ~~`<ProveedorComboBox>`~~ reemplazado por reusar selector
  existente + agregar `<HistorialComprasProveedor>` como componente
  complementario (no es selector).
- §6 sizing: nota sobre `<ProveedorSelector>` actualizada.
- §7 decisiones operativas: ítem de verificación marcado como [x]
  con hallazgo: 9 selectores ya heredados, 4 selectores nuevos a
  crear en UF3 (Incoterm, Transportista, CondicionesPago,
  RegimenFiscal — alineados con F9-PR1 backend).

### Rev. 1 — versión inicial (2026-05-11)

Plan inicial basado en el diseño de UI Rev. 2 y todos los docs
backend Rev. 4/0.5/2.

**Filosofía**: reuso máximo del shell del módulo Compras cerrado
por RQ. UF0 de OC es S (no M como en RQ) porque toda la foundation
ya existe en `main`. Las 4 sub-fases nuevas (UF3, UF5, UF6, UF7)
cubren las áreas funcionales propias de OC (información complementaria,
cancelar+duplicar, PDF+bandejas adicionales, reportes operativos).

9 fases productivas + post-v1 diferidos. Camino crítico estimado
**3–4 meses con 1 dev UI + 1 revisor**.

Todas las 16 asunciones FOC del 05 cerradas en sesión 2026-05-11
(Rev. 2 del 05). Todas las brechas backend §14 incluidas en plan
backend F7-PR3 (no requieren tickets externos).
