# Runbook frontend — Compras Requisiciones

> Referencia operativa para el módulo Compras Requisiciones del
> frontend. **Cubre el ciclo de vida del módulo** (no es ni un
> tutorial de React ni un manual del producto). El plan de UAT vive
> en [docs/operacion/uat-frontend-compras.md](../../docs/operacion/uat-frontend-compras.md).

## 1. Mapa del módulo

```
features/compras/
├── api/                # hooks de TanStack Query (queries + mutations)
│   ├── keys.ts         # comprasKeys.* — namespace por familia
│   ├── types.ts        # mirror manual de DTOs / enums del backend
│   ├── useRequisiciones.ts, useRequisicion.ts, ...
│   ├── useWorkflow.ts  # transmitir / autorizar / rechazar / eliminar / cancelar
│   ├── useLineas.ts    # CRUD de líneas estructurales + notas
│   ├── useAprobadores.ts  # P9 admin: vigentes / histórico / designar / revocar
│   └── useMotivosRechazo.ts
├── components/         # componentes específicos del módulo
│   ├── CabeceraRequisicion.tsx
│   ├── EditorLineas.tsx, ListaLineas.tsx, LineaDialog.tsx
│   ├── AccionesRequisicion.tsx     # botones de workflow gateados por matriz
│   ├── ModalMotivo.tsx             # 3 variantes: rechazar / eliminar / cancelar
│   ├── DesignarAprobadorDialog.tsx
│   ├── TimelineAutorizaciones.tsx
│   ├── ResumenCubrimiento.tsx
│   └── ...
├── lib/
│   ├── acciones-disponibles.ts     # matriz §6.1: visibility + enabled por estado/permiso
│   ├── glosario.ts                 # estados / naturalezas / términos transversales
│   ├── *-search-schema.ts          # Zod schemas para search params de cada bandeja
│   └── draft-storage.ts            # protección contra pérdida de captura
├── pages/              # pantallas (P1, P2, P3, P4, P9, ayuda)
│   ├── BandejaRequisiciones.tsx (P1)
│   ├── BandejaPendientes.tsx    (P2)
│   ├── DetalleRequisicion.tsx   (P3)
│   ├── NuevaRequisicion.tsx     (P4)
│   ├── AdminAprobadores.tsx     (P9)
│   └── Ayuda.tsx
└── schemas/            # Zod schemas de payloads de mutation
    ├── crear-requisicion.ts
    ├── linea.ts
    ├── notas-linea.ts
    ├── autorizar.ts
    ├── terminar-requisicion.ts
    └── designar-aprobador.ts
```

## 2. Patrones obligatorios

### 2.1 Mutaciones HTTP

- **Idempotency-Key** (ADR-0020) en TODAS las mutations de comando
  (POST/PATCH/DELETE) salvo en `useEliminarRequisicion`
  (decisión backend doc 05 §7.6 — operación naturalmente idempotente).
- Hooks usan `useFormIdempotencyKey()` (un hook que devuelve un UUID
  estable por monta del componente — un retry del usuario reusa la
  misma key).
- En `onError`:
  - 422 → `applyServerErrors(form, error)` mapea `errores[]` del
    `ProblemDetails` a campos del form react-hook-form.
  - 403 con `code` específico → manejo inline en el campo
    correspondiente.
  - 409 `CONCURRENCY_CONFLICT` → `useConflictDialog().openSimple(...)`
    o `openPreserve(...)` (UF3-PR3 wireup).
  - Otros → `toast.error(error.problem.title, { description:
    'Código: ' + error.traceId })`.

### 2.2 ETag / If-Match

- `useRequisicion(id)` guarda el ETag en el cache como
  `{ data, etag }` y expone `select: (cached) => cached.data`.
- Las mutations sobre el agregado (PATCH cabecera, líneas) leen el
  ETag con `getQueryData` y lo mandan como `If-Match` (ADR-0012).
- Conflict: el backend devuelve 409 → frontend abre el conflict
  dialog (modo simple o preserve según corresponda).

### 2.3 Permisos (gating UI)

- Los permisos los resuelve `useHasPermission(canonical)` o
  `useHasAnyPermission([a, b])`. **El gate de UI es UX-only**; el
  backend sigue siendo la barrera real.
