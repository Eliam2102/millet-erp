# PR Breakdown de frontend — Submódulo Órdenes de Compra (Compras)

> **Construido sobre:** [05-frontend-diseno.md](05-frontend-diseno.md) (Rev. 3)
> y [06-frontend-plan-implementacion.md](06-frontend-plan-implementacion.md) (Rev. 2).
>
> **Hereda contexto de:** [`docs/modulos/compras-requisiciones/07-frontend-pr-breakdown.md`](../compras-requisiciones/07-frontend-pr-breakdown.md)
> (Rev. 6). El shell, cliente HTTP enriquecido, UX kit, ConflictDialog
> con preserve-form-state, useCollaboration con hub real, y 9 selectores
> cross-módulo (incluyendo ProveedorSelector) están en `main` desde
> RQ. OC arranca con plataforma madura.
>
> **Estado:** Rev. 1 — propuesta inicial. Sigue las mismas convenciones
> que el breakdown de backend ([03-pr-breakdown.md](03-pr-breakdown.md) Rev. 5).
>
> **Fecha:** 2026-05-11.

---

## 0. Cómo leer

- Cada fila es **un PR**. ID `UF<fase>-PR<n>` (UF = "UI Frontend")
  secuencial dentro de la fase.
- **Tamaños**: XS (≤ 200 líneas netas), S (200–500), M (500–800).
  Techo absoluto: 800. Por encima, partir.
- **Riesgo de romper main**: bajo / medio / alto. Cada PR debe dejar
  `main` verde y desplegable; el riesgo se refiere al *blast radius*
  si se cuela un bug a `main`.
- **Branch naming**: `compras/oc-uf<fase>-<slug-corto>` (ej.
  `compras/oc-uf2-sheet-nueva-oc`). Prefijo `oc-` para distinguir
  de RQ.
- **Dependencias**: PRs previos que deben estar mergeados + fase
  backend requerida en main.
- Al final de cada fase hay una nota de **paralelización**.

> Convención: PR mergeable = build verde (`tsc -b` + `eslint`) +
> tests pasando (Vitest) + revisión de 1 dev + screenshot/clip de la
> pantalla afectada en el body del PR (cuando aplique).

> **Reuso primero (regla del proyecto):** cada PR indica qué pieza
> hereda del shell o de RQ. Componentes con potencial cross-módulo
> se promueven a `components/erp/` desde su primer PR (FOC2, FOC6
> cerrados).

---

## Fase 0 — Foundation OC UI (S)

1 PR consolidado: extensiones puntuales del shell (toda la plataforma
ya existe heredada de RQ).

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF0-PR1 | `compras/oc-uf0-foundation-permisos-glosario` | **Plumbing del submódulo OC**. (a) **Permisos canónicos**: agregar 10 constantes `compras.ordenes.*` (incluye `crear.sin_rq` por FOC11) a [permission-codes.ts](../../../frontend/src/lib/auth/permission-codes.ts). (b) **Estructura de carpetas**: crear `features/compras/ordenes/{api,components,lib,pages,routes}/` + `components/erp/adjuntos/.gitkeep` + `components/erp/trazabilidad/.gitkeep`. (c) **Extender `<EstadoBadge>`**: agregar 7 enums de OC (Borrador, EnAutorizacionJefeCompras, EnAutorizacionDireccion, Autorizada, Cerrada, Cancelada, Rechazada) con colores y tooltips de glosario. Verificar contraste WCAG AA. (d) **Extender `glosario.ts`** con términos OC (sub-estado, partida abierta, consolidación, duplicar OC, cotización excepcionada, OC origen, contenedor, ruta, semana, pedimento, incoterm). (e) **Activar sidebar**: item "Órdenes de compra" en `nav.ts` bajo grupo Compras, gateado por `compras.ordenes.leer`. Ruta placeholder `routes/_app/compras/ordenes/index.tsx`. (f) **Search global contextual del topbar**: extender para reconocer ruta `/compras/ordenes/...` y pasar `entityType=orden-compra` al endpoint (brecha §14.5 incluida en MVP). Tests por cada extensión. | `frontend/src/lib/auth/permission-codes.ts`, `frontend/src/features/compras/ordenes/` (estructura), `frontend/src/components/erp/{adjuntos,trazabilidad}/.gitkeep`, `frontend/src/components/erp/display/EstadoBadge.tsx` (extensión), `frontend/src/features/compras/lib/glosario.ts` (extensión), `frontend/src/lib/nav.ts` (extensión), `frontend/src/routes/_app/compras/ordenes/index.tsx`, `frontend/src/components/layout/Topbar.tsx` (extensión search) | (cierre de RQ en main: UF0–UF8) | S | bajo (cambios aditivos) | Click "Órdenes de compra" del sidebar muestra placeholder vacío sin error. `<EstadoBadge state="Autorizada" />` renderiza con color y tooltip. Search contextual desde `/compras/ordenes/...` pasa `entityType` al backend. Tests pasan. |

**Paralelización Fase 0**: 1 PR. Paralelizable con F1-F3 backend.

---

## Fase 1 — Read-only de OC (M)

Bandeja general + detalle read-only. Valida shape de DTOs reales.

