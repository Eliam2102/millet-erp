# PR Breakdown de frontend — Submódulo Requisiciones (Compras)

> **Construido sobre:** [05-frontend-diseno.md](05-frontend-diseno.md)
> (Rev. 5) y [06-frontend-plan-implementacion.md](06-frontend-plan-implementacion.md)
> (Rev. 6).
>
> **Estado:** Rev. 1 — propuesta inicial. Sigue las mismas
> convenciones de tamaño, riesgo y mergeable que el breakdown de
> backend ([03-pr-breakdown.md](03-pr-breakdown.md) Rev. 2).
>
> **Fecha:** 2026-05-09.

---

## 0. Cómo leer

- Cada fila es **un PR**. ID `UF<fase>-PR<n>` (UF = "UI Frontend")
  secuencial dentro de la fase.
- **Tamaños**: XS (≤ 200 líneas netas), S (200–500), M (500–800).
  Techo absoluto: 800. Por encima, partir.
- **Riesgo de romper main**: bajo / medio / alto. Cada PR debe dejar
  `main` verde y desplegable; el riesgo se refiere al *blast radius*
  si se cuela un bug a `main`.
- **Branch naming**: `compras/uf<fase>-<slug-corto>` (ej.
  `compras/uf2-editor-lineas`). Si el equipo prefiere prefijar
  `frontend/`, ajustar consistentemente.
- **Dependencias**: PRs previos que deben estar mergeados, y/o
  brechas backend (§14 del 05) cerradas.
- Al final de cada fase hay una nota de **paralelización**.

> Convención: PR mergeable = build verde (incluye `tsc -b` + `eslint`)
> + tests pasando (Vitest) + revisión de 1 dev + screenshot/clip de
> la pantalla afectada en el body del PR (cuando aplique).

---

## Fase 0 — Foundation UI Compras (S)

Plumbing del módulo. **Nada visible al usuario final** salvo el
sidebar habilitado.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF0-PR1 | `compras/uf0-plataforma-modulo` | **Plataforma del módulo** — todo lo que las features posteriores asumen disponible. (a) **Permisos canónicos**: 13 constantes (`compras.requisiciones.*` + `compras.aprobadores.administrar` + `compartido.catalogos.*`) en `permission-codes.ts`. (b) **Cliente HTTP enriquecido** en `lib/api/`: `apiRequest<T>(...)`, `ApiError`, helpers de detección, parsing de `application/problem+json`, captura de `ETag`, `useFormIdempotencyKey()` con retry automático de 409 `IDEMPOTENCY_IN_PROGRESS`, `applyServerErrors`. (c) **shadcn primitives faltantes** (`npx shadcn add`): `dialog`, `select`, `command`, `form`, `toast`/`sonner`, `calendar`, `popover`, `card`, `badge`, `skeleton`, `alert`. `<Toaster>` wireado en `_app.tsx`. (d) **Activar sidebar y shell**: item "Compras" en `nav.ts` con permiso, ruta placeholder `routes/_app/compras/index.tsx` → redirige a bandeja, estructura `features/compras/` y `features/catalogos/`. Tests unitarios del cliente HTTP. | `frontend/src/lib/auth/permission-codes.ts`, `frontend/src/lib/api/{client,error,idempotency,etag,apply-server-errors}.ts` + tests, `frontend/src/components/ui/*.tsx` (varios), `frontend/src/routes/_app.tsx`, `frontend/src/lib/nav.ts`, `frontend/src/routes/_app/compras/index.tsx` + placeholder bandeja, `frontend/src/features/compras/.gitkeep` (y subcarpetas) | — | **M** | medio (toda mutación futura pasa por el cliente HTTP) | Click "Compras" en sidebar → placeholder. Tests del cliente HTTP pasan: 409 reintenta con `Retry-After`, ProblemDetails se expone como `ApiError`. Cada primitive shadcn importable. |
| UF0-PR2 | `compras/uf0-ux-kit` | **UX kit del módulo** — componentes transversales que las pantallas consumen. (a) **Feedback** (§13.1 del 05) en `components/erp/feedback/`: `<EmptyState>`, `<ErrorState>`, `<TableSkeleton>`. (b) **`<Breadcrumbs>`** (§13.9) en `components/erp/` con preservación de search params via `useSearch`. (c) **`useUnsavedChangesGuard(isDirty)`** hook (§13.2) en `lib/hooks/` (wrapper de `beforeunload`). (d) **`<DomainTermTooltip>`** (§13.7) que lee de `features/compras/lib/glosario.ts` (8 estados + 4 naturalezas + `Cubrimiento` + `Matriz` + `Bifurcación` + `Reserva`). (e) **Stubs de collaboration** en `components/erp/collaboration/`: `<CollaborationIndicator />` renderiza `null`, hook `useCollaboration(entidad, id)` retorna `[]` con `PLATFORM-TODO(<CollaborationHub>)`. (f) **`<ConflictResolutionDialog />` con preserve-form-state** (F9 Rev. 3) — componente real, no stub: acepta `form` opcional, captura state, muestra diff visual, ofrece "Reaplicar mis cambios". Tests unitarios + snapshots por componente, ambos modos del ConflictDialog. | `frontend/src/components/erp/feedback/{EmptyState,ErrorState,TableSkeleton,DomainTermTooltip}.tsx`, `frontend/src/components/erp/Breadcrumbs.tsx`, `frontend/src/lib/hooks/useUnsavedChangesGuard.ts`, `frontend/src/features/compras/lib/glosario.ts`, `frontend/src/components/erp/collaboration/{CollaborationIndicator,useCollaboration,ConflictResolutionDialog}.tsx` + tests | UF0-PR1 | **M** | medio (ConflictDialog con diff y captura de form state es lógica delicada y crítica para adopción) | Tests pasan. Demo de prueba: click "fake conflict" → ConflictDialog muestra diff → "Reaplicar" repopula form. `useUnsavedChangesGuard(true)` instala listener; `false` lo remueve. Cada componente importable. |

**Paralelización Fase 0**: UF0-PR1 → UF0-PR2 secuencial (PR2 depende de los primitives shadcn de PR1).

---

## Fase 1 — Read-only de Requisiciones (M)