- La matriz §6.1 (qué acciones son válidas en qué estado para qué
  permiso) vive en `lib/acciones-disponibles.ts`. Cada acción
  expone `{ visible, habilitada, motivoDeshabilitada }`. Los
  componentes de acción NUNCA hardcodean condiciones — siempre
  consumen estos helpers.

### 2.4 Search params (URL state)

- Cada bandeja tiene su Zod schema en `lib/*-search-schema.ts`
  (`BandejaSearch`, `PendientesSearch`, `AdminAprobadoresSearch`).
- Las rutas de TanStack Router aplican `validateSearch:
  Schema.parse` para limpiar valores inválidos en navegación.
- Filtros + búsqueda + paginación van en search params para que
  los links sean compartibles.

### 2.5 Estructura de tests

- **Schemas** (`schemas/*.test.ts`): `parse(...)` con casos válidos
  e inválidos.
- **Hooks** (`api/*.test.tsx`): MSW (`mswServer`) para mockear el
  backend. Wrapper `createQueryWrapper()` provee QueryClient +
  ConflictDialogProvider. Casos: éxito (204/200/201), errores 4xx
  específicos, 5xx.
- **Componentes** (`components/*.test.tsx`): render + render
  gateado por permisos. Para componentes con Radix Dialog/Select,
  los tests son pragmáticos (presence checks + `screen.findByRole`)
  porque jsdom no maneja bien portales + measurements.
- **Pages** (`pages/*.smoke.test.tsx`): mock de `@tanstack/react-router`
  vía `vi.mock`, MSW para los endpoints, asserts de los 4 estados
  (loading / empty / data / error).
- **Accesibilidad**: `axe-core` con tag `wcag21aa` en al menos una
  pantalla por release. `<CubrimientoBar>` y `<Ayuda>` lo tienen.

## 3. Convenciones cross-módulo (replicar en CxC, OC, CxP, ...)

Estas convenciones se diseñaron en el módulo Compras pero aplican
a todo el back-office:

1. **Permisos canónicos** centralizados en
   [lib/auth/permission-codes.ts](../src/lib/auth/permission-codes.ts).
   Mirror del backend; los string literales nunca se duplican en el
   código.
2. **Query keys** namespaced: `['<modulo>', '<recurso>',
   '<acción>', ...filtros]`. Familia entera invalida con
   `invalidateQueries({ queryKey: <modulo>Keys.all })`.
3. **Tipos DTO** en `<modulo>/api/types.ts` con enums como objetos
   `as const` (numéricos, mirror del backend que serializa enums
   como número).
4. **Glosario** por módulo: `<modulo>/lib/glosario.ts` con
   `obtenerDefinicion(term)`. La página de ayuda lo lee.
5. **Hooks de mutation devuelven `useMutation<T, Error, Args>`** —
   no envuelven con abstracciones de auth; eso lo hace el cliente
   `apiRequest` interno.
6. **Conflict resolution**: cualquier hook que dispare 409 abre el
   `<ConflictResolutionDialog>` vía `useConflictDialog()` (shell-
   level). Modo simple para acciones, modo preserve para forms.
7. **App Launcher de nav** (ADR-0032): cada módulo registra
   `secciones: [{ label, cards: NavCard[] }]` en
   [lib/nav.ts](../src/lib/nav.ts).

## 4. Comandos comunes

```bash
# Tests
npm test                  # full suite
npm run test:watch        # watch mode (TDD)
npm run test:coverage     # con threshold gateado en features/compras/**

# Calidad
npm run lint              # ESLint
npm run build             # tsc -b + vite build (producción)

# Dev local
npm run dev               # vite con HMR (default :5173)
```

## 5. Dónde mirar primero al diagnosticar

- **Mutation falla con 4xx no esperado**: red panel del DevTools en
  Network → respuesta `application/problem+json`. El `traceId`
  permite correlacionar con logs del backend.
- **Cache no se actualiza tras mutation**: revisa que el `onSuccess`
  del hook llame a `queryClient.invalidateQueries(...)` con la key
  correcta. La convención es invalidar la familia entera del
  módulo (`comprasKeys.all`) para no perder ninguna view.