> **Dependencia backend**: F1 + F2 + F3 backend OC en main (walking
> skeleton + borrador completo + workflow autorización).

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF1-PR1 | `compras/oc-uf1-types-display-hooks-read` | **Pieza de lectura completa para OC**. (a) **DTOs mirror** en `features/compras/ordenes/api/types.ts`: `OrdenCompraListItemResponse`, `OrdenCompraDetalleResponse`, `LineaOrdenCompraResponse`, `AdjuntoOcResponse`, `AutorizacionOcResponse`, `InformacionLogisticaResponse`, `InformacionImportacionResponse`, `TotalesOCResponse`, `PagedResponse<T>`, enums (`EstadoOrdenCompra`, `SubEstadoRecepcion`, `SubEstadoFacturacion`, `SubEstadoPago`, `NivelAutorizacion`, `ResultadoAutorizacion`, `DescuentoTipo`, `TipoDocumentoOc`). (b) **Display components** específicos de OC en `features/compras/ordenes/components/`: `<SubEstadosBar>` (FOC1 — 3 barras horizontales segmentadas con colores + patrón + números visibles + tooltip), `<StepperAutorizacionOc>` (FOC9 — stepper visual N1 → N2). Verificación de contraste WCAG AA documentada en el body. (c) **Hooks de read** en `features/compras/ordenes/api/`: `useOrdenesCompra(filtros)` (query key estructurada), `useOrdenCompra(id)` (extrae `etag` en `query.meta`). Tests con MSW: cada hook con respuesta exitosa, 404, 403. Snapshots por componente. | `frontend/src/features/compras/ordenes/api/{types,useOrdenesCompra,useOrdenCompra}.ts` + tests, `frontend/src/features/compras/ordenes/components/{SubEstadosBar,StepperAutorizacionOc}.tsx` + tests | UF0-PR1 | **M** | bajo | Tests pasan. `useOrdenCompra` expone `etag`. `<SubEstadosBar>` renderiza con 3 barras coherentes según `subEstados` mockeados. `<StepperAutorizacionOc>` resalta paso actual. |
| UF1-PR2 | `compras/oc-uf1-pantallas-readonly` | **Pantallas P1 + P3 read-only** — valida shape de DTOs reales contra el backend de OC. (a) **P1 — Bandeja general** con **search params Zod-validados** (FOC12: estado, sub-estados, proveedorId, comprador, fechaDesde/Hasta, contenedor, ruta, semana, q, offset, limit). Tabla con columnas Folio/Fecha/Proveedor/Comprador/Líneas/Total/Estado/Sub-estados/Acciones. Presets (chips clickeables): Mis borradores, Pendientes recepción, Pendientes factura, Pendientes pago, Cerradas mes, Canceladas/Rechazadas. Paginación offset (50/200). Estados loading/empty/error con `<TableSkeleton>` / `<EmptyState>` (CTA "Nueva OC" gateado por `.crear`) / `<ErrorState>`. Breadcrumbs `Compras / Órdenes de compra`. (b) **P3 — Detalle read-only**: layout master-detail con aside list (RQs upstream); sub-topbar con folio + ReferenciaProveedor + `<EstadoBadge>`; `<SubEstadosBar>` arriba; tabs Líneas / Información / Adjuntos / Autorización / Historial (este último stub con autorizaciones del detalle — UF7 amplía). Tab Líneas con `<NaturalezaBadge>` heredado (FOC15) + badge "Desde RQ-..." con enlace si aplica. Tab Información con 3 sub-tabs (FOC8): Logística/Importación/Financiera (read-only en esta fase). `<CollaborationIndicator>` activo. 403/404 con páginas dedicadas. Breadcrumbs `Compras / Órdenes de compra / <folio>`. | `frontend/src/routes/_app/compras/ordenes/{index,$id}.tsx`, `frontend/src/features/compras/ordenes/pages/{BandejaOrdenesCompra,DetalleOrdenCompra}.tsx`, `frontend/src/features/compras/ordenes/components/{FiltrosBandejaOc,CabeceraOrdenCompra,ListaLineasOc,TabsInformacion,TabAdjuntosReadOnly,TabAutorizacionStub,TabHistorialStub}.tsx`, `frontend/src/features/compras/ordenes/lib/bandeja-oc-search-schema.ts` | UF1-PR1, **F1-F3 backend en main** | **M** | medio (primeras pantallas con datos reales — si DTO no encaja, TS falla en build y se ajusta aquí) | Bandeja pagina, filtros se reflejan en URL, presets funcionan. Detalle muestra cabecera + sub-estados + tabs. 403/404 con páginas amigables. Estados empty/loading/error funcionan. |

**Paralelización Fase 1**: UF1-PR1 → UF1-PR2 secuencial.

---

## Fase 2 — Captura básica (M-L)

3 PRs: Sheet wizard; selector consolidación (aislado por riesgo); editor de líneas.