Bandeja general + detalle, ambos read-only. Valida shape de DTOs reales.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF1-PR1 | `compras/uf1-types-display-y-hooks-read` | **Pieza de lectura completa** — todo lo necesario para empezar pantallas. (a) **DTOs mirror** en `features/compras/api/types.ts` (`RequisicionListItemResponse`, `RequisicionResponse`, `MotivoRechazoResponse`, `PagedResponse<T>`, enums `EstadoRequisicion`/`Clasificacion`/`Prioridad`/`NivelAutorizacion`/`Naturaleza`/`EstatusCatalogo`). (b) **Display components** en `components/erp/display/`: `<EstadoBadge>` (8 colores con tooltips de glosario), `<NaturalezaBadge>` (4 colores con tooltips), `<MoneyDisplay>`, `<DateTimeDisplay>`. Verificación de contraste WCAG AA documentada en el body del PR. (c) **Hooks de read** en `features/compras/api/`: `useRequisiciones(filtros)` (query key estructurada), `useRequisicion(id)` (extrae `etag` y lo guarda en `query.meta`), `useMotivosRechazo()` (staleTime 1h). Tests con MSW: cada hook con respuesta exitosa, 404, 403. Snapshots por cada badge. | `frontend/src/features/compras/api/{types,useRequisiciones,useRequisicion,useMotivosRechazo}.ts` + tests, `frontend/src/components/erp/display/{EstadoBadge,NaturalezaBadge,MoneyDisplay,DateTimeDisplay}.tsx` + tests | UF0-PR2 | **M** | bajo | Tests pasan. `useRequisicion` expone `etag`. Cada badge renderiza con tooltip de glosario activo. |
| UF1-PR2 | `compras/uf1-pantallas-readonly` | **Pantallas P1 + P3 read-only** — valida shape de DTOs reales contra el código backend. (a) **P1 — Bandeja general** con **search params Zod-validados** en la ruta (`estado`, `departamentoId`, `requisitanteId`, `q`, `offset`, `limit`) → preservación de filtros al volver del detalle. Tabla con folio/fecha/requisitante/depto/monto/estado/Ver. Filtros (depto stub si §14.7 del 05 no resuelto), búsqueda por folio client-side. Paginación offset-based (50/200). Estados loading/empty/error con `<TableSkeleton>` / `<EmptyState>` (CTA "Nueva RQ" gateado por `crear`) / `<ErrorState>`. Breadcrumbs `Compras / Requisiciones`. (b) **P3 — Detalle read-only**: cabecera + lista de líneas + `<TimelineAutorizaciones>` (con autorizaciones del detalle, ampliación a histórico completo en UF7-PR4) + stub de `<CubrimientoBar>` (solo `CantidadOriginal`). 403/404 (§13.6 del 05) con páginas dedicadas. CTA "Volver a bandeja" preserva filtros via `useSearch`. Breadcrumbs `Compras / Requisiciones / <folio>`. `<CollaborationIndicator>` arriba (stub silente). | `frontend/src/routes/_app/compras/requisiciones/{index,$id}.tsx`, `frontend/src/features/compras/pages/{BandejaRequisiciones,DetalleRequisicion}.tsx`, `frontend/src/features/compras/components/{FiltrosBandeja,CabeceraRequisicion,ListaLineas,TimelineAutorizaciones,CubrimientoBarStub,DetalleErrorBoundary}.tsx`, `frontend/src/features/compras/lib/bandeja-search-schema.ts` | UF1-PR1 | **M** | medio (primeras pantallas con datos reales — si DTO no encaja, TS falla en build y se ajusta aquí) | Bandeja paginar funciona, filtros se reflejan en URL, back preserva filtros. Detalle muestra cabecera+líneas+autorizaciones. 403/404 con páginas amigables. Empty/loading/error states funcionan. |

**Paralelización Fase 1**: UF1-PR1 → UF1-PR2 secuencial.

---

## Fase 2 — Crear y editor de líneas (M-L)