- **Form muestra error de campo que no debería**: chequea que el
  `applyServerErrors` esté llamado correctamente y que el `name`
  del campo en el `errores[]` del backend coincida con el `name`
  del react-hook-form.
- **Botón visible cuando no debería**: el gate vive en
  `acciones-disponibles.ts`. Verifica que el caller lea el helper
  correcto (`accionTransmitir`, `accionAprobarNivel1`, etc.).
- **Search params se "pierden" al navegar**: TanStack Router
  requiere pasar `search={...}` en `<Link>` o `state={...}` en
  navegación programática. Chequea ese punto en el componente
  origen.

## 6. Patrones de diseño polish (Zoho-style)

`design/frontend-polish` introdujo un conjunto de patrones de UX que
aplican al módulo Compras y deberían replicarse en CxC, OC, CxP y
demás. Resumen y referencias al código:

### 6.1 Master-detail con lista compacta

Las bandejas con "ver detalle" se renderizan en layout de dos columnas
cuando se selecciona un item: lista compacta sticky a la izquierda
(320px) + panel detalle a la derecha. La lista no se desmonta al cambiar
de item — preserva scroll y filtros.

- Layouts: `RequisicionesLayout` (P1) y `PendientesLayout` (P2).
- Lista compacta: `ListaRequisicionesCompacta`, `ListaPendientesCompacta`.
  Cada item es un `<Link>` con `search={search}` (preserva filtros) y
  `state={{ bandejaSearch | pendientesSearch }}` (alimenta el botón
  "Cerrar" del detalle).
- Mobile drill-down: `idActivo != null` esconde la lista (full-screen
  detalle); sin id, la lista ocupa la pantalla.
- Botón "Cerrar" (X) del detalle infiere a cuál bandeja volver leyendo
  `useMatches().routeId` (`/pendientes/` vs `/requisiciones/`). Ver
  `DetalleRequisicion.inferirRutaPadre`.

Cuando un nuevo módulo agregue master-detail: copiar el patrón de
`RequisicionesLayout` (querias propias + filtros propios) y exponer
`detalle` como prop `ReactNode`. La ruta `/<modulo>/<recurso>/$id`
monta el layout pasándole el componente de detalle.

### 6.2 Sheet (slide-from-right) para forms de "Nueva ..."

Los forms de creación de registro (cabecera + submit) se montan en un
`<Sheet side="right" max-w-3xl>` a nivel shell, no como página
full-screen. Reusa el mismo componente de la página standalone
pasándole `onClose`/`onDirtyChange`. Ver:

- `Sheet` primitive (Radix Dialog wrap): `components/ui/sheet.tsx`.
- Provider shell-level: `NuevaRequisicionProvider` +
  `useNuevaRequisicion()` (context API en
  `nueva-requisicion-context.ts`).
- Confirm al cerrar con cambios: el form reporta `isDirty` via
  `onDirtyChange`; el provider muestra `window.confirm` antes de
  cerrar. Success bypasea con `onClose({ force: true })`.
- Triggers: botón "Nueva" de la bandeja (`useNuevaRequisicion().abrir()`),
  `<QuickCreateMenu>` del topbar.

### 6.3 Inline forms (sin modal) para edición de líneas/items

Forms de detalle dentro de una tabla (líneas de RQ, partidas de
OC, etc.) usan `LineaInlineForm` — un form expandible AL FINAL de
la tabla para agregar, o que REEMPLAZA la fila al editar. No abre
modal. Border colors para distinguir modo:

- Agregar: `border-dashed border-primary/40` + `bg-primary/5`.
- Editar: `border-amber-400` + `bg-amber-50/40` (warning sutil:
  "este registro existente se está modificando").

5 campos críticos visibles en grid (artículo, cantidad, unidad, precio,
fecha) + botón "+Detalles" expande opcionales (cuenta contable, centro
de costo, notas).

### 6.4 Topbar global (search + Quick Create + ayuda)