> **Dependencia backend**: F4 backend OC en main (creación desde RQ
> + consolidación + compromiso exclusivo).

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF2-PR1 | `compras/oc-uf2-sheet-nueva-oc-basico` | **P4 — Sheet "Nueva OC" con 3 modos** (FOC3). (a) **Componente Sheet** detecta modo según entry point: (i) desde bandeja RQ "Convertir" → modo 1:1; (ii) desde "Nueva OC" → modo Consolidación o vacía; (iii) toggle "Sin RQ previa" gateado por `compras.ordenes.crear.sin_rq` (FOC11). (b) **Reusar `<ProveedorSelector>` existente** + agregar `<HistorialComprasProveedor>` (componente complementario en `features/compras/ordenes/components/` — muestra últimas 100 compras al elegir proveedor; consume `useUltimas100ComprasMaterial` mockeado hasta F7 backend). (c) **Reusar `<SucursalSelector>`, `<AlmacenSelector>`** existentes. (d) **Form**: Proveedor, Sucursal, Almacén destino, Moneda + Tipo cambio (si ≠ MXN), Condiciones de pago (combobox simple hasta UF3), Uso principal, Bandera `EsImportacion`, Observaciones. (e) **Hooks de mutación**: `useCrearOrdenCompraDesdeRequisicion()` (Idempotency-Key), `useCrearOrdenCompraVacia()` (Idempotency-Key). (f) **Schemas Zod** en `features/compras/ordenes/schemas/`. (g) **Draft protection**: `useUnsavedChangesGuard` + localStorage key `compras:oc:draft:nueva:<userId>:<empresaId>`. (h) Modo "Sin RQ previa": motivo obligatorio + adjuntar correo (upload simple por ahora; AdjuntosManager completo en UF3). Submit → redirige a P3 con OC en `Borrador`. Breadcrumbs. | `frontend/src/routes/_app/compras/ordenes/nueva.tsx` (o overlay sobre P1), `frontend/src/features/compras/ordenes/pages/SheetNuevaOC.tsx`, `frontend/src/features/compras/ordenes/components/{HistorialComprasProveedor,FormCabeceraOC,UploadCorreoAutorizacion}.tsx`, `frontend/src/features/compras/ordenes/api/{useCrearOrdenCompraDesdeRequisicion,useCrearOrdenCompraVacia}.ts`, `frontend/src/features/compras/ordenes/schemas/{crear-oc-desde-rq,crear-oc-vacia}.ts`, `frontend/src/features/compras/ordenes/lib/draft-storage.ts` | UF1-PR2, **F4 backend en main** | **M** | medio (primera mutación con idempotency en OC; Sheet con 3 modos detectados es UX nueva) | Crear OC desde RQ funciona (modo 1:1). Crear OC vacía funciona. Draft se recupera tras refresh. Toggle "Sin RQ previa" oculto sin permiso. |
| UF2-PR2 | `compras/oc-uf2-selector-consolidacion` | **P5 — Selector de RQs para consolidación** (FOC4). Modal `<SelectorRequisicionesConsolidacion>` invocado desde Sheet P4 (modo Consolidación). Multi-select con tabla de RQs disponibles. Filtros server-side (departamento, requisitante, fecha, búsqueda por folio). **Restricción de sucursal aplicada automáticamente** (input read-only — toma sucursal del Sheet). Hook `useRequisicionesDisponibles(sucursalId)`. Expandir fila → preview de líneas. Submit → cierra modal, agrega RQs al payload del Sheet. **PR aislado por riesgo: restricción de sucursal + multi-select + UX nueva.** Tests E2E del flujo: abrir Sheet en modo Consolidación, seleccionar 3 RQs, confirmar → líneas aparecen en payload. Tests negativos: RQs de otra sucursal NO aparecen. | `frontend/src/features/compras/ordenes/components/SelectorRequisicionesConsolidacion.tsx`, `frontend/src/features/compras/ordenes/api/useRequisicionesDisponibles.ts`, tests E2E con MSW | UF2-PR1 | **S-M** | **alto** (restricción de sucursal es invariante crítica del backend §10.5; UI debe respetarlo sin permitir bypass) | RQs aparecen filtradas por sucursal. Tests E2E verifican restricción. Preview de líneas funciona. |
| UF2-PR3 | `compras/oc-uf2-editor-lineas-y-acciones` | **P6 — Editor de líneas embebido en P3** + matriz `acciones-disponibles.ts`. En `Borrador`/`Rechazada`: tabla editable inline con add/edit/delete. Borde dashed primary (agregar) / amber (editar) — patrón exemplar. Líneas desde RQ: cantidad/artículo bloqueados, precio editable. Líneas manuales (solo si `SinRQ` = true): todo editable. Múltiples líneas mismo artículo desde RQs distintas: entries separadas (C8) con tooltip. Recalcular totales en tiempo real. **`features/compras/ordenes/lib/acciones-disponibles.ts`** con la matriz §6.1 del 05 (20 acciones × 7 estados) + tests parametrizados que recorren TODA la matriz. Hooks: `useAgregarLineaDesdeRequisicion`, `useAgregarLineaManual`, `useActualizarLinea`, `useEliminarLinea`, `useActualizarCabecera`. Schemas Zod. Draft protection en LineaDialog. Estados loading/empty. | `frontend/src/features/compras/ordenes/components/{EditorLineas,LineaDialog,LineaDesdeRqBadge}.tsx`, `frontend/src/features/compras/ordenes/lib/acciones-disponibles.ts` + tests parametrizados, `frontend/src/features/compras/ordenes/api/{useAgregarLineaDesdeRequisicion,useAgregarLineaManual,useActualizarLinea,useEliminarLinea,useActualizarCabecera}.ts` + tests, schemas Zod | UF2-PR2 | **M** | **alto** (matriz §6.1 es la lógica más sensible del módulo + multiple mutations + edge cases de líneas con/sin RQ) | Tests parametrizados pasan TODA la matriz §6.1. Comprador agrega/edita/elimina líneas en Borrador. Líneas desde RQ tienen badge azul "Desde RQ-..." con enlace. Recalculación de totales en tiempo real. |

**Paralelización Fase 2**: UF2-PR1 → UF2-PR2 → UF2-PR3 secuencial.

---

## Fase 3 — Información complementaria + Adjuntos (M)

2 PRs: selectores catálogos + tabs información; AdjuntosManager
cross-módulo (aislado por riesgo de promoción).

> **Dependencia backend**: F2 + F9-PR1 backend OC en main (líneas
> completas + catálogos seed: Incoterms, Transportistas, Regímenes
> fiscales, Condiciones de pago).

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF3-PR1 | `compras/oc-uf3-tabs-informacion-y-catalogos` | **4 selectores nuevos cross-módulo** + tabs Logística/Importación/Financiera con edición inline. (a) **Selectores nuevos** en `components/erp/selectors/` (cross-módulo desde el inicio — CxP los consumirá): `<IncotermSelector>`, `<TransportistaSelector>`, `<CondicionesPagoSelector>`, `<RegimenFiscalSelector>`. Combobox simple con cache 1h (catálogos read-only seed). Hooks `useIncoterms`, `useTransportistas`, etc. en `features/catalogos/api/`. (b) **`<InformacionLogisticaForm>`** (FOC8) — dirección, transportista (selector + texto libre fallback), número guía, instrucciones. Edición inline con border dashed amber. Editable hasta `Autorizada` inclusive. (c) **`<InformacionImportacionForm>`** condicional `EsImportacion`: incoterm, país origen, contenedor, código ruta, semana, pedimento. `NumeroPedimento` editable post-aut sin re-auth. (d) **`<TotalesFinancierosForm>`**: descuento global, gastos adicionales, redondeo. Cálculo del backend devuelve totales recalculados. (e) **Hooks**: `useActualizarInformacionLogistica`, `useActualizarInformacionImportacion`, `useActualizarTotalesFinancieros` (PATCH, sin Idempotency-Key). (f) **Wirear los 3 sub-tabs** en P3 Tab "Información". Tests con MSW. | `frontend/src/components/erp/selectors/{Incoterm,Transportista,CondicionesPago,RegimenFiscal}Selector.tsx` + tests, `frontend/src/features/catalogos/api/{useIncoterms,useTransportistas,useCondicionesPago,useRegimenesFiscales,types}.ts`, `frontend/src/features/compras/ordenes/components/{InformacionLogisticaForm,InformacionImportacionForm,TotalesFinancierosForm}.tsx` + tests, `frontend/src/features/compras/ordenes/api/{useActualizarInformacionLogistica,useActualizarInformacionImportacion,useActualizarTotalesFinancieros}.ts` | UF2-PR3, **F9-PR1 backend en main** | **M** | medio (4 selectores nuevos cross-módulo + 3 forms con edición inline) | Catálogos cargan. Edición inline de logística funciona en cualquier estado no terminal. Importación oculta si `EsImportacion=false`. `NumeroPedimento` editable post-aut con tooltip "Editable hasta cierre". Recalculación de totales correcta. |
| UF3-PR2 | `compras/oc-uf3-adjuntos-manager-cross-modulo` | **`<AdjuntosManager>`** (FOC2 cerrado — **promovido cross-módulo desde el inicio**) en `components/erp/adjuntos/`. **PR aislado por riesgo: primer componente cross-módulo de adjuntos en el ERP — diseñado para que CxP, Activos y Recepción lo consuman.** Drag-and-drop + file picker fallback (WCAG). Lista de adjuntos con miniatura (PDFs primera página con `<embed>` nativo, imágenes thumbnail). Selector `<TipoDocumentoOcSelector>`. Upload con progress bar (XHR + onUploadProgress). Validaciones: ≤ 20MB por archivo, MIME types whitelist. **Diseñar con props parametrizadas** (`endpoint`, `tipos`, `obligatorios`) para no acoplar a OC. Hooks `useUploadFile(endpoint)` (generic en `lib/hooks/`), `useAdjuntarDocumento` (wrapper para endpoint OC), `useRemoverAdjunto` (DELETE en Borrador). Wirear en P3 Tab "Adjuntos". Tests unitarios + snapshots. **PR mergeable solo con review explícito sobre la API de props** (define el contrato cross-módulo). | `frontend/src/components/erp/adjuntos/{AdjuntosManager,TipoDocumentoSelector,AdjuntoPreview}.tsx` + tests, `frontend/src/lib/hooks/useUploadFile.ts`, `frontend/src/features/compras/ordenes/api/{useAdjuntarDocumento,useRemoverAdjunto}.ts` + tests | UF3-PR1 | **M** | **alto** (define API cross-módulo de adjuntos — primer consumidor; refactor caro si la API resulta incorrecta) | Subir PDF → 201, blob URL devuelto. Quitar adjunto → 204 (solo Borrador). Drag-and-drop funciona; fallback file picker accesible por teclado. Tipo de documento obligatorio. Tests pasan; props parametrizadas validadas con mock backend genérico. |