> **Bloqueada por §14.7 del 05** (selectores org). Confirmar antes de
> arrancar.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF2-PR1 | `compras/uf2-selectores-y-form-fields` | **Toda la base de inputs** que P4 y P5 consumen. (a) **Selectores de catálogos**: `<ArticuloSelector>` (combobox lazy contra `GET /api/v1/catalogos/articulos` con debounce 300ms, filtra `estatus=Activo`, muestra clave + nombre + naturaleza badge), `<ProveedorSelector>` (análogo), `<MotivoRechazoSelector>` (filtra por bitmask `aplicaA`, exige textarea si `permiteTextoLibre`). Hooks `useArticulos`, `useProveedores`. (b) **Selectores org** (depende de §14.7 del 05): `<DepartamentoSelector>`, `<SucursalSelector>`, `<AlmacenSelector>`, `<UsuarioSelector>` consumiendo los endpoints que el backend confirme. (c) **Form fields**: `<MoneyField>`, `<DatePickerField>`, `<DecimalField>`, `<TextAreaField>` con integración react-hook-form. (d) **Schemas Zod** base: `crear-requisicion.ts`, `linea.ts`, `notas-linea.ts`. Tests con MSW por cada selector + integration test de form de prueba con todos los fields. | `frontend/src/features/catalogos/api/{useArticulos,useProveedores,types}.ts`, `frontend/src/components/erp/selectors/{Articulo,Proveedor,MotivoRechazo,Departamento,Sucursal,Almacen,Usuario}Selector.tsx`, `frontend/src/components/erp/forms/{MoneyField,DatePickerField,DecimalField,TextAreaField}.tsx`, `frontend/src/features/compras/schemas/*.ts` + tests | UF1-PR2, **§14.7 del 05 confirmado** | **M** | medio (combobox lazy es el patrón base que se reutilizará en CxC/OC; selectores org dependen del backend) | Combobox abren/buscan/seleccionan. Form de prueba con `useForm` + Zod resolver valida cada field. Tests pasan. |
| UF2-PR2 | `compras/uf2-pantalla-nueva-requisicion` | **P4 — Nueva requisición** (cabecera): form con selectores org + clasificación + prioridad + descripción + fecha entrega deseada + proveedor sugerido + requisitante (gateado por `seleccionar-requisitante`). Hook `useCrearRequisicion()` con Idempotency-Key. Submit → POST → redirige a `/compras/requisiciones/$id`. Errores de servidor con `applyServerErrors`. **Draft protection** (§13.2 del 05): `useUnsavedChangesGuard(form.formState.isDirty)` + `localStorage` (key `compras:rq:draft:nueva:<userId>:<empresaId>`, debounce 500ms). Modal "Recuperar borrador" si existe al montar; borrar tras submit. Tooltips de glosario en clasificación/prioridad/almacén destino. Breadcrumbs `Compras / Requisiciones / Nueva`. | `frontend/src/routes/_app/compras/requisiciones/nueva.tsx`, `frontend/src/features/compras/pages/NuevaRequisicion.tsx`, `frontend/src/features/compras/api/useCrearRequisicion.ts`, `frontend/src/features/compras/lib/draft-storage.ts` | UF2-PR1 | **M** | medio (primera mutación con idempotency en producción) | Submit crea RQ. Refresh + recuperar borrador funciona. Cerrar tab con form dirty → diálogo nativo. Errores 422 inline. Doble-submit usa misma key. |
| UF2-PR3 | `compras/uf2-editor-lineas-y-acciones-disponibles` | **P5 — Editor de líneas** dentro de P3 + matriz `acciones-disponibles.ts` (la lógica más sensible del módulo, aislada por riesgo). En `Borrador`: tabla editable inline (Agregar/Pencil/Trash) con `LineaDialog` usando `<ArticuloSelector>` + `<MoneyField>`. En `EnAutorizacion`/`Autorizada`/`EnSurtido`: read-only excepto notas (textarea inline optimistic). En terminales: read-only completo. **`features/compras/lib/acciones-disponibles.ts`** con la matriz §6.1 del 05 + tests parametrizados que recorren TODA la matriz (cada celda × cada permiso → outcome esperado). Hooks de mutación: `useAgregarLinea` (idempotency), `useActualizarLinea` (sin idempotency), `useEliminarLinea`, `useActualizarNotasLinea` (optimistic). Draft protection en `LineaDialog` (`useUnsavedChangesGuard` + localStorage opcional). Estados loading/empty con `<TableSkeleton>` y `<EmptyState>`. | `frontend/src/features/compras/components/{EditorLineas,LineaDialog}.tsx`, `frontend/src/features/compras/lib/acciones-disponibles.ts` + tests parametrizados, `frontend/src/features/compras/api/{useAgregarLinea,useActualizarLinea,useEliminarLinea,useActualizarNotasLinea}.ts` + tests | UF2-PR2 | **M** | **alto** (matriz §6.1 es la lógica más sensible del módulo + multiple mutations) | Tests parametrizados pasan TODA la matriz §6.1. Capturador agrega/edita/elimina en Borrador. Comprador edita notas en Autorizada. Cerrar dialog con cambios → diálogo nativo. Eliminar última línea: gateará Transmitir en UF3. |

**Paralelización Fase 2**: UF2-PR1 → UF2-PR2 → UF2-PR3 secuencial.

---

## Fase 3 — Workflow de autorización (M-L)

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF3-PR1 | `compras/uf3-workflow-autorizador` | **Flujo completo del autorizador** (capturador transmite → N1/N2 aprueba o rechaza). (a) **P2 — Bandeja de pendientes** (`/compras/pendientes`): misma estructura que P1, filtrada por `EnAutorizacion`, con filtro depto (gateado por `ver-todos-departamentos`). Hook `usePendientesAutorizacion`. Visibility del menú gateada por `useHasAnyPermission(['autorizar.nivel1', 'autorizar.nivel2'])`. Sub-item en `nav.ts`. (b) **Hooks de mutación** con Idempotency-Key: `useTransmitirRequisicion`, `useAutorizarRequisicion` (body `{Nivel, Notas?}`), `useRechazarRequisicion` (body `{MotivoId, MotivoTexto?}`). Schemas Zod `autorizar.ts`, `terminar-requisicion.ts`. (c) **Acciones contextuales en P3** detalle (gateadas via `acciones-disponibles.ts`): "Transmitir" (Borrador, deshabilitado si 0 líneas), "Aprobar Nivel1"/"Aprobar Nivel2" (según permiso + estado), "Rechazar" (`EnAutorizacion`). Confirm dialog antes de cada acción. (d) **P6 — Modal de motivos** para rechazo (`<MotivoRechazoSelector aplicaA="Rechazo">` + textarea condicional). Toast invalida queries (matriz puede satisfacerse y RQ pasar a `Autorizada`/`EnSurtido`). Tests parametrizados con MSW: 204, 409, 422. | `frontend/src/routes/_app/compras/pendientes.tsx`, `frontend/src/features/compras/pages/BandejaPendientes.tsx`, `frontend/src/features/compras/api/{usePendientesAutorizacion,useTransmitirRequisicion,useAutorizarRequisicion,useRechazarRequisicion}.ts`, `frontend/src/features/compras/components/{AccionesRequisicion,ModalMotivo}.tsx`, `frontend/src/features/compras/schemas/{autorizar,terminar-requisicion}.ts`, `frontend/src/lib/nav.ts` + tests | UF2-PR3 | **M** | **alto** (acciones que disparan bifurcación stock-aware en el backend; conflict 409 esperable; matriz §6.1 se ejercita end-to-end aquí) | Capturador transmite. N1 aprueba; matriz se satisface y RQ pasa a Autorizada/EnSurtido. N2 aprueba si requerido. Rechazo con motivo "OTRO" + texto libre → `Rechazada`. RQ ya no aparece en pendientes. |
| UF3-PR2 | `compras/uf3-conflict-dialog-wireup` | **Wireup del `<ConflictResolutionDialog />`** ante 409 `CONCURRENCY_CONFLICT` en TODAS las mutaciones del módulo (Fase 2 + Fase 3). **PR aislado por riesgo según §14.6 del 05** — es la salvaguarda crítica de adopción. **Dos modos** (F9 Rev. 3): (a) **Mutaciones desde formularios con state local** (P4 nueva, `LineaDialog`, edición de notas) → pasar el `form` al dialog, captura state, refresca, ofrece "Reaplicar mis cambios"; (b) **Mutaciones de acción simple** (transmitir, aprobar, rechazar) → modo simple "refrescar y revisar". Mutation hooks capturan el error y abren el dialog con el modo apropiado. Tests E2E: (1) capturador con 15 líneas + cabecera modificada por colega → preserve-form-state → reaplica → submit OK; (2) dos autorizaciones simultáneas → modo simple. | `frontend/src/components/erp/collaboration/ConflictResolutionDialog.tsx` (wireup, ya creado en UF0-PR2), `frontend/src/features/compras/api/{useTransmitir,useAutorizar,useRechazar,useAgregarLinea,useActualizarLinea,useActualizarNotasLinea,useCrearRequisicion}.ts` (manejo de error con form opcional), tests E2E | UF3-PR1 | **S-M** | **alto** (salvaguarda crítica de adopción §14.6 del 05; aislado para tener foco de revisión) | Tests E2E ambos modos pasan. Capturador con 15 líneas no pierde su trabajo cuando colega toca cabecera. |