- **Search input** con placeholder dinámico por ruta
  (`SEARCHABLE_ROUTES` en `Topbar.tsx`). Debounce 200ms entre keystroke
  y URL update; `Enter` aplica inmediatamente. La búsqueda escribe en
  el query param `q` con `replace: true` (no contamina historial).
  Cuando un módulo agrega bandejas nuevas, registrar las rutas en
  `SEARCHABLE_ROUTES` (UF0-PR1 de OC ya registró `/compras/ordenes`,
  `/compras/ordenes/pendientes-autorizacion` y
  `/compras/ordenes/partidas-abiertas` aunque las pantallas reales
  llegan en UF1/UF4/UF7).
- **Quick Create** (`<QuickCreateMenu>`): popover con grupos por
  módulo; cada acción dispara el Sheet del módulo (por ahora, solo
  Compras → "Requisición"). **Convención de wireup cross-módulo**:
  cuando un nuevo tipo de documento agrega su Sheet "Nueva ..." con
  su provider shell-level (ver §6.2), en el mismo PR se agrega la
  acción en `QuickCreateMenu` invocando `useNuevaXxx().abrir()` y
  gateada por el permiso `<modulo>.<recurso>.crear`. Sin Sheet no se
  registra Quick Create — un click que solo navegue al placeholder
  vacío sería UX confusa. Para OC, esto sucede en **UF2-PR1** junto
  con `useNuevaOrdenCompra` y el Sheet de 3 modos (1:1 / consolidación
  / sin RQ previa).
- **Ayuda contextual**: el icono `?` apunta a `/<modulo>/ayuda` según
  pathname.

### 6.5 Sub-topbar del detalle

`DetalleRequisicion` monta una barra sticky bajo el topbar global con:

- Folio (mono) + EstadoBadge + CollaborationIndicator → izquierda.
- AccionesRequisicion (workflow) → centro.
- Imprimir + Histórico + Cerrar (X) → derecha.

`data-print="hidden"` en la sub-topbar y el banner de "editando"
para que `Ctrl+P` produzca un layout limpio. El aside master de los
layouts P1/P2 también lleva `data-print="hidden"`.

### 6.6 Tabular vs master-detail (cuándo)

- Bandeja P1 (`/compras/requisiciones`): tabla full-page con sort,
  filtros, paginación. Click en "Ver" navega a `/$id` y monta el
  master-detail.
- Bandeja P2 (`/compras/pendientes`): igual — tabla en `index`,
  master-detail en `$id`.
- Bandeja OC P1 (`/compras/ordenes`, UF1-PR3): mismo patrón —
  `<BandejaOrdenesCompra>` tabular en `index` (folio / fecha /
  proveedor / comprador / estado / sub-estados compactos / Ver);
  click en "Ver" → `/compras/ordenes/$id` que monta
  `<OrdenesCompraLayout>` master-detail (320px compact list +
  `<DetalleOrdenCompra>`).

El tabular es el "overview" exportable/escanable. El master-detail es
el "workspace" para revisar varios items sin perder contexto. Ambos
viven en rutas distintas y comparten la misma `*-search-schema.ts`.

**Cuándo agregar la vista tabular en un módulo nuevo**: tan pronto
como aparezca su 1ra bandeja, no después. UF1-PR2 de OC se entregó
inicialmente solo con la master-detail (placeholder cuando
`idActivo=null`) y eso fue una desviación del patrón que generó
inconsistencia visible para el usuario. UF1-PR3 lo corrigió. Para
módulos nuevos (CxC, CxP, Activos), construir el tabular y el
master-detail en el mismo PR aunque crezca un poco — la falta del
tabular es un agujero de UX inmediato; vale el size budget.

### 6.7 Sin breadcrumbs en bandejas y placeholders

Las bandejas (P1, P2) y las páginas de placeholder del back-office
**NO renderizan `<Breadcrumbs>`**. La jerarquía y el contexto los da:

- **Sidebar** del shell: muestra el módulo activo (Compras /
  Facturación / etc.) y la card seleccionada (Mis requisiciones /
  Pendientes / Órdenes de compra).
- **Topbar global**: search contextual con placeholder por ruta y
  empresa activa (suficiente "dónde estoy").
- **Sub-topbar del detalle** (§6.5): folio + estado + acciones — la
  ÚNICA pantalla donde el contexto de la fila individual amerita
  navegación de regreso, y eso lo cubre el botón "Cerrar (X)" que
  lee la ruta padre y vuelve a la bandeja con filtros preservados
  vía `state` del Link.