**Paralelización Fase 3**: UF3-PR1 → UF3-PR2 secuencial.

---

## Fase 4 — Workflow autorización (M)

2 PRs: bandeja pendientes + acciones detalle; ConflictDialog wireup
(aislado por riesgo).

> **Dependencia backend**: F3 backend OC en main (ya cumplida desde
> UF1).

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF4-PR1 | `compras/oc-uf4-workflow-autorizador` | **Flujo completo del autorizador**. (a) **P2 — Bandeja pendientes autorización** (`/compras/ordenes/pendientes-autorizacion`): filtro automático por permiso (N1 → `EnAutorizacionJefeCompras`; N2 → `EnAutorizacionDireccion`; ambos → tab switcher). Columnas Folio/Proveedor/Comprador/Líneas/Total/Días esperando/"Ver detalle". **Sin acciones inline (FOC16 cerrado)** — la única acción de la fila es navegar a P3. Hook `usePendientesAutorizacionOc(nivel)`. Visibility del menú gateada por `useHasAnyPermission(['autorizar.nivel1','autorizar.nivel2'])`. Sub-item en `nav.ts`. (b) **Hooks de mutación** con Idempotency-Key (desde el inicio — decisión cerrada): `useEnviarAAutorizacion`, `useAutorizarOrdenCompra` (body `{Nivel, Resultado, Notas?}`), `useRechazarOrdenCompra` (body `{Nivel, MotivoId, MotivoTexto?}`). Schemas Zod. (c) **Acciones contextuales en P3** detalle (gateadas via `acciones-disponibles.ts`): "Transmitir" en Borrador (deshabilitado si invariantes pre-auth fallan con tooltip), "Aprobar Nivel1" en `EnAutorizacionJefeCompras`, "Aprobar Nivel2" en `EnAutorizacionDireccion`, "Rechazar" en cualquier `EnAutorizacion*`. Confirm dialog con resumen. (d) **P7 — Modal de motivos** reusa `<MotivoRechazoSelector>` de RQ con filtro `aplicaA = OrdenCompra` bitmask. Tests parametrizados con MSW. | `frontend/src/routes/_app/compras/ordenes/pendientes-autorizacion.tsx`, `frontend/src/features/compras/ordenes/pages/BandejaPendientesOc.tsx`, `frontend/src/features/compras/ordenes/api/{usePendientesAutorizacionOc,useEnviarAAutorizacion,useAutorizarOrdenCompra,useRechazarOrdenCompra}.ts`, `frontend/src/features/compras/ordenes/components/{AccionesOC,ModalMotivoOC}.tsx`, `frontend/src/features/compras/ordenes/schemas/{enviar-a-autorizacion,autorizar,rechazar}.ts`, `frontend/src/lib/nav.ts` | UF2-PR3 | **M** | medio (flujo crítico del autorizador; sin acciones inline FOC16 simplifica UX) | Comprador transmite. Jefe Compras N1 abre detalle desde P2, aprueba. OC pasa a `EnAutorizacionDireccion`. Director N2 abre detalle, aprueba. OC pasa a `Autorizada`. Rechazo con motivo funciona. P2 no tiene acciones inline. |
| UF4-PR2 | `compras/oc-uf4-conflict-dialog-wireup` | **Wireup del `<ConflictResolutionDialog>` ante 409** en TODAS las mutaciones de OC. **PR aislado por riesgo: salvaguarda crítica de adopción (igual que UF3-PR2 de RQ).** Dos modos: (a) mutaciones desde formularios (P4 Sheet, edición de líneas, edición de información) → pasar el `form` al dialog, captura state, refresca, ofrece "Reaplicar mis cambios"; (b) mutaciones de acción simple (transmitir, aprobar, rechazar) → modo simple "refrescar y revisar". Mutation hooks capturan el error y abren el dialog con el modo apropiado. Tests E2E: (1) comprador con 8 líneas + cabecera modificada por colega → preserve-form-state → reaplica → submit OK; (2) dos autorizaciones simultáneas → modo simple. | `frontend/src/components/erp/collaboration/ConflictResolutionDialog.tsx` (wireup, ya creado por RQ), `frontend/src/features/compras/ordenes/api/*.ts` (manejo de error con form opcional en todas las mutations), tests E2E | UF4-PR1 | **S-M** | **alto** (salvaguarda crítica; aislado para foco de revisión) | Tests E2E ambos modos pasan. Comprador con 8 líneas no pierde trabajo cuando colega toca cabecera. Dos autorizadores simultáneos manejan 409 correctamente. |