**Paralelización Fase 3**: UF3-PR1 → UF3-PR2 secuencial.

---

## Fase 4 — Cancelar y eliminar (S)

Consolida en 1 PR — cohesivo y bajo riesgo.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF4-PR1 | `compras/uf4-cancelar-y-eliminar` | Hooks `useCancelarRequisicion` (Idempotency-Key) y `useEliminarRequisicion` (**sin** Idempotency-Key — decisión backend, ver §7.6 del 05). En P3 detalle: botón "Cancelar" en `Autorizada`/`EnSurtido` (gateado por `cancelar`); botón "Eliminar" en `Borrador`/`EnAutorizacion` (gateado por `eliminar`). Ambos abren `<ModalMotivo>` con `aplicaA="Cancelacion"` o `"Eliminacion"`. Toast del flujo `CANCELAR_FALLO` (422): mensaje específico con `traceId` + CTA reintentar (no automático — el usuario debe entender que algo falló downstream). Tests parametrizados con MSW. | `frontend/src/features/compras/api/{useCancelarRequisicion,useEliminarRequisicion}.ts`, `frontend/src/features/compras/components/AccionesRequisicion.tsx` (extender) | UF3-PR3 | S | medio (CANCELAR_FALLO requiere UX cuidada) | Cancelar `Autorizada` con motivo → `Cancelada`, libera reservas (verificable en logs del stub). Eliminar `Borrador` → `Eliminada`. CANCELAR_FALLO muestra toast con traceId. |

**Paralelización Fase 4**: 1 PR.

---

## Fase 5 — Cubrimiento visible (S)

> **Pre-requisito**: §15.7 del 05 confirmado (DTO incluye cubrimiento
> por línea).

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF5-PR1 | `compras/uf5-cubrimiento-bar` | `<CubrimientoBar>` real (reemplaza el stub de UF1-PR4) con **WCAG AA + táctil sin hover** (F6 Rev. 3): (1) **Barra horizontal segmentada** con 4 segmentos cada uno con **patrón visual distintivo** (almacén verde sólido / pendiente recepción amarillo rayas diagonales / recibido azul oscuro punteado / pendiente compra gris cross-hatch) — distinguible sin depender de color. (2) **Números visibles al lado de la barra siempre**: "10 total · 3 alm · 5 OC (2 rec) · 2 pend" — el dato no depende de hover ni de tooltip. (3) Tooltip con desglose extendido + porcentajes queda como detalle adicional para desktop con mouse. `aria-label` con info numérica completa, `role="img"`, contraste de cada segmento ≥ 4.5:1 contra el fondo. Resumen agregado en cabecera de P3 ("Cubrimiento global: X% de líneas cerradas") como `<ResumenCubrimiento>`. Tests visuales (snapshot) por casos: solo almacén, solo compra, mixto, recibido parcial, cerrado. **Test de accesibilidad con axe-core** del componente. | `frontend/src/components/erp/display/CubrimientoBar.tsx`, `frontend/src/components/erp/display/CubrimientoBar.module.css` (patrones SVG / CSS), `frontend/src/features/compras/components/ResumenCubrimiento.tsx`, tests con casos parametrizados + axe | UF1-PR4, **§15.7 del 05 confirmado** | S | bajo (visual; lógica de cálculo simple) | Una RQ `EnSurtido` muestra la barra con 4 segmentos correctamente proporcionados, números al lado, patrones distinguibles en B/N. axe-core sin issues. Tests pasan. |

**Paralelización Fase 5**: 1 PR. Paralelizable con Fase 4.

---

## Fase 6 — Admin de aprobadores (S)

> **Rev. 6**: solo P9. P10 (reclasificar naturaleza bulk) sale del
> scope — vive en módulo Datos Maestros (es operación sobre
> `compartido.articulos`, transversal al ERP).

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF6-PR1 | `compras/uf6-admin-aprobadores` | **P9 — Admin de aprobadores**: tab "Vigentes" con tabla filtrable (depto, rol, usuario), botón "Designar" (dialog con DepartamentoSelector + RolAprobador select + UsuarioSelector + motivo opcional), botón "Revocar" (confirm + DELETE). Tab "Histórico" con form que exige al menos un filtro (validación cliente; backend devuelve 422 `FILTRO_OBLIGATORIO` como defensa). Hooks `useAprobadoresVigentes`, `useAprobadoresHistorico`, `useDesignarAprobador` (Idempotency-Key), `useRevocarAprobador` (Idempotency-Key). Schema `designar-aprobador.ts`. Item de menú "Admin · Aprobadores" gateado por `compras.aprobadores.administrar` (oculto sin el permiso). Manejo del 404 `USUARIO_NO_ENCONTRADO`: inline error en `<UsuarioSelector>`. | `frontend/src/routes/_app/compras/admin/aprobadores.tsx`, `frontend/src/features/compras/pages/AdminAprobadores.tsx`, `frontend/src/features/compras/api/{useAprobadoresVigentes,useAprobadoresHistorico,useDesignarAprobador,useRevocarAprobador}.ts`, `frontend/src/features/compras/schemas/designar-aprobador.ts`, `frontend/src/lib/nav.ts` (sub-item) | UF2-PR1 (UsuarioSelector), UF0-PR1 | M | medio | Admin captura los 3 roles para un depto. Re-designar al mismo usuario es no-op. Revocar cierra vigencia. Histórico sin filtro → 422 visible al usuario. |

**Paralelización Fase 6**: 1 PR.

---

