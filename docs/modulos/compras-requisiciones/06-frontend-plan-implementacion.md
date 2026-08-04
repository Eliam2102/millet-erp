# Plan de implementación de frontend — Submódulo Requisiciones (Compras)

> **Construido sobre:** [05-frontend-diseno.md](05-frontend-diseno.md) (Rev. 5).
> Granularidad de PRs en [07-frontend-pr-breakdown.md](07-frontend-pr-breakdown.md) (Rev. 6)
> y la base del módulo cerrado en backend
> ([01-diseno.md](01-diseno.md) Rev. 14,
> [02-plan-implementacion.md](02-plan-implementacion.md) Rev. 5,
> [03-pr-breakdown.md](03-pr-breakdown.md) Rev. 2,
> [04-cuidados-infra.md](04-cuidados-infra.md) Rev. 1).
>
> **Estado:** propuesta de plan para revisión con el owner. Sizing en
> bandas (XS/S/M/L/XL) — calibrar contra capacidad real del equipo de
> UI. Sigue el mismo formato que el plan de backend.
>
> **Fecha:** 2026-05-09.

---

## 0. Cómo leer

- Sizing en bandas:
  - **XS** ≈ 1–2 días
  - **S** ≈ 3–5 días
  - **M** ≈ 1–2 semanas
  - **L** ≈ 3–4 semanas
  - **XL** > 1 mes
- Cada fase produce algo **deployable y validable** — no son entregables
  internos del equipo.
- Las fases son secuenciales por dependencia técnica, pero dentro de
  cada fase hay paralelismo posible (anotado como "‖").
- Las dependencias hacia el backend están marcadas en negrita
  (**dep backend §14.x del 05**) y se resuelven con tickets contra ese equipo.

---

## 1. Resumen ejecutivo

**Objetivo:** entregar v1 de la UI del submódulo Requisiciones del
módulo Compras del nuevo ERP, alineado con el diseño aprobado y
contra la API real ya implementada.

**Estrategia:**

1. **Foundation primero**: el repo tiene el shell autenticado, auth,
   layout, query-client wireado, pero faltan piezas del cliente HTTP
   enriquecido (ProblemDetails, Idempotency-Key, ETag), permisos
   canónicos del módulo, y varios primitives shadcn. Fase 0 los trae
   todos juntos antes de cualquier feature funcional.
2. **Read-only antes de mutación**: bandeja + detalle se entregan
   read-only en Fase 1. Esto valida shape de DTOs reales contra el
   código backend (donde no hay codegen ADR-0017 aún) y desbloquea
   la mayor parte de pantallas.
3. **Capturador → Autorizador → Comprador**: el orden de fases
   refleja el flujo de la RQ. El capturador necesita el editor de
   líneas (Fase 2). El autorizador necesita la bandeja específica y
   las acciones de matriz (Fase 3). El comprador es lectura + notas,
   ya cubierto en Fase 1+2.
4. **Soft lock al final**: el `<CollaborationHub>` SignalR es deuda
   de plataforma; UI Compras stubea desde Fase 0 y wirear cuando el
   hub exista (Fase 8).

**Pendientes que NO bloquean arranque pero SÍ bloquean release v1:**

- **`<CollaborationHub>` real** (F8 / §14.6 del 05) — **Camino A
  confirmado por owner Rev. 4 (2026-05-09)**: UF8-PR1 entra antes
  del UAT, no post-release. Total v1 sube a **17 PRs**. Acción
  inmediata: coordinar plazo de cierre con plataforma. Si no hay
  plan claro en 4-6 semanas, fallback automático a Camino B (F8
  difiere a v1.1, F9 robusto compensa).
- **Endpoint `GET /historico` de RQ** (§14.1 del 05) — ticket P0
  al backend (Rev. 3, elevado de P1). Necesario para timeline
  completo en F7. Si no llega, F7 cierra con timeline reducido y se
  documenta como riesgo en UAT.