**Paralelización Fase 4**: UF4-PR1 → UF4-PR2 secuencial.

---

## Fase 5 — Cancelar y duplicar (M)

2 PRs: cancelar (1 firma + doble firma); duplicar (aislado).

> **Dependencia backend**: F5-PR4 backend OC en main (cancelar con
> recepciones parciales) + F6-PR2 backend (duplicar).

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF5-PR1 | `compras/oc-uf5-cancelar-1-firma-y-doble-firma` | **Cancelación con 1 firma y doble firma (recepciones parciales).** (a) **Hook 1 firma**: `useCancelarOrdenCompra` con `{MotivoId, MotivoTexto?}` (Idempotency-Key). Acción "Cancelar" en P3 visible en `Borrador`/`EnAutorizacion*`/`Rechazada`/`Autorizada sin recepciones` (gateado por `cancelar`). Abre `<ModalMotivoOC>` con `aplicaA = Cancelacion`. (b) **Hook doble firma**: `useCancelarOrdenCompraConRecepciones` con `{MotivoId, MotivoTexto?, UsuarioAutorizadorN1Id, UsuarioAutorizadorN2Id}` (Idempotency-Key). Visible en `Autorizada con recepciones parciales` (gateado por `cancelar.doble`). (c) **`<DobleFirmaDialog>`** (componente nuevo en `features/compras/ordenes/components/`): stepper de 3 pasos — motivo, firma N1 (reusa `<UsuarioSelector>` + notas), firma N2 (usuario distinto del N1 + notas). Validator del payload: usuarios distintos + permisos correspondientes. Submit → mutation con feedback de cada paso. Toast del flujo `CANCELAR_FALLO` (422). Schemas `cancelar.ts`, `cancelar-doble-firma.ts`. | `frontend/src/features/compras/ordenes/api/{useCancelarOrdenCompra,useCancelarOrdenCompraConRecepciones}.ts`, `frontend/src/features/compras/ordenes/components/{AccionesOC,DobleFirmaDialog}.tsx` (extensión + nuevo), `frontend/src/features/compras/ordenes/schemas/{cancelar,cancelar-doble-firma}.ts` + tests | UF4-PR2 | **M** | **alto** (DobleFirmaDialog es UX nueva con permisos cruzados; edge cases del payload) | Cancelar OC en Borrador con 1 firma OK. Cancelar OC autorizada con recepciones parciales abre `<DobleFirmaDialog>`. Validators rechazan: mismo usuario N1=N2 → 422; N1 sin permiso `autorizar.nivel1` → 403; sin permiso `cancelar.doble` → 403. |
| UF5-PR2 | `compras/oc-uf5-duplicar-oc` | **Duplicación de OC** (C4 cerrado como cancelar + recrear). **PR aislado por riesgo: flujo C4 es la mitigación operativa principal — UX debe ser cristalina para que los compradores la entiendan.** (a) **Hook** `useDuplicarOrdenCompra` (Idempotency-Key) que llama `POST /ordenes/{ocOrigenId}/duplicar`. (b) **`<ConfirmDuplicarDialog>`** (FOC7) en `features/compras/ordenes/components/`: muestra preview de qué se copia (cabecera + líneas) y qué NO (adjuntos, autorizaciones, RQs). Acción primaria "Duplicar y abrir nueva". (c) **Acción "Duplicar OC" en P3**: visible solo en `Cancelada`/`Rechazada` (gateado por `crear`). En estados no terminales, deshabilitado con tooltip "Solo desde Cancelada o Rechazada". Submit → POST → redirige a P3 de la nueva OC en `Borrador` con `oc_origen_id`. (d) **Aside list de P3 con OCs hermanas**: si la OC tiene `oc_origen_id`, mostrar enlace "Origen: OC-..."; si la OC es origen, mostrar lista de hermanas. Hook `useOcsHermanasDuplicadas(ocOrigenId)`. Tests con MSW. | `frontend/src/features/compras/ordenes/api/{useDuplicarOrdenCompra,useOcsHermanasDuplicadas}.ts`, `frontend/src/features/compras/ordenes/components/{ConfirmDuplicarDialog,AsideListOcsHermanas}.tsx`, `frontend/src/features/compras/ordenes/schemas/duplicar.ts` + tests | UF5-PR1, **F6-PR2 backend en main** | **S-M** | **alto** (UX del flujo C4 es crítica; mal entendido → compradores no lo usan o lo abusan) | Cancelar OC autorizada → duplicar → nueva OC en Borrador con cabecera y líneas, sin adjuntos ni autorizaciones, `oc_origen_id` apuntando a origen. Aside list muestra hermanas. ConfirmDialog es claro sobre qué se copia y qué no. |

**Paralelización Fase 5**: UF5-PR1 → UF5-PR2 secuencial.

---

## Fase 6 — PDF + bandejas adicionales (S)

1 PR consolidado: PDF embedded + presets de bandeja.

> **Dependencia backend**: F6-PR1 + F6-PR3 backend OC en main (PDF
> con QuestPDF real).

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF6-PR1 | `compras/oc-uf6-pdf-y-bandejas-presets` | **PDF embebido + presets adicionales.** (a) **Hook** `usePdfOrdenCompra(id)` devuelve URL del blob (no carga el PDF). (b) **Tab "PDF" en P3**: `<embed>` nativo (FOC5 cerrado) apuntando a la URL del blob. Botón "Descargar PDF" arriba. Banner "PDF disponible después de la autorización Nivel 2" si OC no autorizada. (c) **Presets adicionales en P1** (refinamiento de UF1): "OCs autorizadas pendientes de recepción", "OCs recibidas pendientes de factura", "OCs facturadas pendientes de pago", "OCs canceladas/rechazadas" (auditoría), "Mis duplicadas" (filtro `oc_origen_id != null AND comprador_titular = me`). Cada preset setea search params Zod en URL. (d) **Hover state visual** en filas de P1: tooltip detallado al hover sobre los 3 sub-estados compactos. Tests con MSW. | `frontend/src/features/compras/ordenes/api/usePdfOrdenCompra.ts`, `frontend/src/features/compras/ordenes/components/{TabPdf,HoverSubEstados}.tsx`, `frontend/src/features/compras/ordenes/lib/presets-bandeja.ts`, `frontend/src/features/compras/ordenes/pages/BandejaOrdenesCompra.tsx` (extensión de presets) | UF5-PR2, **F6-PR3 backend en main** | **S** | bajo (PDF nativo + filtros sobre query existente) | Autorizar OC → PDF embebido en Tab "PDF" visible. Descarga funciona. Los 5 presets devuelven OCs correctas. Hover en sub-estados muestra detalle. |