## Fase 7 — Hardening v1 (M)

Lo que falta para release. Consolidación: 6 PRs Rev. 3 → 3 PRs +
1 condicional. UF7-PR2 (E2E Playwright) sigue aislado por
introducir config de CI nueva con su propio blast radius.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF7-PR1 | `compras/uf7-tests-y-cobertura` | **Tests + cobertura completa** del módulo. (a) **Unitarios** faltantes en hooks de mutación con MSW (cada uno: éxito, 4xx específicos, 5xx). Coverage de `features/compras/` > 70%. (b) **Parametrizados** de `acciones-disponibles.ts` recorriendo TODA la matriz §6.1 del 05 (cada celda × cada permiso → outcome esperado). (c) **Integration** del módulo: smoke tests por pantalla (P1, P2, P3, P4, P5, P6, P9, P10) con `QueryClientProvider` envolvente y MSW. Threshold de coverage configurado en `vitest.config.ts`. | `frontend/src/features/compras/**/__tests__/*.test.tsx`, `frontend/vitest.config.ts` | UF6-PR2 | **M** | bajo (solo aditivos) | Coverage report > 70%. Matriz §6.1 recorrida por test parametrizado. CI verde. |
| UF7-PR2 | `compras/uf7-e2e-playwright` | **E2E con Playwright** (aislado por introducir config de CI nueva). Si el equipo ya tiene Playwright wireado, este PR solo agrega specs; si no, lo introduce primero (config + workflow GH Actions). Suites: (a) happy path capturador → N1 → autorizada → comprador notas; (b) rechazo con motivo; (c) **conflicto 409 con preserve-form-state** (caso 15 líneas + colega que toca cabecera); (d) eliminar pre-aut; (e) cancelar post-aut. Corre contra backend mockeado o docker-compose con backend real + DB seed. | `frontend/e2e/*.spec.ts`, `frontend/playwright.config.ts`, `.github/workflows/frontend-e2e.yml` | UF7-PR1 | **M** | **alto** (config de CI nueva; intermitencia conocida de E2E; PR aislado) | Suites pasan en local y CI. Test del caso 15 líneas verifica preserve-form-state. Documentado cómo correrlo localmente. |
| UF7-PR3 | `compras/uf7-polish-y-uat` | **Polish completo para release** — todo lo no-test. (a) **Accesibilidad** (asunción F3): `axe-core` en cada pantalla, contraste de badges ≥ 4.5:1, tab order en P5 editor. (b) **Mobile P2** (asunción F2): layout responsive con cards apilables < 768px en `/compras/pendientes`, `<MobileSidebar>` con hamburger en topbar, sidebar colapsable. (c) **Performance**: benchmark con seed 10k RQs, optimizar si P95 > 1s render o > 200ms fetch (virtualización TanStack Table virtual si necesario), Lighthouse Performance ≥ 90, bundle audit. (d) **Página de ayuda** (§13.7 del 05) en `/compras/ayuda` con glosario + diagrama ciclo de vida + matriz simplificada + FAQ. Link en topbar. (e) **Stylesheet de impresión** (§13.8 del 05) `@media print` para P3 detalle (cubre Ctrl+P mientras endpoint PDF v1.1 no exista). (f) **Runbook** en `frontend/CLAUDE.md` o `frontend/docs/patrones-compras.md`. (g) **Plan de UAT** con grupo piloto. | `frontend/src/components/layout/{Topbar,Sidebar,MobileSidebar}.tsx`, `frontend/src/features/compras/pages/BandejaPendientes.tsx` (responsive), `frontend/src/routes/_app/compras/ayuda.tsx`, `frontend/src/features/compras/pages/Ayuda.tsx`, `frontend/src/features/compras/pages/DetalleRequisicion.print.css`, `frontend/CLAUDE.md` o `frontend/docs/patrones-compras.md`, `docs/operacion/uat-frontend-compras.md`, tests con axe | UF7-PR1 | **M** | medio (cambios al shell impactan otras rutas; UAT puede revelar ajustes) | Lighthouse Performance + Accessibility ≥ 90. P2 usable en mobile. `?` en topbar lleva a ayuda. `Ctrl+P` desde P3 imprime A4 limpia. Owner firma runbook + UAT planificado. |
| UF7-PR4 | `compras/uf7-timeline-historico-completo` | **Condicional** a §14.1 del 05 (ticket P0 al backend, F7 Rev. 3): cuando el endpoint `GET /api/v1/compras/requisiciones/{id}/historico` esté disponible, **rebautizar `<TimelineAutorizaciones>` → `<TimelineRequisicion>`** y ampliar para mostrar TODAS las transiciones del agregado (Creada, LineaAgregada/Actualizada/Eliminada, Transmitida, AutorizadaN1/N2, Rechazada, Eliminada, Cancelada, Cubrimiento, Recepcion, SaldoNoSurtido, Cerrada) con avatar del actor + timestamp + payload. Hook `useHistoricoRequisicion(id)`. Si el endpoint NO está disponible: PR queda en `wontfix` para v1 y se documenta como riesgo de adopción en plan de UAT. | `frontend/src/features/compras/api/useHistoricoRequisicion.ts`, `frontend/src/features/compras/components/TimelineRequisicion.tsx` (rename + ampliación), `frontend/src/features/compras/api/types.ts` | UF1-PR2, **§14.1 del 05 endpoint disponible** | **S** | medio (rename de componente con consumidores) | Endpoint responde shape esperado. Timeline muestra los N tipos de transición. Si endpoint no disponible: PR draft y se escala con backend. |

**Paralelización Fase 7**: UF7-PR1 → UF7-PR2 (E2E) y UF7-PR3 (polish) paralelos. UF7-PR4 condicional, en cualquier momento si el endpoint llega.

---

## Fase 8 — Soft lock real (M, antes de release v1 — Camino A)