- ~~Mecanismo de seed inicial de catálogos~~ — **resuelta en Rev. 5**:
  el backend ya entregó CRUD operativo (PR #73). La UI de
  catálogos vive en módulo Datos Maestros, fuera del scope de
  Compras. v1 de Compras sale solo con selectores read-only que
  consumen los GET ya disponibles.

**Pendientes que NO bloquean arranque:**

- Codegen ADR-0017 (`api-types.ts` desde OpenAPI) — UI Compras vive
  con DTOs mirroreados manualmente en `features/compras/api/types.ts`
  hasta que plataforma cierre el ticket. Cuando exista, sustitución
  trivial.
- CRUD de catálogos editable — fuera del alcance del módulo
  Requisiciones (ver §14.3 del 05); P10 sí entrega reclasificación
  de naturaleza.

**Pendientes que SÍ bloquean fases específicas:**

- **Selectores Sucursal/Departamento/Almacén/Usuario** (§14.7 del
  05): bloquean Fase 2 (P4 cabecera) y Fase 6 (P9 aprobadores).
  Antes de arrancar Fase 2 hay que confirmar si los endpoints
  existen.
- **`PATCH /requisiciones/{id}` para editar cabecera** (§14.2 del 05):
  bloquea la flexibilidad de Fase 2 — sin esto, el flujo "creo
  cabecera y luego la corrijo" requiere eliminar y recrear. Aceptable
  si el backend entrega el endpoint en paralelo.
- **`me` con `departamentoId`** (§14.8 del 05): bloquea el filtro automático
  de bandeja por depto del usuario (Fase 1). Workaround: filtro
  visible pero vacío por default.

---

## 2. Prerrequisitos — audit del repo (2026-05-09)

Auditado contra
[c:\Users\UserSP\Desktop\Project_Millet_ERP\frontend\](../../../frontend/).
Estado real:

| Prerrequisito | Estado | Detalle / plan B |
|---|---|---|
| React 19 + TypeScript 6 + Vite 8 | ✅ existe | [package.json](../../../frontend/package.json) |
| Tailwind v4 (`@tailwindcss/vite`) | ✅ configurado | tokens corporativos via theme |
| shadcn/ui inicializado | ✅ existe | primitives copiados: avatar, button, dropdown-menu, input, separator, tooltip ([components/ui/](../../../frontend/src/components/ui/)) |
| TanStack Router 1.169 file-based + plugin Vite | ✅ wireado | `routeTree.gen.ts` autogenerado, `createFileRoute` en cada ruta |
| TanStack Query 5.100 + DevTools | ✅ wireado | [query-client.ts](../../../frontend/src/lib/query-client.ts) con `staleTime: 30s`, no retry 4xx |
| Zustand 5.0 | ✅ usado | [auth-store.ts](../../../frontend/src/lib/auth/auth-store.ts) sin persist |
| react-hook-form + `@hookform/resolvers` + Zod | ✅ instalados, sin uso real | sin formularios complejos todavía |
| MSAL (Entra ID, ADR-0003) | ✅ wireado | [AuthBootstrap.tsx](../../../frontend/src/lib/auth/AuthBootstrap.tsx) |
| Cliente HTTP base (`apiFetch`) con auth | ✅ existe | [api-client.ts](../../../frontend/src/lib/auth/api-client.ts) — inyecta `Bearer`, limpia sesión en 401 |
| Layout `_app` con guard de auth | ✅ existe | [routes/_app.tsx](../../../frontend/src/routes/_app.tsx) |
| AppShell (sidebar + topbar) | ✅ existe | [AppShell.tsx](../../../frontend/src/components/layout/AppShell.tsx) |
| Sidebar con `disabled: true` para Compras | ✅ existe | [nav.ts](../../../frontend/src/lib/nav.ts) — listo para activar |
| Hooks de permisos (`useHasPermission`, `useHasAnyPermission`) | ✅ existe | [useHasPermission.ts](../../../frontend/src/lib/auth/useHasPermission.ts) |
| Componente `<RequirePermission>` | ✅ existe | [RequirePermission.tsx](../../../frontend/src/components/auth/RequirePermission.tsx) |
| `formatMoney` / `formatDateTime` (es-MX, TZ MX) | ✅ existe | [datetime.ts](../../../frontend/src/lib/datetime.ts), [money.ts](../../../frontend/src/lib/money.ts) |
| **`api-types.ts` codegen** (ADR-0017) | ❌ no existe | F0 mirror manual |
| **Cliente HTTP enriquecido** (ProblemDetails, Idempotency, ETag) | ❌ no existe | F0 lo introduce en `lib/api/` |
| **Permisos canónicos del módulo** en frontend | ❌ no existe | F0 los agrega a `permission-codes.ts` |
| **Componentes shadcn faltantes** (Dialog, Select, Combobox, DataTable, Form, Toast, Calendar, Card, Badge, Skeleton) | ❌ no copiados | F0 los copia con `npx shadcn add` |
| **`components/erp/forms/`, `display/`, `selectors/`, `collaboration/`** | ❌ vacíos (`.gitkeep`) | F0+ los pueblan según necesidad |
| **`features/compras/`** | ❌ no existe | F0 crea estructura |
| `CollaborationHub` SignalR (ADR-0001/0012 Capa 2) | ⏳ pendiente plataforma | F0 stubea `useCollaboration`, F8 conecta |
| **Endpoints backend de Compras** | ✅ todos los de §9 del 01-diseno excepto `PATCH /{id}` (§14.2 del 05, P1) y **`GET /historico`** (§14.1 del 05, **P0 elevado en Rev. 3** — ticket inmediato al backend) | confirmados en código |
| **Endpoints catálogos compartidos** | ✅ proveedores y artículos read-only + reclasificar-naturaleza | [CatalogosEndpoints.cs](../../../backend/src/Api/Endpoints/Catalogos/CatalogosEndpoints.cs) |
| **Endpoints catálogos org** (Sucursal/Depto/Almacén/Usuario) | **[Verificar §14.7 del 05]** | bloqueante para F2 cabecera |

**Conclusión del audit:** la base del frontend está sólida. Las
brechas son del propio scope del módulo Compras (mirror de permisos,
cliente HTTP enriquecido, componentes shadcn faltantes, features) —
todas se cubren en Fase 0. **Solo bloqueante:** §14.7 del 05 (selectores
org) que el backend debe entregar antes/durante Fase 2.

> **Tracking de deuda de plataforma** (ADR-0031): los stubs de Fase 0
> (`<CollaborationIndicator />` que renderiza `null` mientras
> `<CollaborationHub>` no exista) se documentan en la sección §8.6 del
> diseño 01 con `PLATFORM-TODO(<CollaborationHub>)` y se borran
> cuando el ticket cierre.

---

## 3. Dependencias con otros equipos / módulos

| Equipo | Necesidad | Estado | Plan |
|---|---|---|---|
| **Backend Compras** | endpoints estables (`/requisiciones`, `/lineas`, `/aprobadores`, `/motivos-rechazo`, `/pendientes-autorizacion`) | ✅ todos mergeados a `main` | usar tal cual |
| **Backend Catálogos** | `/api/v1/catalogos/{proveedores,articulos}` read + reclasificar naturaleza | ✅ mergeado | usar tal cual |
| **Backend Identidad** | `/me` con `departamentoId`, listado de usuarios para selectores | **[Verificar §14.7/14.8 del 05]** | F2/F6 bloqueadas hasta confirmar |
| **Backend Catálogos org** | listado de Sucursal/Depto/Almacén | **[Verificar §14.7 del 05]** | F2 bloqueada hasta confirmar |
| **Plataforma — Codegen ADR-0017** | `api-types.ts` desde OpenAPI | ⏳ pendiente | F0 mirroring manual; sustitución trivial cuando llegue |
| **Plataforma — `CollaborationHub` SignalR** | hub real para soft locks (ADR-0012 Capa 2) | ⏳ deuda `<CollaborationHub>` | F0 stub; F8 wirear cuando exista |
| **UX / Diseño** | revisión de mockups antes de implementación de cada pantalla | depende del equipo | sesión de review tras cada fase |
| **Cliente** | validación de asunciones F1–F15 | pendiente | sesión inicial antes de F0; sesiones de UAT por hito |

---

## 4. Fases

### Fase 0 — Foundation UI Compras (S)

Crear el plumbing del módulo. **Sin features funcionales para el
usuario final**, pero todo lo que las siguientes fases necesitan.

- [ ] Agregar permisos canónicos de Compras + Catálogos al mirror
      [permission-codes.ts](../../../frontend/src/lib/auth/permission-codes.ts)
      (los 11 permisos del 05 §10.2). Test que compara contra
      `/api/v1/identidad/permisos` (cuando ese endpoint exista) o
      contra `PermisosCanonicos.cs` con un check de naming.
- [ ] Crear `lib/api/` con:
  - `client.ts`: `apiRequest<T>(...)` que extiende `apiFetch`,
    parsea `application/problem+json`, captura `ETag` de respuesta.
  - `error.ts`: clase `ApiError`, helpers `esApiError`,
    `esConflictoConcurrencia`, `esIdempotencyInProgress`,
    `esValidacion`.
  - `idempotency.ts`: `useFormIdempotencyKey()`,
    `addIdempotencyHeader()`. Manejo automático de 409
    `IDEMPOTENCY_IN_PROGRESS` con respeto a `Retry-After`.
  - `etag.ts`: `extractEtag()`, helper para enviar `If-Match`.
- [ ] `applyServerErrors()` helper (`lib/api/apply-server-errors.ts`)
      que mapea `ProblemDetails.errores[]` → `form.setError`.
- [ ] Componentes shadcn faltantes vía `npx shadcn add`: `dialog`,
      `select`, `combobox`/`command`, `data-table` (si no existe,
      build encima de `@tanstack/react-table`), `form`, `toast`,
      `calendar`+`popover`, `card`, `badge`, `skeleton`, `alert`.
- [ ] Stub de `<CollaborationIndicator />` en
      `components/erp/collaboration/` (renderiza `null`, hook
      `useCollaboration` retorna `[]`). Comentario
      `PLATFORM-TODO(<CollaborationHub>)`.
- [ ] **`<ConflictResolutionDialog />` v1 con preserve-form-state**
      (F9 Rev. 3) en `components/erp/collaboration/`. **NO es un
      stub simple**: el dialog acepta `form` opcional como prop;
      cuando se le pasa, captura el form state local antes de
      refrescar, muestra diff visual (cambios remotos vs cambios
      locales) y ofrece botón **"Reaplicar mis cambios"** que
      repopula el form post-refresh. Sin form prop, queda en modo
      simple "refrescar y revisar". +1-2 días de trabajo vs versión
      simple, **crítico** para la confianza del sistema (ver §14.6
      del 05 sobre el riesgo combinado con F8 NoOp). Tests
      unitarios: ambos modos.
- [ ] **Componentes de feedback** (§13.1 del 05) en
      `components/erp/feedback/`: `<EmptyState>`, `<ErrorState>`
      (recibe `problem: ProblemDetails` + `onRetry`), `<TableSkeleton>`.
      Patrón estándar para todas las pantallas siguientes.
- [ ] **`<Breadcrumbs>`** (§13.9 del 05) en `components/erp/` —
      reusable, lee de array `items` con `to` opcional para preservar
      search params via `useSearch`.
- [ ] **`useUnsavedChangesGuard(isDirty)`** hook (§13.2 del 05) en
      `lib/hooks/` — wrapper de `beforeunload` listener para
      cualquier form con cambios sin guardar.
- [ ] **`<DomainTermTooltip>`** (§13.7 del 05) en
      `components/erp/feedback/` o `components/erp/display/` que lee
      de `features/compras/lib/glosario.ts` (semilla con los términos
      `Naturaleza`, `Cubrimiento`, los 8 estados, `Matriz`,
      `Bifurcación`, `Reserva`).
- [ ] Toast provider wireado en `_app.tsx` (sonner o equivalente
      del shadcn ecosystem).
- [ ] Activar el item "Compras" en
      [nav.ts](../../../frontend/src/lib/nav.ts) (`disabled: false`,
      `permission: 'compras.requisiciones.leer'`). Crear shell
      vacío de la ruta `routes/_app/compras/index.tsx` que redirige
      a `/compras/requisiciones` (placeholder hasta F1).
- [ ] Crear estructura de carpetas: `features/compras/`,
      `features/catalogos/`. `.gitkeep` en subcarpetas vacías.

**Criterio de aceptación:** un dev de UI puede importar `apiRequest`,
`useFormIdempotencyKey`, `ApiError`, `<RequirePermission>` y los
componentes shadcn listados, y pre-empacar un POST con
`Idempotency-Key`. El sidebar muestra "Compras" para usuarios con el
permiso. Click en Compras lleva a un placeholder vacío (sin error).

**Sin riesgo de romper main**: cambios aditivos.

---

### Fase 1 — Read-only de Requisiciones (M)

Bandeja general + detalle, ambas read-only. Antes de cualquier
mutación, validamos los DTOs reales del backend.

‖ paralelizable: F0 ya cerrada; un dev arranca la bandeja, otro el
detalle.

- [ ] Mirror de DTOs en `features/compras/api/types.ts`:
  - `RequisicionListItemResponse` (de la bandeja)
  - `RequisicionResponse` (detalle, con líneas, autorizaciones,
    cubrimiento — **[Verificar §15.7 del 05]** que el shape incluye
    cubrimiento)
  - `MotivoRechazoResponse`
  - `PagedResponse<T>`
  - Enums: `EstadoRequisicion`, `Clasificacion`, `Prioridad`,
    `NivelAutorizacion`, `Naturaleza`, `EstatusCatalogo`.
- [ ] Hooks `features/compras/api/`:
  - `useRequisiciones(filtros)` — `GET /api/v1/compras/requisiciones`
    con offset+limit. Query key `comprasKeys.requisicionesList(filtros)`.
  - `useRequisicion(id)` — `GET /api/v1/compras/requisiciones/{id}`,
    extrae y guarda `etag` en `query.meta`.
  - `useMotivosRechazo()` — staleTime 1h.
- [ ] Componentes ERP:
  - `<EstadoBadge>` (display) — colores por estado.
  - `<NaturalezaBadge>` (display) — colores por naturaleza.
  - `<MoneyDisplay>` (display) — wrap de `formatMoney`.
  - `<DateTimeDisplay>` (display) — wrap de formatos.
- [ ] **P1 — Bandeja general** (`routes/_app/compras/requisiciones/index.tsx`):
  - **Search params validados con Zod** (§13.9 del 05): `estado`,
    `departamentoId`, `requisitanteId`, `q`, `offset`, `limit`.
    Filtros se reflejan en URL → preservados al volver del detalle.
  - Tabla con columnas: folio, fecha, requisitante, depto, monto
    total, estado, acciones (solo "Ver").
  - Filtros: estado (dropdown), depto (selector — bloqueado si
    §14.7 del 05 no resuelto), búsqueda por folio (filtro
    client-side sobre la página actual; la API no expone `q`
    server-side, ver §13.4 del 05).
  - Paginación offset-based (50 default, máx 200 backend).
  - **Estados (§13.1 del 05)**: `<TableSkeleton>` durante loading,
    `<EmptyState>` con CTA "Nueva requisición", `<ErrorState>` con
    Reintentar.
  - **Breadcrumbs**: `Compras / Requisiciones`.
- [ ] **P3 — Detalle (read-only)** (`routes/_app/compras/requisiciones/$id.tsx`):
  - Cabecera: folio, estado badge, fechas, requisitante, depto,
    almacén destino, prioridad, descripción.
  - Lista de líneas: artículo (clave + nombre), naturaleza badge,
    cantidad, UM, precio estimado (Money), cuenta contable, centro
    costo, proyecto, fecha requerida, notas.
  - Lista de autorizaciones: nivel, usuario, fecha, notas
    (`<TimelineAutorizaciones>` componente nuevo).
  - Stub de `<CubrimientoBar>` (renderiza solo `CantidadOriginal`
    hasta confirmar §15.7 del 05).
  - **Sin botones de acción** (esos vienen en F2/F3/F4).
  - **Estados (§13.1 del 05)**: skeleton de cabecera + N filas
    durante loading.
  - **403 / 404 (§13.6 del 05)**: 403 página completa con candado +
    "No tienes permiso..."; 404 página neutra "Esta requisición no
    existe o no pertenece a tu empresa actual." Sin distinguir
    inexistente vs cross-empresa. CTA "Volver a bandeja" preserva
    search params si vino de bandeja.
  - **Breadcrumbs**: `Compras / Requisiciones / <folio>` (click en
    "Requisiciones" preserva filtros via `useSearch`).
  - **Tooltips de glosario** (§13.7 del 05) en
    `<EstadoBadge>`/`<NaturalezaBadge>` desde su primera aparición.

**Criterio de aceptación:** un usuario con permiso entra a
`/compras/requisiciones`, ve sus RQs (las de su empresa), pagina, y
abre el detalle de una. Cualquier discrepancia entre el DTO real y
el mirror se detecta acá (TypeScript compile error).

**Riesgo:** medio. La bandeja con dataset grande (10k seed de
F8-PR3 backend) no debería degradar — el backend ya optimizó
índices. Si P95 > 200ms, evaluar.

---

### Fase 2 — Crear y editor de líneas (M)

Activa el flow del capturador hasta listo-para-transmitir.

‖ paralelizable: el dev que cierra F1 sigue con el editor de
líneas; otro arranca el form de cabecera.

> **Bloqueada por §14.7 del 05** (selectores org). Si los endpoints
> existen, arranca; si no, escalar al backend antes.

- [ ] Selectores ERP (`components/erp/selectors/`):
  - `<ArticuloSelector>` — combobox lazy contra
    `GET /api/v1/catalogos/articulos`, filtra `estatus=Activo`.
    Muestra clave + nombre + naturaleza badge.
  - `<ProveedorSelector>` — análogo a artículo.
  - `<MotivoRechazoSelector>` — filtra por bitmask `aplicaA`
    según el flujo (rechazo/eliminación/cancelación). Si el motivo
    `permiteTextoLibre`, exige textarea.
  - `<DepartamentoSelector>`, `<SucursalSelector>`,
    `<AlmacenSelector>`, `<UsuarioSelector>` — **dependen de §14.7 del 05**.
- [ ] Componentes ERP de form (`components/erp/forms/`):
  - `<MoneyField>` — input con formato local, parsea string a
    `{amount, currency}`.
  - `<DatePickerField>` — popover Calendar. Convierte ISO local →
    UTC al submit (usar `parseLocalToUtc`).
  - `<DecimalField>` — input numérico con N decimales máx.
  - `<TextAreaField>` — autosize.
- [ ] Schemas Zod (`features/compras/schemas/`):
  - `crear-requisicion.ts`
  - `linea.ts` (agregar y actualizar comparten shape)
  - `notas-linea.ts`
- [ ] Hooks de mutación:
  - `useCrearRequisicion()` — POST con `Idempotency-Key`.
  - `useAgregarLinea()` — POST.
  - `useActualizarLinea()` — PATCH (sin Idempotency-Key, ver §7.6 del 05).
  - `useActualizarNotasLinea()` — PATCH (con optimistic update).
  - `useEliminarLinea()` — DELETE.
- [ ] **P4 — Nueva requisición (cabecera)**:
  - Form con todos los selectores org + clasificación + prioridad
    + descripción + fecha entrega deseada + proveedor sugerido
    (opcional) + requisitante (solo si tiene
    `seleccionar-requisitante`).
  - Submit → POST con idempotency → redirige a `/compras/requisiciones/$id`.
  - Validación: errores de servidor mapeados con `applyServerErrors`.
  - **Draft protection (§13.2 del 05)**: `useUnsavedChangesGuard`
    activo si form `isDirty`. Borrador en `localStorage` con key
    `compras:rq:draft:nueva:<userId>:<empresaId>`, debounce 500ms.
    Modal "Tienes un borrador guardado de hace X. ¿Recuperar?" si
    existe al montar. Borrar tras submit exitoso.
  - **Tooltips de glosario** (§13.7 del 05) en campos no obvios
    (clasificación, prioridad, almacén destino).
  - **Breadcrumbs**: `Compras / Requisiciones / Nueva`.
- [ ] **P5 — Editor de líneas (sección de P3 detalle)**:
  - En `Borrador`: tabla editable inline. Botón "Agregar línea"
    abre dialog con form. Pencil icon en cada fila → dialog edit.
    Trash icon → confirm → DELETE.
  - En `EnAutorizacion`/`Autorizada`/`EnSurtido`: read-only excepto
    notas (textarea inline editable con optimistic update).
  - En terminales: read-only completo.
  - Lógica condicional vía
    `features/compras/lib/acciones-disponibles.ts` (§6 del 05).
  - **Estados (§13.1 del 05)**: skeleton de filas durante loading,
    empty state "Esta requisición no tiene líneas. Agrega la
    primera." (en `Borrador`) o "Sin líneas registradas." (read-only).
  - **Draft protection en `LineaDialog`** (§13.2 del 05):
    `useUnsavedChangesGuard` mientras el dialog tiene cambios
    sin guardar. Draft opcional en localStorage con key
    `compras:rq:linea-draft:<requisicionId>:<lineaId|new>`.

**Criterio de aceptación:** un capturador crea una RQ desde cero,
agrega 3 líneas con artículos del catálogo seed, edita una, elimina
otra. La RQ aparece en su bandeja. **Sin transmitir todavía** — eso
es Fase 3.

**Riesgo:** alto si §14.7 del 05 no se resuelve a tiempo. Sin selectores
org, P4 no funciona.

---

### Fase 3 — Transmitir + bandeja autorizador + aprobar/rechazar (M)

Activa el flow del autorizador.

‖ paralelizable: la bandeja P2 y las acciones N1/N2 pueden ir en
paralelo (mismo dev o dos).

- [ ] Hooks:
  - `useTransmitirRequisicion()` — POST `/transmitir`. Idempotency-Key.
  - `useAutorizarRequisicion()` — POST `/autorizaciones`. Idempotency-Key.
    Body: `{ Nivel, Notas? }`.
  - `useRechazarRequisicion()` — POST `/rechazar`. Idempotency-Key.
    Body: `{ MotivoId, MotivoTexto? }`.
  - `usePendientesAutorizacion(filtros)` — GET `/pendientes-autorizacion`.
- [ ] Schemas Zod: `autorizar.ts`, `terminar-requisicion.ts`.
- [ ] **P2 — Bandeja de pendientes de autorización**:
  - Misma estructura que P1 pero filtrada por `EnAutorizacion`.
  - Visibility del menú: gateado por
    `useHasAnyPermission(['compras.requisiciones.autorizar.nivel1',
    'compras.requisiciones.autorizar.nivel2'])`.
  - Filtro por departamento (si tiene `ver-todos-departamentos`).
- [ ] **Acciones en P3 detalle**:
  - Botón "Transmitir" en `Borrador` (gateado por `editar`,
    deshabilitado si 0 líneas con tooltip "Agrega al menos una
    línea").
  - Botón "Aprobar Nivel1" en `EnAutorizacion` (gateado por
    `autorizar.nivel1`, deshabilitado si ya firmaste).
  - Botón "Aprobar Nivel2" en `EnAutorizacion` (gateado por
    `autorizar.nivel2`, deshabilitado si N1 no firmado o ya
    firmaste con tooltip explicativo).
  - Botón "Rechazar" en `EnAutorizacion` (gateado por `rechazar`).
  - Cada acción → confirm dialog estándar (Aprobar muestra resumen
    "Vas a aprobar Nivel1 de RQ MID2026-000042"; Rechazar abre P6).
- [ ] **P6 — Modal de motivos**:
  - `<MotivoRechazoSelector>` con `aplicaA=Rechazo`.
  - Textarea condicional según `permiteTextoLibre`.
  - Submit → mutation correspondiente.
- [ ] Manejo de 409 `CONCURRENCY_CONFLICT` → abre
      `<ConflictResolutionDialog />` v1.

**Criterio de aceptación:** un capturador transmite. Un autorizador
N1 entra a su bandeja, ve la RQ, la aprueba. Si la matriz se
satisface (caso seed: monto bajo), la RQ pasa a `Autorizada` o
`EnSurtido` (según haya stock en el stub). Si requiere N2, queda en
`EnAutorizacion` y el N2 la ve en su bandeja. Rechazo con motivo
funciona y la RQ termina en `Rechazada`.

**Riesgo:** medio. La bandeja del autorizador es UX crítica —
asegurar que el filtrado por permiso es claro (no mostrar RQs que
el autorizador no puede aprobar).

---

### Fase 4 — Cancelar y eliminar (S)

Cierra los terminales que faltaban.

- [ ] Hooks:
  - `useCancelarRequisicion()` — POST `/cancelar`. Idempotency-Key.
  - `useEliminarRequisicion()` — POST `/eliminar`. **NO** Idempotency-Key
    (decisión backend, ver §7.6 del 05).
- [ ] **Acción "Cancelar"** en P3:
  - Visible en `Autorizada` o `EnSurtido` (gateado por `cancelar`).
  - Abre P6 con `aplicaA=Cancelacion`.
  - Toast de éxito + invalida queries (la cancelación libera
    reservas y aborta OC borrador en el handler; la UI lo refleja
    automáticamente al refrescar).
- [ ] **Acción "Eliminar"** en P3:
  - Visible en `Borrador` o `EnAutorizacion` (gateado por
    `eliminar`).
  - Abre P6 con `aplicaA=Eliminacion`.
- [ ] Toast del flujo `CANCELAR_FALLO` (422): mensaje específico
      con `traceId` + CTA reintentar.

**Criterio de aceptación:** un autorizador cancela una RQ
`Autorizada` con motivo. La RQ queda `Cancelada`. Un capturador
elimina una `Borrador`. La RQ queda `Eliminada` (no físicamente
borrada). Ambas siguen visibles en bandeja con su badge terminal.

**Riesgo:** bajo.

---

### Fase 5 — Cubrimiento visible (S)

Hace visible la pieza más distintiva del modelo: la bifurcación
stock-aware.

> **Pre-requisito**: confirmar §15.7 del 05 (que `RequisicionResponse`
> incluye `cantDeAlmacen`, `cantDeCompra`, `cantRecibida` por línea).
> Si no, abrir ticket backend antes de arrancar.

- [ ] **`<CubrimientoBar>`** (display) — barra horizontal segmentada
      **+ números visibles + patrón visual** (F6 Rev. 3):
  - 4 segmentos: almacén (verde, sólido) / pendiente recepción
    (amarillo, rayas) / recibido (azul oscuro, punteado) /
    pendiente compra (gris, cross-hatch). **Patrón visual
    distintivo además de color** para WCAG 1.4.1 + accesibilidad
    en táctil.
  - **Números visibles al lado de la barra siempre** (no solo en
    tooltip): "10 total · 3 alm · 5 OC (2 rec) · 2 pend". El dato
    no depende de hover.
  - Tooltip con desglose extendido y porcentajes queda como detalle
    adicional para contextos desktop con mouse.
  - Accesibilidad: `aria-label` con la info numérica completa,
    `role="img"`, contraste de cada segmento ≥ 4.5:1 contra el
    fondo.
- [ ] Wirear en P5 detalle de cada línea reemplazando el stub de F1.
- [ ] Resumen agregado en cabecera de P3: "Cubrimiento global:
      X% de líneas cerradas" (componente pequeño).

**Criterio de aceptación:** una RQ `EnSurtido` muestra una barra
visual por línea con las 4 cantidades correctamente. Cuando llega
una recepción (que dispara `RegistrarRecepcionCommand` desde el
módulo OC), la barra se actualiza al refresh.

**Riesgo:** bajo.

---

### Fase 6 — Admin de aprobadores (S)

Pantalla administrativa específica del workflow de Compras. Bajo
gating estricto de permisos. **P10 reclasificar naturaleza salió
del scope** en Rev. 5 → vive en módulo Datos Maestros (es operación
sobre `compartido.articulos`, transversal al ERP).

- [ ] Hooks:
  - `useAprobadoresVigentes(filtros)`, `useAprobadoresHistorico(filtros)`.
  - `useDesignarAprobador()`, `useRevocarAprobador()`.
- [ ] Schemas: `designar-aprobador.ts`.
- [ ] **P9 — Admin de aprobadores** (`/compras/admin/aprobadores`):
  - Tab "Vigentes": tabla con filtros (depto, rol, usuario).
  - Botón "Designar" → dialog con form (DepartamentoSelector +
    `RolAprobador` selector + UsuarioSelector + motivo opcional).
  - Botón "Revocar" en cada fila → confirm → DELETE.
  - Tab "Histórico": exige al menos un filtro (form lo previene
    inline; backend devuelve 422 `FILTRO_OBLIGATORIO` como defensa
    en profundidad).
  - Gateado por `compras.aprobadores.administrar` (item del menú
    oculto sin el permiso).

**Criterio de aceptación:** el admin captura aprobadores en un depto
con los 3 roles (JefeDpto, JefeAlmacen, AutorizadorN2). Si redesigna
al mismo usuario, no-op. Si redesigna a otro, el backend cierra el
vigente y crea uno nuevo. El histórico muestra ambas filas.

**Riesgo:** bajo (un solo CRUD contra endpoint estable).

---

### Fase 7 — Hardening v1 (M)

Lo que falta para release v1. Calibrar contra capacidad real.

- [ ] **Tests con Vitest + React Testing Library**:
  - Componentes ERP (selectores, badges, CubrimientoBar).
  - `acciones-disponibles.ts` parametrizado contra la tabla §6.1
    del 05 (cada celda).
  - Hooks de mutación con `QueryClientProvider` envolvente y MSW
    para mocks HTTP.
  - Coverage objetivo: > 70% de `features/compras/`.
- [ ] **E2E con Playwright** (si el equipo tiene capacidad):
  - Happy path capturador → autorizador N1 → autorizada → comprador
    notas.
  - Error path: rechazo con motivo, conflicto 409 (vía test que
    fuerza dos autorizaciones simultáneas mockeadas).
- [ ] **Accessibility audit con axe-core**:
  - Pasar axe en cada pantalla.
  - Validar contraste de `<EstadoBadge>` y `<NaturalezaBadge>` ≥ 4.5:1.
  - Tab order razonable en P5 editor de líneas.
- [ ] **Mobile P2** (asunción F2):
  - Layout responsive de `/compras/pendientes` con cards apilables
    < 768px.
  - Sidebar toggle hamburger en topbar para mobile (componente
    nuevo).
- [ ] **Performance**:
  - Bandeja con seed 10k RQs: P95 de render < 1s.
  - Lighthouse score ≥ 90 en Performance.
  - Bundle audit: confirmar tree-shaking de TanStack y shadcn.
- [ ] **Documentación de patrones** en `frontend/CLAUDE.md` (o
      sección nueva): cómo agregar un endpoint, cómo manejar errores,
      convenciones de query keys, dónde van los componentes ERP.
- [ ] **Página de ayuda** (§13.7 del 05) en `/compras/ayuda`:
      glosario de términos, diagrama del ciclo de vida (8 estados),
      tabla simplificada de matriz de aprobación, FAQ corto. MDX o
      markdown servido como string. Versionada con
      [01-diseno.md](01-diseno.md).
- [ ] **Stylesheet de impresión** (§13.8 del 05) para P3 detalle:
      `@media print` que oculta sidebar/topbar/acciones, formatea
      cabecera + líneas + cubrimiento + autorizaciones en A4
      portrait, con membrete simple (logo + folio + fecha + usuario
      que imprime). Cubre el caso `Ctrl+P` mientras endpoint PDF
      v1.1 no exista.
- [ ] **Timeline completo** (F7 Rev. 3, depende de §14.1 del 05):
      cuando el ticket P0 al backend de
      `GET /api/v1/compras/requisiciones/{id}/historico` cierre,
      ampliar `<TimelineAutorizaciones>` (rebautizar a
      `<TimelineRequisicion>`) para mostrar todas las transiciones
      del agregado, no solo autorizaciones. Hook
      `useHistoricoRequisicion(id)` con staleTime 5min. Si el
      endpoint NO está disponible al llegar a F7, el plan de UAT
      lo lista como riesgo de adopción explícito y se difiere a
      v1.1.
- [ ] **UAT con grupo piloto** del cliente (1–2 capturadores +
      1 autorizador + admin).

**Criterio de aceptación:** UAT firmado. Tests verde en CI. Sin
issues bloqueantes de accesibilidad. Listo para release a piloto.

**Riesgo:** medio. UAT puede revelar ajustes de UX no anticipados.

---

### Fase 8 — Soft lock real (S, decisión P0 antes de release)

Activa la Capa 2 de ADR-0012 cuando el hub esté disponible.

> **Decisión P0 antes de release v1** (Rev. 3, ver §14.6 del 05):
> técnicamente no bloquea release porque la Capa 1
> (`Version`/`IsConcurrencyToken`) protege los datos, pero el
> riesgo combinado con F9 (sin awareness, dos usuarios chocan al
> guardar) es destructor de adopción en day-1. Tres caminos:
>
> - **Camino A (recomendado si la deuda cierra a tiempo)**:
>   `<CollaborationHub>` cierra antes de Fase 7, y Fase 8 se ejecuta
>   **antes del UAT** como parte de release v1. Esto convierte F8
>   en infraestructura activa desde día 1.
> - **Camino B (fallback)**: `<CollaborationHub>` no cierra a
>   tiempo. F9 robusto (preserve form state, ya cubierto en
>   UF0-PR3 y UF3-PR4) compensa parcialmente. Fase 8 se ejecuta
>   post-release como originalmente planeado.
> - **Camino C (último recurso)**: ni A ni B disponibles. Documentar
>   como riesgo de adopción explícito en plan de UAT y comunicar al
>   cliente la limitación de v1.
>
> Decisión final del owner antes de iniciar Fase 7.

- [ ] Cliente SignalR en `lib/signalr.ts` (extiende ADR-0001 cuando
      llegue).
- [ ] Hook real `useCollaboration(entidad, id)` que reemplaza el stub.
- [ ] `<CollaborationIndicator />` activo: avatares apilados,
      badges, tooltips.
- [ ] Banner sutil en P3 cuando "alguien edita".
- [ ] Invalidación reactiva de queries en eventos del hub
      (`requisicion.actualizada`, etc.).
- [ ] Borrar comentarios `PLATFORM-TODO(<CollaborationHub>)` y
      actualizar §8.6 del 01-diseno.md.

**Criterio de aceptación:** dos usuarios abriendo la misma RQ se ven
mutuamente. Cuando uno edita, el otro ve el banner. Si guardan en
conflicto, el `<ConflictResolutionDialog />` con preserve-form-state
de F9 se abre.

**Riesgo:** bajo si camino A (es polish y la lógica crítica ya
estaba cubierta). Medio si camino B (la sin-awareness sigue activa
hasta v1.1). Alto si camino C (sin mitigación en day-1).

---

### Fase 9 (post-v1) — Items diferidos

- **Bulk operations** (asunción A13): aprobar varias RQs desde la
  bandeja N1/N2. Espera que backend entregue
  `POST /autorizaciones-batch`.
- **Adjuntos** (asunción A11, v1.1 backend): UI de upload con blob
  storage.
- **Conflict merge automático** (v1.1 ADR-0012): el dialog v1
  refresca; v1.1 ofrece merge campo-a-campo cuando no hay solape.
- **CRUD de catálogos** (proveedores y artículos): cuando el módulo
  de admin de catálogos exista en backend.
- **Admin de umbrales por departamento**: cuando backend entregue
  endpoints (§14.4 del 05).
- **i18n** (multi-locale): si el cliente lo pide.
- **Editar cabecera de RQ tras crearla**: cuando backend entregue
  `PATCH /requisiciones/{id}` (§14.2 del 05).
- **`GET /historico` de RQ**: cuando el backend lo exponga (§14.1
  del 05).
- **Búsqueda global por folio** (§13.4 del 05): topbar input con
  `Cmd+K`, requiere endpoint `/buscar?q=` server-side.
- **Notificaciones in-app** (§13.5 del 05): badge en topbar con
  contador de pendientes, requiere SignalR (`<CollaborationHub>`)
  o endpoint de summary del usuario.
- **Exportación a PDF** (§13.8 del 05): endpoint backend
  `/{id}/pdf` con QuestPDF + plantilla con membrete; botón
  "Descargar PDF" en P3.

---

## 5. Riesgos y mitigaciones

| Riesgo | Probabilidad | Impacto | Mitigación |
|---|---|---|---|
| Selectores org (Sucursal/Depto/Almacén/Usuario) tardan en backend | media | alto | Verificar §14.7 del 05 antes de F2. Si no existen, escalar al owner para abrir ticket backend prioritario. F1 procede igual. |
| Shape de DTOs reales difiere del mirror manual | media | medio | F1 lo cataliza: TypeScript falla en compile y se ajusta en el mismo PR. Después del codegen ADR-0017, riesgo desaparece. |
| `RequisicionResponse` no incluye cubrimiento (§15.7 del 05) | baja | medio | F5 condicional. Si el shape no lo incluye, abrir ticket backend antes de F5. Mientras tanto, F5 stubea. |
| **Conflicto + sin awareness = pérdida de trabajo en day-1** (F8 NoOp + F9 destructivo, ver §14.6 del 05) | **alta si camino C** | **alto** | Camino A: priorizar `<CollaborationHub>` antes de release v1. Camino B: F9 robusto con preserve form state (UF0-PR3 + UF3-PR4) compensa parcialmente. Camino C: documentar como riesgo P0 explícito en plan de UAT. **Decisión del owner antes de Fase 7.** |
| Endpoint `GET /historico` no se prioriza en backend (§14.1 del 05) | media | medio | Ticket P0 al backend. Si no llega antes de F7, timeline limitado a `Autorizaciones` del detalle; documentar como riesgo de adopción en UAT. F1-PR4 entrega timeline reducido; F7 ampliación condicional. |
| `<CollaborationHub>` SignalR tarda — F8 se desliza a v1.1 | media | medio | Camino B/C de §14.6. F9 robusto compensa parcialmente. Si la deuda no cierra en 6 meses, abrir ticket de riesgo en backlog. |
| Performance de bandeja con datasets grandes | baja | medio | Backend ya optimizó índices (F8-PR3 backend). Re-evaluar en F7 con seed 10k. Plan B: virtualización con TanStack Table virtual. |
| Diferencias entre permisos backend/frontend (drift) | baja | medio | Test que compara contra endpoint de manifiesto cuando exista; mientras, code review consistente. |
| Cambios de UX durante UAT que requieren rework | alta | medio | Sesión de design review con cliente al cierre de F1 y F3 (no esperar a UAT). |
| Mobile P2 toma más tiempo del estimado | media | bajo | F2 mobile es asunción F2; si se cae, salimos sin mobile y se diferimos. |

---

## 6. Sizing total y dependencias

Conteo de PRs por fase coherente con
[07-frontend-pr-breakdown.md](07-frontend-pr-breakdown.md) Rev. 4
(consolidado, "trabajo entre dos personas").

| Fase | PRs | Sizing fase | Bloquea a | Depende de |
|---|---|---|---|---|
| 0 — Foundation UI Compras | 2 | M | Todas | — |
| 1 — Read-only de Requisiciones | 2 | M | 2, 3, 5 | 0 |
| 2 — Crear y editor de líneas | 3 | M-L | 3 (parcial) | 1, **§14.7 backend** |
| 3 — Workflow autorizador | 2 | M-L | 4 | 2 |
| 4 — Cancelar y eliminar | 1 | S | — | 3 |
| 5 — Cubrimiento visible | 1 | S | — | 1, **§15.7 del 05 (cubrimiento en DTO)** |
| 6 — Admin de aprobadores | 1 | S | — | 0, **§14.7 backend** (UsuarioSelector) |
| 7 — Hardening v1 | 3 + 1 condicional | M | release v1 | todas las anteriores |
| 8 — Soft lock real | 1 | M | **pre-release v1 — Camino A confirmado Rev. 4** | 7, **`<CollaborationHub>` cerrado** |
| 9 — Diferidos | — | — | — | post-v1 (incluye P11/P12 CRUD catálogos) |
| **Total v1** | **16** | | | |

**Camino crítico hasta release v1:** 0 → 1 → 2 → 3 → 4 → 5 → 6 → 7
→ **8** ≈ **2 a 3 meses con 1 dev (Eduardo) + 1 revisor (Claude)**,
asumiendo:

- Selectores org disponibles del backend antes/durante F2.
- `RequisicionResponse` con cubrimiento confirmado antes de F5.
- `<CollaborationHub>` cerrado por plataforma antes de Fase 7
  (Camino A de §14.6 del 05).
- Mecanismo de seed inicial de catálogos decidido antes del UAT.
- Sin sorpresas grandes en UAT.

> **Nota sobre granularidad** (Rev. 4): los PRs son S–M cada uno,
> consolidados por afinidad. Solo los de alto riesgo (matriz §6.1
> en UF2-PR3, ConflictDialog wireup en UF3-PR2, E2E config CI en
> UF7-PR2, SignalR en UF8-PR1) se mantienen aislados para tener
> foco de revisión propio. El backend cerró Compras con el mismo
> patrón en 24 PRs (ver 03-pr-breakdown.md Rev. 2).

---

## 7. Decisiones operativas pendientes

**Cerradas en sesión de validación 2026-05-09 (Rev. 4 del 05)**:

- [x] F1 — Captura desktop-only con baseline 1366×768 + toggle
      columnas contables en P5.
- [x] F2 — Mobile P2 vale la inversión (~3 días en UF7-PR3).
- [x] F4 — es-MX único locale, sin i18n.
- [x] F8 — Camino A: `<CollaborationHub>` antes de release v1.
      Total v1 = 17 PRs.
- [x] F9 — Conflict 409 con preserve-form-state, diff filtrado,
      "Reaplicar" como botón primario.
- [x] F10 — Opción B: v1 read-only de catálogos, CRUD en v1.1
      (P11+P12). El ERP es la fuente de verdad, no SAP.

**Pendientes de validar con el cliente** (sin bloqueo inmediato):

- [ ] Validar asunciones F3 (WCAG AA), F5 (selector artículo),
      F6 (CubrimientoBar visual), F7 (timeline), F11 (delegación),
      F12 (terminales read-only), F13 (idempotency por form),
      F14 (sin real-time) y F15 (If-Match defensivo) del 05.

**Pendientes de coordinación inmediata**:

- [ ] **Coordinar con plataforma HOY** plazo de cierre de
      `<CollaborationHub>` (define si Camino A es viable o cae a B).
- [ ] **Decidir mecanismo de seed inicial de catálogos** para
      cutover (script SQL / importer SAP one-shot / endpoint admin
      temporal). Decidir antes del UAT.
- [ ] **Abrir tickets backend P0**:
  - §14.1 — `GET /api/v1/compras/requisiciones/{id}/historico`
  - §14.7 — Endpoints Sucursal/Departamento/Almacén/Usuario
- [ ] Confirmar §15.7 del 05 (cubrimiento en `RequisicionResponse`)
      con backend antes de F5.
- [ ] Confirmar §14.2 del 05 (editar cabecera) con backend — define
      si F2 entrega P4 con UX completa o limitada.
- [ ] Codegen ADR-0017 con plataforma (no bloqueante; reduce deuda
      de DTOs mirroreados).
- [ ] Sesión de design review con UX/cliente al cierre de F1 y F3.

**Comunicación al cliente antes de release v1**:

- [ ] Limitación de v1 sobre catálogos: alta de proveedor durante
      v1 → v1.1 requiere contactar a soporte/dev.

---

## 8. Cambios respecto a versiones previas

### Rev. 6 — separación arquitectónica: catálogos a Datos Maestros (2026-05-09)

Alineado con [05-frontend-diseno.md](05-frontend-diseno.md) Rev. 5.
Cambios al plan:

- **Fase 6 reescrita**: solo P9 (admin de aprobadores). P10
  (reclasificar naturaleza bulk) sale del scope — vive en módulo
  Datos Maestros. Título de la fase cambia de "Admin de aprobadores
  y reclasificar naturaleza" a "Admin de aprobadores".
- **§1 Resumen**: eliminado el pendiente "mecanismo de seed inicial
  de catálogos" — ya no aplica porque el backend de CRUD está
  mergeado y la UI vive en otro módulo.
- **§6 Sizing**: Fase 6 baja de 2 a 1 PR. **Total v1: 17 → 16 PRs**.
- **§7 Decisiones operativas**: sin cambios materiales (las
  decisiones operativas pendientes siguen siendo `<CollaborationHub>`
  ya cerrado en backend, validar asunciones restantes con cliente).

Sin cambios en items del resto de fases ni en sizing fase. El
cambio es estructural (qué pertenece a qué módulo), no de scope
de trabajo: el CRUD de catálogos sigue entregándose en v1, pero
en otro módulo de UI.

### Rev. 5 — sesión de validación de asunciones con owner (2026-05-09)

Alineado con [05-frontend-diseno.md](05-frontend-diseno.md) Rev. 4
(asunciones F1, F2, F4, F8, F9, F10 confirmadas en sesión).
Cambios al plan:

- **§1 Resumen**: agregado mecanismo de seed inicial de catálogos
  como pendiente que bloquea release v1 (F10 Opción B).
- **§6 Sizing**: total v1 actualizado de 16 → **17 PRs** (UF8-PR1
  entra antes del UAT por F8 Camino A). Camino crítico ahora
  incluye F8 explícitamente. Asunción nueva: hub cerrado por
  plataforma antes de Fase 7.
- **§7 Decisiones operativas**: las 6 asunciones validadas marcadas
  con [x]. Pendientes redistribuidos en (a) validar asunciones
  restantes con cliente, (b) coordinación inmediata con plataforma
  y backend, (c) comunicación al cliente. Acción nueva: decidir
  mecanismo de seed inicial de catálogos.

Sin cambios en items por fase ni en sizing fase (UF8-PR1 ya estaba
en el plan; solo cambia el momento de ejecución).

### Rev. 4 — alineación con consolidación de PRs (2026-05-09)

Tras consolidación del breakdown 07 (Rev. 3 → Rev. 4: 26 → 16 PRs
base), el plan no cambia en fases ni sizing fase ni camino crítico.
Solo cambia el conteo de PRs por fase en el resumen y la nota
explícita de que el ritmo es "1 dev + 1 revisor" (no 2 devs
paralelos), por lo que la microgranularidad de Rev. 3 agregaba
overhead sin valor.

Sin cambios en items por fase ni en criterios de aceptación. El
trabajo es el mismo; cambia el agrupamiento en PRs.

### Rev. 3 — pushbacks del owner sobre F6/F7/F8/F9 (2026-05-09)

Alineado con [05-frontend-diseno.md](05-frontend-diseno.md) Rev. 3.
Cambios al plan:

- **§1 Resumen ejecutivo**: nueva categoría "Pendientes que NO
  bloquean arranque pero SÍ bloquean release v1" con dos items:
  decisión sobre `<CollaborationHub>` (§14.6 del 05) y ticket P0
  GET /historico (§14.1).
- **Fase 0**: `<ConflictResolutionDialog />` ya no es stub simple,
  es componente con preserve-form-state desde día 1 (F9 Rev. 3).
- **Fase 5**: `<CubrimientoBar>` con números visibles + patrón
  visual + tooltip como detalle adicional (F6 Rev. 3). Validación
  WCAG 1.4.1 explícita.
- **Fase 7 (hardening)**: nuevo item "Timeline completo" condicional
  a §14.1; si el endpoint llega, F7 amplía a transiciones completas;
  si no, queda con timeline reducido + riesgo en UAT.
- **Fase 8 (soft lock)**: reescrita como "decisión P0 antes de
  release" con tres caminos (priorizar / compensar con F9 / aceptar
  riesgo). Decisión del owner antes de iniciar Fase 7.
- **§5 Riesgos**: nuevo riesgo "Conflicto + sin awareness = pérdida
  de trabajo en day-1" con probabilidad alta si camino C, alto
  impacto. Riesgo de adopción explícito.
- **§3 Dependencias**: GET /historico ahora P0.

Sin cambios en sizing total ni en camino crítico (~2-3 meses con
1 dev). El refuerzo de F9 (preserve form state) son 1-2 días
adicionales absorbidos en UF0-PR3 + UF3-PR4 (no nuevos PRs).

### Rev. 2 — patrones UX transversales (2026-05-09)

Alineado con [05-frontend-diseno.md](05-frontend-diseno.md) Rev. 2
(§13 Patrones UX transversales). Cambios al plan:

- **Fase 0**: agregados los componentes auxiliares de feedback
  (`<EmptyState>`, `<ErrorState>`, `<TableSkeleton>`),
  `<Breadcrumbs>`, `useUnsavedChangesGuard` hook,
  `<DomainTermTooltip>` con diccionario `glosario.ts`. Sigue siendo
  S — los items son pequeños y aditivos.
- **Fase 1**: P1 bandeja con search params Zod-validados
  (preservación de filtros en URL, §13.9 del 05). P3 detalle con
  tratamiento explícito de 403/404 (§13.6 del 05) y breadcrumbs.
  Estados loading/empty/error obligatorios en cada pantalla de
  esta fase en adelante.
- **Fase 2**: P4 con borrador en `localStorage` y
  `useUnsavedChangesGuard` (§13.2 del 05). `LineaDialog` con
  beforeunload guard. Tooltips de glosario en campos no obvios.
- **Fase 7**: agregados (a) página de ayuda
  `/compras/ayuda` (§13.7 del 05) con glosario + diagrama de ciclo
  de vida + FAQ; (b) stylesheet `@media print` para P3 detalle
  (§13.8 del 05) que cubre el caso `Ctrl+P` mientras endpoint PDF
  v1.1 no exista.
- **Fase 9 diferidos**: agregadas búsqueda global por folio
  (§13.4 del 05), notificaciones in-app (§13.5 del 05) y
  exportación a PDF (§13.8 del 05).

Sin cambios en sizing total ni camino crítico (~2–3 meses con 1
dev). Renumeración de referencias al 05 reflejando que las brechas
backend pasaron a §14, hallazgos a §15.

### Rev. 1 — versión inicial (2026-05-09)

Plan inicial basado en el diseño de UI Rev. 1
([05-frontend-diseno.md](05-frontend-diseno.md)) y la API real ya
implementada en el backend. Pendiente de calibración con capacidad
real del equipo de UI y validación de los prerrequisitos
(selectores org backend, cubrimiento en DTO).