**Paralelización Fase 6**: 1 PR.

---

## Fase 7 — Reportes operativos (M)

3 PRs: partidas abiertas; árbol documentos (aislado cross-módulo);
historial + últimas 100 + hermanas.

> **Dependencia backend crítica**: **F7-PR3 backend OC en main**
> (5 queries: `ObtenerHistoricoOrdenCompraQuery`,
> `ListarOcsHermanasDuplicadasQuery`, `ObtenerKpisPartidasAbiertasQuery`,
> `ListarPartidasAbiertasQuery`, `ListarUltimas100ComprasMaterialQuery`,
> `ObtenerArbolDocumentosQuery`). Esta es la fase con mayor dependencia
> backend.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF7-PR1 | `compras/oc-uf7-partidas-abiertas` | **P9 — Partidas abiertas** (vista crítica del negocio). (a) **Hooks**: `usePartidasAbiertas(filtros)`, `useKpisPartidasAbiertas(filtros)`. (b) **`<KpiCardsPartidasAbiertas>`** (FOC10 cerrado): 4 cards arriba — monto pendiente recibir, pendiente facturar, pendiente pago, count atrasadas. Reactivas a filtros aplicados. (c) **`<DiasAtrasadosBadge>`**: verde/amarillo/rojo según umbral contra `fecha_entrega_esperada` con ícono adicional para color-blind. (d) **Pantalla P9** (`routes/_app/compras/ordenes/partidas-abiertas.tsx`): KPI cards arriba; tabla densa con filtros sticky lateral (estado, sub-estados, proveedor, comprador, contenedor, ruta, semana, importe); columna calculada días atrasados con badge. Permiso `compras.ordenes.reportes.partidas_abiertas`. Search params Zod-validados. Breadcrumbs. Performance: target P95 < 500ms con 5k OCs activas seed. | `frontend/src/routes/_app/compras/ordenes/partidas-abiertas.tsx`, `frontend/src/features/compras/ordenes/pages/PartidasAbiertas.tsx`, `frontend/src/features/compras/ordenes/api/{usePartidasAbiertas,useKpisPartidasAbiertas}.ts`, `frontend/src/features/compras/ordenes/components/{KpiCardsPartidasAbiertas,DiasAtrasadosBadge,FiltrosPartidasAbiertas}.tsx`, `frontend/src/features/compras/ordenes/lib/partidas-abiertas-search-schema.ts` + tests | UF6-PR1, **F7-PR3 backend en main** | **M** | medio (vista crítica del negocio; performance-sensitive) | KPI cards reactivas a filtros. Tabla con 5k OCs P95 < 500ms. Días atrasados con color + ícono. Filtros combinados funcionan. |
| UF7-PR2 | `compras/oc-uf7-arbol-documentos` | **P10 — Árbol de documentos cross-módulo** (FOC6). **PR aislado por riesgo: primer componente cross-módulo de trazabilidad en el ERP — diseñado para que CxP, Recepción y Tesorería lo consuman.** (a) **`<ArbolDocumentos>`** en `components/erp/trazabilidad/`: vista grafo bidireccional RQ ← OC ← Recepción ← Factura ← Pago. Cada nodo con folio + fecha + monto + click navega al detalle. Layout responsive (vertical en mobile, horizontal en desktop). **Props parametrizadas** (`tipoDocumento`, `id`) — sin acoplar a OC. Renderizado SVG o CSS grid con líneas conectoras. (b) **Hook** `useArbolDocumentos(tipo, id)` en `features/compras/ordenes/api/` (o promover a `features/trazabilidad/api/` si emerge necesidad). (c) **P10 wrapper** (`routes/_app/compras/trazabilidad/oc/$id.tsx`): renderiza `<ArbolDocumentos tipoDocumento="orden-compra" id={id} />`. Cuando CxP/Recepción/Tesorería existan, agregar wrappers análogos. Tests por cada tipo de nodo + navegación por teclado (WCAG). | `frontend/src/components/erp/trazabilidad/{ArbolDocumentos,NodoDocumento,ConectorDocumentos}.tsx` + tests, `frontend/src/routes/_app/compras/trazabilidad/oc/$id.tsx`, `frontend/src/features/compras/ordenes/api/useArbolDocumentos.ts` (o `features/trazabilidad/api/`) | UF7-PR1 | **M** | **alto** (define API cross-módulo de trazabilidad — primer consumidor; refactor caro si la API resulta incorrecta) | Árbol bidireccional desde OC muestra 2 RQs upstream + recepciones/facturas/pagos downstream. Click en cada nodo navega al detalle correspondiente. Layout responsive en mobile. Tests pasan con mock de cada tipo. |
| UF7-PR3 | `compras/oc-uf7-historial-hermanas-y-ultimas100` | **Tab Historial completo + aside list hermanas + reporte últimas 100.** (a) **Tab "Historial" en P3 ampliado**: reemplazar stub de UF1 por timeline completo con todos los eventos (`useHistoricoOrdenCompra`). Hook + componente `<TimelineOrdenCompra>` (basado en `<TimelineAutorizaciones>` de RQ pero ampliado con tipos de evento — creación, transmisión, autorizaciones, recepciones, facturas, pagos, cancelación, duplicación). (b) **Aside list de P3 con OCs hermanas** activado (UF5-PR2 dejó stub): wirear `useOcsHermanasDuplicadas` con la query. (c) **P11 — Reporte últimas 100 compras del material** (`routes/_app/compras/articulos/$id/historial-compras.tsx`): tabla con filtros (proveedor, fecha, cantidad mínima, tipo doc). Hook `useUltimas100ComprasMaterial`. (d) **Entry points** del reporte: desde detalle de artículo (cuando exista), desde editor de línea en P3 (botón "Ver historial del material"), desde menú reportes. | `frontend/src/features/compras/ordenes/api/{useHistoricoOrdenCompra,useUltimas100ComprasMaterial}.ts`, `frontend/src/features/compras/ordenes/components/TimelineOrdenCompra.tsx`, `frontend/src/routes/_app/compras/articulos/$id/historial-compras.tsx`, `frontend/src/features/compras/ordenes/pages/HistorialComprasMaterial.tsx` | UF7-PR2 | **M** | bajo (read-only sobre endpoints ya entregados por F7-PR3 backend) | Tab Historial muestra ciclo completo cronológico. Aside list muestra hermanas si aplica. P11 funcional para 3 proveedores y 2 períodos verificados manualmente. |