> **Camino A confirmado por owner Rev. 4 (2026-05-09)**: UF8-PR1
> entra como parte de release v1, antes del UAT. Total v1 = 17 PRs.
>
> **Pre-requisito**: `<CollaborationHub>` SignalR cerrado por
> plataforma antes de Fase 7 hardening. Si en 4-6 semanas no hay
> plan claro, fallback automático a Camino B (F8 difiere a v1.1, F9
> robusto compensa parcialmente).
>
> Camino C (aceptar riesgo + dialog simple) descartado por el owner.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| UF8-PR1 | `compras/uf8-soft-lock-real` | **Soft lock activo end-to-end.** (a) **Cliente SignalR** en `lib/signalr.ts` (extiende ADR-0001 cuando llegue). (b) **Hook real `useCollaboration(entidad, id)`** que reemplaza el stub: heartbeat 30s, reconexión, expiración. (c) **`<CollaborationIndicator />` activo**: avatares apilados, badges, tooltips. (d) **Banner sutil en P3** cuando "alguien edita": "⚠️ Pedro García está editando esta requisición. Revisa con él antes de guardar." (e) **Invalidación reactiva** de queries en eventos del hub (`requisicion.actualizada` → invalida `useRequisicion(id)` y `useRequisiciones`). (f) **Cleanup**: borrar comentarios `PLATFORM-TODO(<CollaborationHub>)` y actualizar §8.6 del 01-diseno.md. Tests con SignalR mock (`@microsoft/signalr` testing utilities). | `frontend/src/lib/signalr.ts`, `frontend/src/components/erp/collaboration/{useCollaboration,CollaborationIndicator}.tsx` (rewrite), `frontend/src/features/compras/pages/DetalleRequisicion.tsx` (banner), `docs/modulos/compras-requisiciones/01-diseno.md` (§8.6), tests | `<CollaborationHub>` cerrado, UF7-PR3 | **M** | **alto** (nueva infraestructura runtime; impactará otros módulos cuando se sumen) | Hub real conectado en dev. Dos usuarios abren misma RQ → ambos ven al otro. Cuando uno edita, el otro ve el banner. Conflictos siguen funcionando con `<ConflictResolutionDialog />`. |

**Paralelización Fase 8**: 1 PR.

---

## Resumen de PRs por fase

| Fase | PRs Rev. 4 | Sizing fase | Notas |
|---|---|---|---|
| 0 — Foundation UI Compras | 2 | M | UF0-PR1 plataforma (cliente HTTP + permisos + shadcn + sidebar) + UF0-PR2 UX kit (feedback + breadcrumbs + collaboration stubs + ConflictDialog robusto). |
| 1 — Read-only de Requisiciones | 2 | M | UF1-PR1 types+display+hooks read; UF1-PR2 pantallas P1+P3 read-only (valida DTOs reales). |
| 2 — Crear y editor de líneas | 3 | M-L | Bloqueada por §14.7 del 05. UF2-PR1 selectores+fields; UF2-PR2 P4 nueva con draft; UF2-PR3 P5 editor con matriz §6.1 (alto riesgo, aislado). |
| 3 — Workflow autorizador | 2 | M-L | UF3-PR1 bandeja+transmitir+autorizar+rechazar+modal; UF3-PR2 ConflictDialog wireup (aislado por riesgo §14.6 del 05). |
| 4 — Cancelar y eliminar | 1 | S | Cohesivo, bajo riesgo. |
| 5 — Cubrimiento visible | 1 | S | Bloqueada por §15.7 del 05. CubrimientoBar con números + patrón (F6 Rev. 3). |
| 6 — Admin de aprobadores | 1 | S | Solo P9 (Rev. 6). P10 reclasificar naturaleza salió del scope a Datos Maestros. |
| 7 — Hardening v1 | 3 + 1 condicional | M | UF7-PR1 tests; UF7-PR2 E2E aislado (config CI); UF7-PR3 polish completo (a11y + mobile + perf + ayuda + print + runbook + UAT); UF7-PR4 condicional (timeline histórico si §14.1 cierra). |
| 8 — Soft lock real | 1 | M | **Camino A confirmado por owner Rev. 4** (2026-05-09): entra antes del UAT. |
| **Total v1** | **16** | | Camino crítico: F0 → F1 → F2 → F3 → F4 → F5 → F6 → F7 → **F8** ≈ 2–3 meses con 1 dev. |
| **Total con UF7-PR4 condicional** | **17** | | Si §14.1 del 05 (endpoint historico) cierra. |

---

## Notas de proceso

### Convenciones de PR

- **Branch**: `compras/uf<fase>-<slug-corto>`. Ejemplos:
  `compras/uf2-editor-lineas`, `compras/uf6-admin-aprobadores`.
  Si el equipo prefiere prefijo `frontend/`, ajustar
  consistentemente desde UF0-PR1.
- **Título**: imperativo, conciso, ≤ 72 caracteres.
- **Body**: incluir referencia al PR ID (`UF1-PR2`), fase, deps que
  ya están en main, y screenshots/clips de pantalla afectada
  (cuando aplique). Si hay `PLATFORM-TODO` nuevos, listarlos.

### Reglas duras

- Cada PR deja `main` verde y desplegable. No se admite "este rompe
  pero el siguiente arregla".
- Cambios de DTOs (TypeScript types) que dependen del backend deben
  documentar en el body del PR contra qué endpoint exacto se
  validaron.
- Si un PR cruza 800 líneas netas, se parte. Excepción justificada
  documentada en el body.
- Sin `PLATFORM-TODO` huérfanos: cada uno debe estar en la tabla
  §8.6 del diseño 01.
- Tests verde + lint + typecheck obligatorios.

### Cuándo escalar

- **UF2-PR1** (selectores incluyendo org): si §14.7 del 05 no se
  resuelve con backend antes de arrancar, escalar al owner para
  ticket backend prioritario. Sin selectores org el PR no entrega
  valor completo.
- **UF2-PR3** (editor de líneas + matriz `acciones-disponibles.ts`):
  revisión cuidadosa obligatoria. Es la lógica más sensible del
  módulo (cada celda × cada permiso). Tests parametrizados son
  el gate.
- **UF3-PR1** (workflow autorizador): los conflictos 409 +
  bifurcación stock-aware son el camino más probable de bugs
  sutiles. Tests con MSW cubren la mayoría; el caso 15 líneas se
  cubre en E2E (UF7-PR2).
- **UF3-PR2** (ConflictDialog wireup): es la salvaguarda crítica
  de adopción según §14.6 del 05. PR aislado de UF3-PR1 para tener
  foco de revisión propio.
- **UF5-PR1** (CubrimientoBar): si §15.7 del 05 no confirmado, no
  mergear; Fase 5 puede deslizarse hasta el final.