`<Breadcrumbs>` se mantiene como componente disponible (lo usa la
página de detalle si en el futuro lo necesita) pero por ahora no se
renderiza en producción. Cuando se evaluó en UF0-PR1 de OC se quitó
del placeholder porque (a) RQ no lo usa, (b) duplica info del
sidebar/topbar, (c) consume vertical valioso en pantallas densas.

El componente `<Breadcrumbs>` queda para casos puntuales que emerjan
(p. ej. árbol de documentos de OC con varios niveles de wrappers).

### 6.8 Selectores dependientes de contexto padre

Cuando el universo válido de un selector depende de OTRO field del
mismo form, el selector recibe la prop `<padre>Id: string | null` y la
query de su catálogo está scopeada por ese id. Ejemplos vigentes:

- `<AlmacenSelector sucursalId={…}>` — cada almacén físico pertenece
  a una sucursal.
- `<DepartamentoSelectorPorSucursal sucursalId={…}>` (PR-A3) —
  asignaciones N:M `compartido.sucursal_departamentos`. Filtra a los
  `Activo`s; los `Inactivo`s no son seleccionables para nuevas RQs.

Convención del selector:

- `sucursalId === null` → `disabled` + placeholder de hint
  (`"Selecciona primero una sucursal"`). La query NO se dispara
  (`enabled: false` en TanStack).
- Catálogo vacío con padre seleccionado → `emptyListText` específico
  ("Esta sucursal no tiene departamentos activos asignados…").
- Cambio del field padre en el form → el `useEffect` que vigila el
  field padre llama `setValue('<hijo>Id', '', { shouldDirty: false })`
  para limpiar el seleccionado previo. Si el dependiente es a su vez
  padre de otro (sucursal → almacén Y departamento), se resetean en
  el mismo efecto. Ver `NuevaRequisicion.tsx` como referencia.

Decisión sobre extender vs crear selector nuevo: cuando el use case
global existe en paralelo (admin de aprobadores, listados read-only),
se crea un componente nuevo (`<XxxSelectorPorYyy>`) en vez de agregar
una prop opcional al global. Mantiene los dos contratos limpios y
evita combinar pesos de cache distintos.

### 6.9 Sheet lateral de gestión N:M desde row

Cuando un recurso (Sucursal) administra asignaciones N:M a otro
catálogo (Departamento), no se crea ruta nueva — se agrega un Sheet
slide-from-right disparado desde cada row activa del panel padre.
Ejemplo: `<SheetDepartamentosDeSucursal sucursal={…} onOpenChange={…}>`
del PR-A3, montado dentro de `SucursalesPanel`.

Forma:

- Componente controlado: `props = { sucursal: SucursalResponse | null;
  onOpenChange }`. `sucursal === null` ⇒ cerrado.
- Renderiza el CATÁLOGO GLOBAL del otro lado de la relación (todos los
  deptos), marcando cada row con su estatus en este padre: Activa /
  Inactiva / Sin asignar. Acciones por row según estatus: Asignar /
  Desactivar / Reactivar.
- Botón "Gestionar deptos" en la row del padre — oculto sin el
  permiso correspondiente (gating por `useHasPermission`, mismo
  patrón §2.3).
- Confirm de desactivación con copy explícito: "Bloquea nuevas RQs
  pero NO afecta las existentes" (decisión consistente con
  desactivar Sucursal/Departamento del propio catálogo).

## 7. Pendientes documentados

- **Coverage functions/branches en 60%** (vs 70% target del
  breakdown) — ver comentario en `vitest.config.ts`. Plan: subir
  con UF7-PR2 E2E Playwright (cubre flujos completos sin las
  limitaciones de jsdom para Radix Dialog/Select).
- **Mobile sidebar `<MobileSidebar>` con hamburger** — UF7-PR3
  difiere a un follow-up dedicado.
- **Performance bench con seed 10k RQs** — UF7-PR3 difiere al
  follow-up que requiere seed grande en backend.
- **PDF de impresión real** — endpoint backend v1.1; mientras
  tanto, `Ctrl+P` con el stylesheet `@media print` de
  `index.css` cubre el caso.