**Paralelización Fase 7**: UF7-PR1 → UF7-PR2 → UF7-PR3 secuencial.

---

## Fase 8 — Hardening v1 + UAT (M)

2 PRs: tests/coverage/a11y; polish + runbook + UAT.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF8-PR1 | `compras/oc-uf8-tests-cobertura-e2e` | **Tests + cobertura + E2E + a11y completos** del submódulo OC. (a) **Unitarios** faltantes en hooks de mutación con MSW (cada uno: éxito, 4xx específicos, 5xx). Coverage de `features/compras/ordenes/` > 70%. (b) **Parametrizados** de `acciones-disponibles.ts` recorriendo TODA la matriz §6.1 del 05 (20 acciones × 7 estados). (c) **Integration tests** por pantalla con MSW: P1, P2, P3, P4 (3 modos), P5, P9, P10, P11. (d) **E2E con Playwright** (los 4 flujos críticos del 06 §8): (i) crear OC desde RQ → autorizar N1 → autorizar N2 → PDF generado; (ii) crear OC consolidada N:1 → transmitir → rechazo N1 → editar → re-transmitir → aprobar; (iii) cancelar OC con recepciones parciales (doble firma); (iv) cancelar + duplicar → modificar líneas → autorizar. (e) **Accessibility audit con axe-core** en cada pantalla. Contraste de `<SubEstadosBar>`, `<EstadoBadge>` OC, `<DiasAtrasadosBadge>` ≥ 4.5:1. Tab order en P3 tabs. `<ArbolDocumentos>` navegable por teclado. | `frontend/src/features/compras/ordenes/**/__tests__/*.test.tsx`, `frontend/e2e/oc/*.spec.ts`, `frontend/vitest.config.ts` (threshold), tests de a11y con axe-core | UF7-PR3 | **M** | medio (4 E2E flows + axe en 11 pantallas) | Coverage > 70%. Matriz §6.1 recorrida. E2E suites pasan en local y CI. axe-core sin issues bloqueantes. |
| UF8-PR2 | `compras/oc-uf8-polish-runbook-uat` | **Polish completo para release + UAT.** (a) **Performance**: P1 bandeja con 5k OCs seed P95 < 1s render. P9 partidas abiertas P95 query < 500ms. Lighthouse Performance ≥ 90. Bundle audit. (b) **Mobile P2**: layout responsive para P1, P2 y P3 con cards apilables < 768px (heredando patrón de UF7-PR3 de RQ). (c) **Stylesheet de impresión** `@media print` para P3 detalle de OC: oculta tabs/sidebar/topbar/acciones, formatea cabecera + líneas + información + autorización en A4 portrait con membrete simple. (d) **Página de ayuda ampliada**: extender `/compras/ayuda` (o crear `/compras/ordenes/ayuda`) con glosario OC, diagrama del ciclo de vida (7 estados + 3 sub-estados), FAQ del flujo cancelar+duplicar, FAQ del flujo doble firma. (e) **Documentación de patrones** en `frontend/docs/patrones-compras.md`: sección "Patrones de UI de OC" con master-detail + Sheet con 3 modos + AdjuntosManager cross-módulo + ArbolDocumentos cross-módulo. (f) **Runbook de operación** en `docs/operacion/runbook-frontend-oc.md`. (g) **Plan de UAT** con grupo piloto (Rodrigo + 1 comprador adicional + 1 Director) cubriendo los 12 indicadores de éxito del §12 del 01-diseño. Sesión de UAT y firma del owner. | `frontend/src/features/compras/ordenes/pages/{BandejaOrdenesCompra,BandejaPendientesOc,DetalleOrdenCompra}.tsx` (responsive), `frontend/src/features/compras/ordenes/pages/DetalleOrdenCompra.print.css`, `frontend/src/features/compras/lib/glosario.ts` (extensión), `frontend/src/routes/_app/compras/ayuda.tsx` o `frontend/src/routes/_app/compras/ordenes/ayuda.tsx`, `frontend/docs/patrones-compras.md` (extensión), `docs/operacion/runbook-frontend-oc.md`, `docs/operacion/plan-uat-frontend-oc.md` | UF8-PR1 | **M** | medio (cambios al shell impactan otras rutas; UAT puede revelar ajustes) | Lighthouse Performance + Accessibility ≥ 90. Mobile usable en P1/P2/P3. `Ctrl+P` desde P3 imprime A4 limpia. Owner firma runbook y UAT planificado. |

**Paralelización Fase 8**: UF8-PR1 → UF8-PR2 secuencial.

---

## Resumen de PRs por fase