- **UF7-PR2** (E2E): si el equipo no tiene Playwright wireado,
  escalar para decidir entre adoptarlo aquí o diferir E2E a v2.
- **UF8-PR1**: si `<CollaborationHub>` cierra durante el camino,
  evaluar si Fase 8 se puede meter antes de release v1 (camino A
  de §14.6 del 05) o se difiere a v1.1 (camino B).

---

## Cambios respecto a versiones previas

### Rev. 6 — separación arquitectónica: catálogos a Datos Maestros (2026-05-09)

Alineado con [05-frontend-diseno.md](05-frontend-diseno.md) Rev. 5
y [06-frontend-plan-implementacion.md](06-frontend-plan-implementacion.md)
Rev. 6. Cambios al breakdown:

- **UF6-PR2 eliminado**: la pantalla P10 (reclasificar naturaleza
  bulk) salió del scope de Compras Requisiciones — vive en módulo
  Datos Maestros (es operación sobre `compartido.articulos`,
  transversal al ERP). Los archivos planeados
  (`ReclasificarNaturaleza.tsx`, `useReclasificarNaturaleza.ts`,
  `MultiSelectArticulos.tsx`, `reclasificar-naturaleza.ts` schema)
  se relocan al backlog de ese módulo cuando arranque.
- **Fase 6 reescrita** como "Admin de aprobadores" (S, 1 PR). Solo
  UF6-PR1.
- **Tabla resumen**: Total v1 baja de 17 a **16 PRs**. La fila
  "Fallback si Camino A no viable" se elimina (Camino A ya está
  ejecutado en backend, irrelevante).
- **Convenciones**: ejemplo de branch naming actualizado.

Sin cambios en otras fases. P9 (admin de aprobadores) se queda en
Compras: tabla `compras.aprobadores_departamento`, permiso
`compras.aprobadores.administrar` — config local del módulo.

### Rev. 5 — sesión de validación de asunciones con owner (2026-05-09)

Alineado con [05-frontend-diseno.md](05-frontend-diseno.md) Rev. 4.
Cambios al breakdown:

- **Fase 8 reescrita**: ya no es "decisión P0 antes de release" con
  tres caminos abiertos. **Camino A confirmado** por owner: UF8-PR1
  entra antes del UAT como parte de release v1. Sub-titulo cambia
  de "S, decisión P0" a "M, antes de release v1 — Camino A".
- **Tabla resumen**: total v1 oficial = **17 PRs** (no 16). El
  fallback a 16 PRs (Camino B) queda documentado como contingencia
  si plataforma no cierra `<CollaborationHub>` a tiempo.
- **UF7-PR4** (timeline histórico) sigue condicional al ticket P0
  backend §14.1; suma a 18 si llega.

Items absorbidos sin cambios estructurales (refuerzos de las celdas
de PRs existentes con detalles de Rev. 4 del 05):

- UF0-PR2 (UX kit con ConflictDialog): **diff filtrado + "Reaplicar"
  como botón primario default** (F9 sub-decisiones).
- UF2-PR3 (editor de líneas P5): **toggle "Mostrar columnas
  contables"** + tabla scrollea internamente (F1 con baseline
  1366×768).
- UF8-PR1 (soft lock real): ya no es "post-release o pre-release
  según camino"; es **pre-release confirmado**.

Sin cambios en items por fase ni en sizing total. La consolidación
de Rev. 4 se mantiene (16 PRs base + UF8-PR1 = 17 v1).

**Brechas backend nuevas / actualizadas en Rev. 4 del 05**:

- §14.3 elevada de P2 a P1 (CRUD catálogos necesario v1.1, no v2).
- §14.9 nueva (endpoints CRUD catálogos para P11+P12 v1.1).

### Rev. 4 — consolidación de PRs (trabajo entre dos personas) (2026-05-09)

Tras feedback del owner: "los PRs no son muy granulares, recuerda que
es trabajo entre los dos". El breakdown Rev. 3 (26 PRs base, 30 con
todo) estaba pensado para equipo de 2 devs paralelos con review
cruzado, pero el contexto real es **1 dev (Eduardo) + 1 revisor
(Claude)** — la microgranularidad agregaba overhead de proceso (PR +
revisión + commit + push) sin valor de revisión proporcional.

**Consolidación aplicada**: 26 → 16 PRs base (-38%). PRs de alto
riesgo (matriz §6.1, ConflictDialog wireup, E2E config CI, soft lock
SignalR) se mantienen aislados. Backend cerró Compras en 24 PRs con
el mismo patrón (ver 03-pr-breakdown.md Rev. 2).

Cambios concretos por fase:

| Fase | Rev. 3 | Rev. 4 | Consolidación |
|---|---|---|---|
| 0 | 5 | 2 | UF0-PR1 plataforma (HTTP+permisos+shadcn+sidebar); UF0-PR2 UX kit (feedback+breadcrumbs+collaboration stubs+ConflictDialog robusto). |
| 1 | 4 | 2 | UF1-PR1 types+display+hooks read; UF1-PR2 pantallas P1+P3 read-only. |
| 2 | 4 | 3 | UF2-PR1 todos los selectores+fields+schemas; UF2-PR2 P4 nueva con draft; UF2-PR3 P5 editor + matriz (alto riesgo, aislado). |
| 3 | 4 | 2 | UF3-PR1 workflow autorizador completo (bandeja+transmitir+autorizar+rechazar+modal); UF3-PR2 ConflictDialog wireup (aislado por riesgo §14.6 del 05). |
| 4 | 1 | 1 | Sin cambio. |
| 5 | 1 | 1 | Sin cambio. |
| 6 | 2 | 2 | Sin cambio (pantallas independientes). |
| 7 | 6 | 3 + 1 condicional | UF7-PR1 tests; UF7-PR2 E2E aislado (config CI); UF7-PR3 polish completo (a11y+mobile+perf+ayuda+print+runbook+UAT); UF7-PR4 timeline histórico condicional. |
| 8 | 2 | 1 | UF8-PR1 SignalR client + collaboration UI activa cohesiva. |

**Total v1**: 26 → **16 PRs** (sin F8, sin condicionales). Con F8 +
condicional: 18 PRs (vs 30 de Rev. 3). Camino crítico sin cambio
(~2-3 meses con 1 dev) — la consolidación reduce overhead de proceso,
no de trabajo real.

Items absorbidos (Rev. 3 lo separaba; Rev. 4 los une por afinidad):

- Permisos canónicos + cliente HTTP + shadcn primitives + activación
  sidebar = UF0-PR1 (todo es plumbing del módulo).
- Feedback + breadcrumbs + helpers UX + collaboration stubs +
  ConflictDialog = UF0-PR2 (todo es UX kit transversal).
- Types + display + hooks read = UF1-PR1 (capa de datos completa).
- Bandeja + detalle read-only = UF1-PR2 (mismas pantallas, mismo
  riesgo, mismo dev en una sentada).
- Selectores catálogos + selectores org + form fields + schemas =
  UF2-PR1 (toda la base de inputs).
- Bandeja pendientes + transmitir + autorizar + rechazar + modal
  motivos = UF3-PR1 (flujo end-to-end del autorizador).
- Tests unitarios + integration smoke = UF7-PR1 (tests no E2E).
- A11y + mobile + perf + ayuda + print + runbook + UAT = UF7-PR3
  (todo polish para release).
- SignalR client + UI activa + cleanup = UF8-PR1 (soft lock end-to-end).

PRs aislados por riesgo (mantenidos separados):

- UF2-PR3 (matriz §6.1 — lógica más sensible).
- UF3-PR2 (ConflictDialog wireup — salvaguarda crítica de adopción).
- UF7-PR2 (E2E — config CI con blast radius).
- UF8-PR1 (SignalR — nueva infra runtime).

### Rev. 3 — pushbacks del owner sobre F6/F7/F8/F9 (2026-05-09)

Alineado con [05-frontend-diseno.md](05-frontend-diseno.md) Rev. 3
y [06-frontend-plan-implementacion.md](06-frontend-plan-implementacion.md)
Rev. 3. Cambios al breakdown:

- **UF0-PR3 ampliado** (XS → S): el `<ConflictResolutionDialog />`
  ya no es stub simple. Es componente real con preserve-form-state
  (acepta `form` opcional, captura state, refresca, ofrece
  "Reaplicar mis cambios" con diff visual). Crítico para adopción
  según §14.6 del 05.
- **UF3-PR4 ampliado** (S → M): wireup del dialog en TODAS las
  mutaciones que pueden conflictuar, distinguiendo dos modos
  (con/sin form para preservar). Tests E2E del caso 15 líneas
  (capturador + colega que toca cabecera).
- **UF5-PR1 ampliado**: `<CubrimientoBar>` con números visibles al
  lado siempre + patrón visual distintivo (sólido/rayas/punteado/
  cross-hatch) además de color, para WCAG 1.4.1 + táctil sin
  hover. Tooltip queda como detalle adicional. Test axe-core.
- **UF7-PR6 nuevo (condicional)**: cuando el ticket P0 backend de
  `GET /historico` (§14.1 del 05, elevado a P0) cierre, ampliar
  el timeline a TODAS las transiciones del agregado. Si no
  llega, queda en `wontfix` para v1 + riesgo en UAT.
- **Fase 8 reescrita** como "decisión P0 antes de release" con
  tres caminos (A priorizar antes / B post-release con F9 robusto /
  C aceptar riesgo). Decisión del owner antes de iniciar Fase 7.
- **Tabla resumen** actualizada: 26 PRs base + UF7-PR6 condicional
  + 2 PRs de F8 (29-30 con todo).

Sin cambios en sizing total ni en camino crítico (~2–3 meses con
1 dev). Los items adicionales (preserve-form-state, patrón visual,
números) son refuerzos absorbidos en PRs existentes; UF7-PR6 es
nuevo pero condicional al backend.

### Rev. 2 — patrones UX transversales (2026-05-09)

Alineado con [05-frontend-diseno.md](05-frontend-diseno.md) Rev. 2
y [06-frontend-plan-implementacion.md](06-frontend-plan-implementacion.md)
Rev. 2. Cambios al breakdown:

- **UF0-PR5 nuevo** (S): feedback components (`<EmptyState>`,
  `<ErrorState>`, `<TableSkeleton>`, `<DomainTermTooltip>`),
  `<Breadcrumbs>`, `useUnsavedChangesGuard`, diccionario
  `glosario.ts`. Paralelizable a UF0-PR3.
- **UF1-PR3 ampliado**: search params Zod-validados (preservación
  de filtros), estados loading/empty/error explícitos,
  breadcrumbs.
- **UF1-PR4 ampliado**: tratamiento explícito de 403/404 con
  página completa, breadcrumbs con preservación de filtros via
  `useSearch`, tooltips de glosario en badges.
- **UF2-PR3 ampliado**: localStorage draft +
  `useUnsavedChangesGuard` + modal "recuperar borrador" en P4
  cabecera. Tooltips de glosario en campos no obvios.
- **UF2-PR4 ampliado**: `useUnsavedChangesGuard` en `LineaDialog`,
  draft opcional en localStorage por línea.
- **UF7-PR5 nuevo** (S): página de ayuda `/compras/ayuda` con
  glosario + diagrama de ciclo de vida + FAQ; `@media print` para
  P3 detalle (cubre `Ctrl+P` mientras endpoint PDF v1.1 no exista).
- **Renumeración** de referencias al 05: brechas backend ahora
  §14, hallazgos §15. Referencias §13.x del 05 ahora apuntan a la
  nueva sección "Patrones UX transversales".

Total v1: 24 → **26 PRs** (+2 PRs nuevos: UF0-PR5 y UF7-PR5).
Camino crítico sin cambio (~2–3 meses con 1 dev).

### Rev. 1 — versión inicial (2026-05-09)

Primer corte del breakdown de UI Compras. Calibrado contra:

- 05-frontend-diseno.md Rev. 1
- 06-frontend-plan-implementacion.md Rev. 1
- API real auditada en `backend/src/Api/Endpoints/Compras/` y
  `backend/src/Api/Endpoints/Catalogos/` al 2026-05-09
- Stack frontend auditado en `frontend/` al 2026-05-09

24 PRs hasta release v1 (Fase 0 a Fase 7), + 2 post-release (Fase 8
soft lock real). Pendiente de calibración con capacidad real del
equipo de UI.