| Fase | PRs Rev. 1 | Sizing fase | Notas |
|---|---|---|---|
| UF0 — Foundation OC UI | 1 | S | 1 PR consolidado: permisos + glosario + EstadoBadge + sidebar + search contextual. Paralelizable con F1-F3 backend. |
| UF1 — Read-only de OC | 2 | M | UF1-PR1 types+display+hooks read (SubEstadosBar + StepperAutorizacionOc); UF1-PR2 pantallas P1+P3 read-only. |
| UF2 — Captura básica | 3 | M-L | UF2-PR1 Sheet con 3 modos; UF2-PR2 selector consolidación (aislado por restricción de sucursal); UF2-PR3 editor de líneas con matriz §6.1 (aislado por riesgo). |
| UF3 — Información + Adjuntos | 2 | M | UF3-PR1 4 selectores nuevos cross-módulo + tabs Logística/Importación/Financiera; UF3-PR2 AdjuntosManager promovido cross-módulo (aislado por API). |
| UF4 — Workflow autorización | 2 | M | UF4-PR1 bandeja P2 + acciones detalle (sin inline FOC16); UF4-PR2 ConflictDialog wireup (aislado por riesgo). |
| UF5 — Cancelar + Duplicar | 2 | M | UF5-PR1 cancelar 1 firma + DobleFirmaDialog; UF5-PR2 duplicar OC (aislado — UX crítica C4). |
| UF6 — PDF + bandejas adicionales | 1 | S | 1 PR consolidado: PDF embedded + 5 presets adicionales + hover sub-estados. |
| UF7 — Reportes operativos | 3 | M | UF7-PR1 partidas abiertas + KPI cards; UF7-PR2 árbol documentos cross-módulo (aislado por API); UF7-PR3 historial + hermanas + últimas 100. |
| UF8 — Hardening + UAT | 2 | M | UF8-PR1 tests/cobertura/E2E/a11y; UF8-PR2 polish + runbook + UAT. |
| **Total v1** | **18** | | Camino crítico: UF0 → UF1 → UF2 → UF3 → UF4 → UF5 → UF6 → UF7 → UF8 ≈ 3–4 meses con 1 dev UI + 1 revisor. |

---

## Notas de proceso

### Convenciones de PR

- **Branch**: `compras/oc-uf<fase>-<slug-corto>`. Ejemplos:
  `compras/oc-uf2-selector-consolidacion`,
  `compras/oc-uf7-arbol-documentos`. Prefijo `oc-` para distinguir
  de RQ.
- **Título**: imperativo, conciso, ≤ 72 caracteres.
- **Body**: incluir referencia al PR ID (`UF1-PR2`), fase, deps que
  ya están en main (incluyendo fase backend), y screenshots/clips
  de pantalla afectada (cuando aplique). Si hay `PLATFORM-TODO`
  nuevos, listarlos.

### Reglas duras

- Cada PR deja `main` verde y desplegable. No se admite "este rompe
  pero el siguiente arregla".
- Cambios de DTOs (TypeScript types) que dependen del backend deben
  documentar en el body del PR contra qué endpoint exacto se
  validaron.
- Si un PR cruza 800 líneas netas, se parte. Excepción justificada
  documentada en el body.
- Tests verde + lint + typecheck obligatorios.
- **Idempotency-Key**: cada PR que agregue un POST de mutación
  incluye `useFormIdempotencyKey()` desde el momento (decisión Rev. 2
  del 04 backend). UF8-PR1 audita cobertura.

### Cuándo escalar

- **UF2-PR2** (selector consolidación): la restricción de sucursal
  única (§10.5 cerrada) es invariante crítica del backend. Tests
  E2E obligatorios antes de mergear.
- **UF2-PR3** (editor de líneas + matriz `acciones-disponibles.ts`):
  revisión cuidadosa obligatoria. Es la lógica más sensible del
  módulo (20 acciones × 7 estados). Tests parametrizados son el
  gate.
- **UF3-PR2** (AdjuntosManager cross-módulo): define API que CxP/
  Activos/Recepción consumirán. Review explícito sobre props
  parametrizadas antes de mergear.
- **UF4-PR2** (ConflictDialog wireup): es la salvaguarda crítica
  de adopción. PR aislado de UF4-PR1 para tener foco de revisión
  propio.
- **UF5-PR1** (DobleFirmaDialog): UX nueva con permisos cruzados.
  Validar con Director y Jefe Compras en design review antes de
  mergear.
- **UF5-PR2** (Duplicar OC): UX del flujo C4 (cancelar + recrear)
  es la mitigación operativa principal — si no se entiende, los
  compradores no la usarán. Validar en design review.
- **UF7-PR2** (ArbolDocumentos cross-módulo): define API que CxP/
  Recepción/Tesorería consumirán. Review explícito.
- **UF7-PR3** (historial + hermanas + últimas 100): depende de
  F7-PR3 backend que tiene 5 queries (riesgo de atraso). Si el
  backend no está listo, UF7-PR3 queda en draft hasta que llegue.
- **UF8-PR1** (E2E): si Playwright no está wireado en el repo
  (heredado de RQ — verificar), agregar config CI en el mismo PR.

---

## Cambios respecto a versiones previas

### Rev. 1 — versión inicial (2026-05-11)

Primer corte del breakdown de UI OC. Calibrado contra:

- 05-frontend-diseno.md Rev. 3 (incluye verificación de 9 selectores
  cross-módulo existentes heredados de RQ).
- 06-frontend-plan-implementacion.md Rev. 2 (sin bloqueante de
  `<ProveedorComboBox>`).
- Plan backend de OC: 02-plan Rev. 5 + 03-PR Rev. 5 (incluye
  sincronización backend↔frontend explícita + F7-PR3 con 5 queries).
- Stack frontend auditado en `frontend/` al 2026-05-11.

**18 PRs hasta release v1**. Camino crítico ~3-4 meses con 1 dev
UI + 1 revisor. PRs aislados por riesgo: UF2-PR2 (consolidación),
UF2-PR3 (matriz), UF3-PR2 (AdjuntosManager API), UF4-PR2 (Conflict),
UF5-PR1 (DobleFirma), UF5-PR2 (Duplicar UX), UF7-PR2 (ArbolDocs API).

**Filosofía**: cada PR es S–M cohesivo. Solo se aíslan los de alto
riesgo (matriz §6.1, primer consumidor cross-módulo de adjuntos y
trazabilidad, UX nueva crítica de adopción C4 + doble firma,
ConflictDialog como salvaguarda). Backend cerró OC con la misma
filosofía en 29 PRs (ver 03-pr-breakdown.md Rev. 5).

**Diferencia con RQ**: el 07 de RQ tiene 16 PRs (consolidación Rev. 4
agresiva). OC tiene 18 porque:

- Agrega F5 (cancelar + duplicar) — 2 PRs nuevos vs RQ.
- Agrega F6 (PDF + bandejas adicionales) — 1 PR nuevo.
- Agrega F7 (reportes operativos completos) — 3 PRs vs solo 1 stub
  en RQ.
- Ahorra en F0 — 1 PR vs 2 de RQ (toda la foundation heredada).
- Ahorra en F8 — sin soft lock (ya wireado por RQ).
