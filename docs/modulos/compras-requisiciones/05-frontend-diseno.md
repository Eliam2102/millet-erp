# Diseño de frontend — Submódulo Requisiciones (Compras)

> **Construido sobre:** [01-diseno.md](01-diseno.md) (Rev. 14),
> [02-plan-implementacion.md](02-plan-implementacion.md) (Rev. 5),
> [03-pr-breakdown.md](03-pr-breakdown.md) (Rev. 2),
> [04-cuidados-infra.md](04-cuidados-infra.md) (Rev. 1).
>
> **Construido contra:** la API real implementada en
> [backend/src/Api/Endpoints/Compras/](../../../backend/src/Api/Endpoints/Compras/)
> y [backend/src/Api/Endpoints/Catalogos/](../../../backend/src/Api/Endpoints/Catalogos/).
>
> **Estado:** propuesta de diseño UI v1 para revisión con el owner. Las
> decisiones marcadas como `[Asunción Fxx]` requieren confirmación con
> el cliente antes de implementar pantallas. Las brechas con el backend
> (§14) son tickets a abrir contra ese equipo.
>
> **Fecha:** 2026-05-09.

---

## 0. Cómo leer este documento

Mismas convenciones que [01-diseno.md](01-diseno.md):

- `[Decidido]` — fijado por ADR existente, decisión del backend ya
  implementada, o por levantamiento previo.
- `[Asunción Fxx]` — propuesta del frontend tech lead, razonable pero
  pendiente de confirmación. Listadas en §3 con `Fxx` para distinguir
  de las del backend (`Axx`).
- `[Diferido]` — fuera de alcance de v1; anotado para no perderlo.
- `[Verificar con equipo]` — algo que no está claro en código ni doc.

Este documento describe **qué construir y por qué en la capa de UI**,
no el código. La implementación seguirá el stack del repo (React 19 +
TanStack ecosystem + shadcn/ui + Tailwind v4) — ver §4.

---

## 1. Posicionamiento

### 1.1 Qué cubre este documento

UI del submódulo Requisiciones (módulo Compras) v1. Cubre las
pantallas que consumen los endpoints HTTP ya mergeados a `main`
(F1-PR1 → F9-PR2) y los catálogos compartidos (F7-PR1).

### 1.2 Qué NO cubre

- Pantallas de Almacén, OC, CxP, Activos Fijos, Contabilidad.
  Conviven en el mismo shell pero su diseño vive en sus respectivos
  documentos (cuando lleguen).
- **Administración de catálogos cross-empresa** (proveedores,
  artículos, sucursales, departamentos, almacenes, usuarios) —
  **viven en el módulo Datos Maestros separado**
  ([docs/modulos/datos-maestros/](../datos-maestros/)). El backend
  del CRUD ya está mergeado (PR #73 backend), pero las pantallas
  no son scope de Compras. Compras solo CONSUME los catálogos vía
  selectores read-only (UF2-PR1).
- Reclasificación bulk de naturaleza de artículos — operación
  sobre `compartido.articulos`, vive también en Datos Maestros.
- Adjuntos en RQs (`A11`, diferido a v1.1, ver 01-diseno.md).
- Bulk operations en bandeja del autorizador (`A13`, diferido a v1.1).
- Re-autorización de saldos (`A12` cliente confirmó: solo informativo).
- Pantalla de admin de la matriz de aprobación / umbrales por
  departamento (no hay endpoint todavía — ver §14).

### 1.3 Posicionamiento en el shell del ERP

El shell autenticado actual ([_app.tsx](../../../frontend/src/routes/_app.tsx))
ya tiene un slot "Compras" en el sidebar
([nav.ts](../../../frontend/src/lib/nav.ts)) marcado `disabled: true`.
Este diseño **activa ese slot** y agrega un sub-árbol de rutas debajo
de `/compras/...`. El resto del shell (Topbar con `EmpresaSelector`,
gating por permiso, layout dark) no cambia.

---

## 2. Personas y flujos

Las personas se derivan del modelo del cliente (01-diseno.md §3.bis,
§8.3) y de los permisos canónicos del backend
([PermisosCanonicos.cs](../../../backend/src/Identidad/Domain/PermisosCanonicos.cs)
+ los 10 de Compras agregados en F0b-PR1).

| Persona | Rol legacy / nuevo | Permisos clave |
|---|---|---|
| **Capturador / Solicitante** | Cualquier usuario autorizado a crear RQs en su depto | `compras.requisiciones.crear`, `editar`, `eliminar` (pre-aut), `leer` |
| **Capturador con delegación** | Asistente que captura para otros (jefes que no entran al ERP) | + `compras.requisiciones.seleccionar-requisitante` |
| **Jefe de departamento (N1)** | Aprueba RQs del depto si naturaleza ≠ `Estandar` y ≠ `Riesgo`-especializado (ver §3.bis.1 del diseño) | `compras.requisiciones.autorizar.nivel1`, `rechazar` |
| **Jefe de almacén (N1 alterno)** | Aprueba RQs cuando naturaleza = `Estandar` (ver §3.bis.2) | `compras.requisiciones.autorizar.nivel1`, `rechazar` |
| **Autorizador N2** | Aprueba RQs cuando se rebasa el umbral del depto o naturaleza ∈ {`Critico`, `Riesgo`} | `compras.requisiciones.autorizar.nivel2`, `rechazar` |
| **Comprador** | Consulta RQs autorizadas, gestiona OC borrador y recepciones (en módulo OC, no aquí). Aquí solo lee y agrega notas a líneas. | `compras.requisiciones.leer`, `editar` (solo notas, ver F2-PR2) |
| **Admin de aprobadores** | Captura/mantiene la matriz `aprobadores_departamento` (F9-PR1) | `compras.aprobadores.administrar` |
| **Auditor / Lectura** | Solo consulta | `compras.requisiciones.leer` |
| **Admin del catálogo de artículos** | Reclasifica naturaleza en bulk (post-go-live, F9-PR1) | `compartido.catalogos.administrar` |

> **`compras.requisiciones.cancelar`** lo poseen tanto el solicitante
> como el comprador (cualquiera con autorización para descartar
> post-autorización; el cliente decide la asignación final en F9-PR3).
> **`compras.requisiciones.editar-de-otros-usuarios`** y
> **`ver-todos-departamentos`** son permisos transversales que aún no
> tienen UI específica — se aplican implícitamente al filtrar bandejas
> y al permitir abrir RQs ajenas.

### 2.1 Flujo del Capturador

1. Entra a `/compras/requisiciones` → ve su bandeja (filtro implícito
   `requisitanteId = current_user`, ver §6.1).
2. Click "Nueva requisición" → `/compras/requisiciones/nueva`.
3. Llena cabecera (sucursal, depto, almacén destino, requisitante si
   tiene delegación, prioridad, fecha entrega deseada, descripción).
   Guarda → estado `Borrador`, redirige a `/compras/requisiciones/{id}`.
4. Agrega líneas (artículo desde catálogo, cantidad, UM, precio
   estimado, cuenta contable, centro de costo, proyecto, fecha
   requerida, notas).
5. Cuando todo cuadra, click "Transmitir" → estado `EnAutorizacion`.
   La RQ desaparece de "Mis pendientes de transmitir" y aparece como
   "En autorización" en su bandeja.
6. Si necesita cancelar antes de autorizar: "Eliminar" con motivo.
7. Mientras está en autorización, puede agregar **notas a líneas**
   (operativas para el comprador).

### 2.2 Flujo del Jefe de departamento / Jefe de almacén (N1)

1. Entra a `/compras/pendientes` → bandeja de pendientes de
   autorización del **nivel que le corresponde**. El backend filtra
   por permiso (`autorizar.nivel1`); el frontend opcionalmente filtra
   por `departamentoId` cuando el rol está restringido.
2. Click una RQ → `/compras/requisiciones/{id}`. Detalle completo
   (cabecera + líneas + cubrimiento previsto + autorizaciones previas
   si las hay).
3. Acción "Aprobar" → confirm dialog → POST autorización Nivel1.
   - Si la matriz queda satisfecha (solo N1), el handler ejecuta la
     bifurcación stock-aware (reservas, movimientos, OC borrador).
     La UI muestra un toast "RQ autorizada y enviada a almacén / OC".
   - Si requiere N2, la UI muestra "Esperando autorización N2".
4. Acción "Rechazar" → modal con selector de motivo + texto opcional
   (si el motivo `permiteTextoLibre`). POST `/rechazar`.

### 2.3 Flujo del Autorizador N2

Idéntico al N1 pero filtrado por `autorizar.nivel2`. Solo ve RQs que
ya tienen Nivel1 firmado y rebasan el umbral / tienen naturaleza
elevada.

### 2.4 Flujo del Comprador

1. Bandeja general filtrada por estado `EnSurtido` (las RQs que
   "ya generaron OC borrador y están esperando recepción").
2. Detalle de RQ: ve `Cubrimiento` por línea (cuánto fue del almacén,
   cuánto es de compra, cuánto se ha recibido).
3. Puede agregar **notas a líneas** (PATCH `/lineas/{id}/notas`) —
   p.ej. "proveedor confirmó entrega 15-mayo".
4. Si el proveedor no responde / cae el flujo, "Cancelar" desde
   `Autorizada` o `EnSurtido` con motivo.

### 2.5 Flujo del Admin de aprobadores

1. `/compras/admin/aprobadores` → matriz vigente
   `(empresa, departamento, rol) → usuario`.
2. "Designar" → modal con `(departamento, rol, usuario)`. Si ya
   existe vigente, el backend lo cierra automáticamente y crea uno
   nuevo (ver `AprobadoresEndpoints.cs`).
3. "Revocar" → cierra vigencia.
4. Tab "Histórico" → consulta auditoría con al menos un filtro
   obligatorio.

---

## 3. Asunciones de frontend

Las asunciones del backend (`A1`–`A15`) están cerradas con el cliente.
Estas son **del frontend**, marcadas `Fxx` para no confundirlas. Se
deben validar antes del primer PR de UI.

| # | Asunción | Default propuesto | Qué pasa si el cliente dice "no" |
|---|---|---|---|
| F1 | **Captura es desktop-only** — **CONFIRMADA por owner 2026-05-09** | Layout pensado con **baseline 1366×768** (laptop corporativa típica MX, no 1280). 1280×800 entra como "degradado pero usable". <1280 explícitamente rompe. **Implicaciones**: (a) tabla de líneas P5 con **toggle "Mostrar columnas contables"** que oculta cuenta/centro/proyecto por default → respiración a 1366; los contadores las activan cuando las necesitan. (b) Tabla de líneas **scrollea internamente** (header sticky), no la página entera, cuando hay >7 líneas. (c) Nada se diseña al límite de 1280; el umbral de "rompe" es 1280, pero el diseño respira a 1366. | N/A — confirmada. |
| F2 | **Bandeja del autorizador vale la pena en móvil** — **CONFIRMADA por owner 2026-05-09** | Diseño responsive con cards apilables. El N1/N2 puede aprobar desde el celular (caso típico: jefe que viaja). ~3 días absorbidos en UF7-PR3 (polish). | N/A — confirmada. |
| F3 | **WCAG AA es objetivo por default** | `shadcn/ui` lo provee gratis para primitives; nosotros mantenemos contraste, navegación por teclado y aria-labels en componentes nuevos. | Si exige WCAG AAA, requiere auditoría externa y ~1 semana extra de polish por pantalla. |
| F4 | **Spanish-MX (es-MX) es default y único locale en v1** — **CONFIRMADA por owner 2026-05-09** | Fechas `dd/MM/yyyy`, números con coma de miles + punto decimal, moneda MXN sin código (`$1,234.56`). Strings hard-coded en español por componente, sin librería de i18n. El stack ya lo tiene en [datetime.ts](../../../frontend/src/lib/datetime.ts) y [money.ts](../../../frontend/src/lib/money.ts). | N/A — confirmada. |
| F5 | **Selector de artículo: combobox con búsqueda por `clave` y `nombre`** | Carga lazy desde `GET /api/v1/catalogos/articulos?clave=<q>` con debounce 300ms. Resultado ≤ 50 por página. | Si el cliente exige árbol jerárquico por `categoria`, +1 semana. |
| F6 | **Visualización de Cubrimiento: barra segmentada + números visibles + patrón** | Barra horizontal segmentada (4 segmentos) **con los números visibles al lado siempre, no solo en tooltip**. Cada segmento tiene **patrón visual distintivo** (rayas / sólido / punteado) además del color, para no depender de color (WCAG AA + táctil sin hover). Tooltip queda como detalle adicional. Componente `<CubrimientoBar />`. Ver §14.7 del 05 para diseño detallado. | Si exige tabla pivoteada en lugar de barra, requiere otro componente. ~3 días. |
| F7 | **Timeline completo (no solo autorizaciones) — el endpoint backend lo provee** | Sección "Línea de tiempo" en P3 detalle con todas las transiciones de estado + autorizaciones + recepciones + cancelación/rechazo/eliminación. **El modelo de auditoría ADR-0008 ya captura las transiciones; falta solo exponer el endpoint** `GET /api/v1/compras/requisiciones/{id}/historico` (§14.1 del 05 → ticket P0 al backend, no diferible). Mientras llega, P3 muestra solo las `Autorizaciones` del detalle como timeline reducido + badge "Histórico completo disponible cuando backend exponga endpoint". | Si el ticket P0 al backend no se prioriza, la primera RQ con dos rechazos y una autorización tardía deja a soporte sin saber qué pasó — riesgo de adopción real. |
| F8 | **Soft lock UX activo desde release v1 — Camino A** — **CONFIRMADA por owner 2026-05-09** | El `CollaborationHub` SignalR se prioriza con plataforma para **cerrar antes de Fase 7 hardening**. UF8-PR1 entra como parte de release v1, antes del UAT. Awareness activo desde día 1: badge con avatares apilados, banner "Pedro García está editando esta requisición". El conflict de F9 sigue posible pero raro (el banner lo previene en la mayoría de casos). **Bloqueante operativo**: coordinar con plataforma sobre plazo de cierre de `<CollaborationHub>`; si en 4-6 semanas no hay plan claro, cae automáticamente a Camino B (F9 robusto compensa, F8 se difiere a v1.1). Recomendación tech lead: hacer el escalamiento HOY para tener visibilidad de plazo. | Si plataforma no puede cerrar el hub en plazo razonable, fallback automático a Camino B. Si surge presión de release, evaluar Camino C (aceptar riesgo + comunicar al cliente). |
| F9 | **Conflictos 409: dialog que PRESERVA form state + permite re-aplicar** — **CONFIRMADA por owner 2026-05-09** | El `<ConflictResolutionDialog />` v1: (1) captura form state local antes de refrescar, (2) refresca + muestra versión actualizada del servidor con **diff filtrado** (solo campos que solapan con tus cambios; expandible a "Ver todos los cambios remotos"), (3) ofrece botón **"Reaplicar mis cambios"** como **acción primaria default** (asume que conservar trabajo es lo costoso de perder), botón secundario "Solo refrescar (descartar mis cambios)", (4) submit con nuevo `Version` / `If-Match`. Sin merge automático (eso es v1.1). +1-2 días absorbidos en UF0-PR2 + UF3-PR2. Compatible con draft localStorage de §13.2 (mismo mecanismo de captura). Ver §8.4 del 05 para flujo completo + §8.5 para caso simple sin form. | N/A — confirmada con sub-decisiones (diff filtrado + Reaplicar primario). |
| F10 | **Catálogos cross-empresa fuera del scope de Compras — viven en módulo Datos Maestros** — **CONFIRMADA por owner 2026-05-09, reacomodada en Rev. 5** | El ERP es la fuente de verdad de proveedores y artículos (no SAP — solo seed inicial one-shot). El backend del CRUD está mergeado (PR #73 backend). **Pero el frontend del CRUD NO vive en Compras**: las pantallas de administración de catálogos cross-empresa son **transversales al ERP** (los consumirán CxC, OC, CxP, Activos Fijos cuando lleguen) y se diseñan en el módulo **Datos Maestros** ([docs/modulos/datos-maestros/](../datos-maestros/)). Compras solo CONSUME los catálogos vía selectores read-only (`<ArticuloSelector>`, `<ProveedorSelector>` en UF2-PR1). Lo mismo aplica a la reclasificación bulk de naturaleza (antigua P10) — operación sobre `compartido.articulos`, vive en Datos Maestros. **Lo único que se queda en Compras**: P9 admin de aprobadores (tabla `compras.aprobadores_departamento`, permiso `compras.aprobadores.administrar` — config local del módulo). | Si el cliente exige que CRUD de catálogos viva bajo `/compras/admin/...`, contradice principio de "datos maestros = transversales"; escalar al owner. |
| F11 | **El selector de requisitante (delegación) solo aparece si el usuario tiene `seleccionar-requisitante`** | Por default, `RequisitanteId` se omite en el body del POST y el backend asume `current_user`. Los pocos usuarios con delegación ven un `<UsuarioSelector>` arriba del formulario. | OK como está. |
| F12 | **Estados terminales son read-only completo, sin "ver versiones anteriores"** | Una RQ `Cerrada`/`Cancelada`/`Rechazada`/`Eliminada` se ve en modo lectura, con un banner que indica el estado y el motivo. No se permite editar nada. | Si pide editar notas en terminales, contradice el dominio (ver §4.2 invariantes del 01-diseno.md). |
| F13 | **Idempotency-Key se genera por formulario, no por click** | `useFormIdempotencyKey()` retorna un UUID v4 estable por la vida del componente. Un retry tras 409/red flaky reusa la misma key (transparente). Al "cerrar y volver a abrir el formulario", nueva key. Coherente con ADR-0020. | OK, es el patrón estándar del ADR. |
| F14 | **Bandeja general no usa real-time updates** | Sin SignalR conectado todavía (ADR-0001 + ADR-0012 Capa 2 = `NoOp`), la bandeja se refresca con `staleTime: 30s` + manual refresh. Cuando el `CollaborationHub` se conecte, agregamos invalidación reactiva. | OK. |

---

## 4. Stack frontend (auditado contra el repo, 2026-05-09)

Estado real auditado contra
[c:\Users\UserSP\Desktop\Project_Millet_ERP\frontend\](../../../frontend/).

### 4.1 Stack confirmado

| Pieza | Versión | Estado | Referencia |
|---|---|---|---|
| **React** | 19.2.5 | ✅ instalado | [package.json](../../../frontend/package.json) |
| **TypeScript** | 6.0.2 | ✅ instalado | [tsconfig.app.json](../../../frontend/tsconfig.app.json) |
| **Vite** | 8.0.10 | ✅ instalado | (cache busting via ADR-0004) |
| **Tailwind CSS** | 4.2.4 (via `@tailwindcss/vite`) | ✅ instalado | ADR-0002 |
| **shadcn/ui** | inicializado | ✅ algunos primitives copiados | [components/ui/](../../../frontend/src/components/ui/) — `avatar`, `button`, `dropdown-menu`, `input`, `separator`, `tooltip` |
| **Radix UI primitives** | varios | ✅ instalados | (subyacentes a shadcn) |
| **TanStack Router** | 1.169.1 + plugin Vite + CLI | ✅ file-based routing activo | [routes/](../../../frontend/src/routes/), [main.tsx](../../../frontend/src/main.tsx) |
| **TanStack Query** | 5.100.9 + DevTools | ✅ wireado | [query-client.ts](../../../frontend/src/lib/query-client.ts) |
| **Zustand** | 5.0.12 | ✅ usado en `auth-store.ts` | [auth-store.ts](../../../frontend/src/lib/auth/auth-store.ts) |
| **react-hook-form** | 7.75.0 + `@hookform/resolvers` | ✅ instalado, sin uso real aún | (ADR-0023) |
| **Zod** | 4.4.2 | ✅ instalado, sin uso real aún | (ADR-0023) |
| **MSAL** | `@azure/msal-browser` 5.9.0 + `@azure/msal-react` 5.3.2 | ✅ wireado para Entra ID | (ADR-0003, ADR-0015) |
| **date-fns** + **date-fns-tz** | 4.1.0 / 3.2.0 | ✅ usado, TZ `America/Mexico_City` | [datetime.ts](../../../frontend/src/lib/datetime.ts) |
| **lucide-react** | 1.14.0 | ✅ usado en sidebar | [nav.ts](../../../frontend/src/lib/nav.ts) |
| **`tailwind-merge` + `clsx` + `class-variance-authority`** | varios | ✅ usados | (helpers de shadcn) |

### 4.2 Patrones establecidos en el repo

**Routing**: file-based. `routes/__root.tsx` → `routes/_app.tsx`
(layout autenticado con `beforeLoad` redirect a `/login` si no
autenticado) → `routes/_app/<modulo>/...`. El plugin de Vite genera
[routeTree.gen.ts](../../../frontend/src/routeTree.gen.ts) automáticamente.

**Auth**: `useAuthStore` (zustand) en memoria pura, sin persistencia
(refresh = re-login silent vía MSAL). Token en `accessToken`,
permisos en `permisos: string[]`. Hooks: `useHasPermission(code)`,
`useHasAnyPermission([])`, `useHasAllPermissions([])`. Componente
declarativo: `<RequirePermission code="..." fallback={...}>`.

**Cliente HTTP**: [api-client.ts](../../../frontend/src/lib/auth/api-client.ts)
con `apiFetch` y `apiFetchJson`. Inyecta `Authorization: Bearer` desde
auth-store; en 401 limpia sesión.

**Layout**: `AppShell` (sidebar 240px dark navy + topbar sticky +
área de contenido). `Sidebar` lee `navItems` de `nav.ts`; items
`disabled: true` se renderizan como "Próximamente". `Topbar` incluye
`EmpresaSelector` y `UserMenu`.

**Permisos canónicos en frontend**: [permission-codes.ts](../../../frontend/src/lib/auth/permission-codes.ts)
es un **mirror manual** del backend. Solo tiene los permisos de
`Identidad` e `Infra` ahora — agregar los 10 de Compras + el de
catálogos es parte del primer PR de UI Compras.

**Estilo de comentarios**: docs strings en español con tags XML
(`<c>`, `<see>`, `<para>`). Replicar.

### 4.3 Lo que falta del stack ADR-0023 (gaps de plataforma)

| Pieza | Estado | Quién lo arregla |
|---|---|---|
| **`api-types.ts` codegen** vía `openapi-typescript` (ADR-0017) | ❌ no existe | Plataforma (no UI Compras). Mientras tanto, mirroring manual de DTOs en `features/compras/api/types.ts`. |
| **`apply-server-errors.ts`** helper que mapea `ProblemDetails.errores[]` a campos del form | ❌ no existe | UI Compras lo introduce en su primer PR (puede vivir en `lib/`). |
| **`useFormIdempotencyKey()`** + helpers `postWithIdempotency` | ❌ no existe | UI Compras lo introduce; vive en `lib/idempotency.ts`. |
| **Parser de `ProblemDetails`** y clase `ApiError` (ADR-0010) | ❌ `apiFetchJson` lanza `Error` plano | UI Compras lo introduce; vive en `lib/api-error.ts`. |
| **Manejo de `ETag` / `If-Match`** (ADR-0012 Capa 1) | ❌ el cliente HTTP no lo conoce | UI Compras lo introduce; envoltorio `requestWithEtag` o flag por endpoint. |
| **Cliente SignalR + `useCollaboration` hook** (ADR-0001 + ADR-0012 Capa 2) | ❌ `<CollaborationHub>` es `NoOp` (deuda de plataforma `<CollaborationHub>` en §8.6 del diseño) | Plataforma. Mientras: stub en frontend que no hace nada y permite descomentar el hook después. |
| **`<ConflictResolutionDialog />`** (ADR-0012) | ❌ no existe | UI Compras introduce versión v1 simple ("refrescar y reintentar"); v1.1 = merge automático. |
| **Componentes shadcn faltantes** | falta `Dialog`, `Select`, `Combobox`, `DataTable`, `Form`, `Toast`, `Calendar`/`DatePicker`, `Card`, `Badge`, `Skeleton` | UI Compras los copia desde `npx shadcn add` en su primer PR. |
| **`components/erp/forms/`** (RfcField, MoneyField, DatePickerField — ADR-0023) | ❌ vacío (`.gitkeep`) | UI Compras introduce los que necesite (`MoneyField`, `DatePickerField`); RfcField no aplica aquí. |
| **`components/erp/display/`** (MoneyDisplay, DateTimeDisplay) | ❌ vacío (`.gitkeep`) | UI Compras los introduce. |
| **`components/erp/selectors/`** | ❌ vacío (`.gitkeep`) | UI Compras introduce `ArticuloSelector`, `ProveedorSelector`, `UsuarioSelector` (cuando aplique), `DepartamentoSelector`, `AlmacenSelector`, `MotivoRechazoSelector`. |
| **`components/erp/collaboration/`** (CollaborationIndicator) | ❌ vacío (`.gitkeep`) | UI Compras lo stubea; queda funcional cuando `CollaborationHub` se conecte. |
| **`features/`** (carpetas) | ❌ vacía (`.gitkeep`) | UI Compras crea `features/compras/`. |

> **Decisión de scope:** los items "UI Compras lo introduce" no son
> abstracciones especulativas: **son componentes que las pantallas
> efectivamente necesitan en este v1**. Lo que NO entra (codegen,
> SignalR real, ConflictDialog merge automático) queda diferido.

---

## 5. Inventario de pantallas

Convención de rutas: `/compras/...`. Todas viven bajo
[`routes/_app/compras/...`](../../../frontend/src/routes/_app/) (auth
guard heredado del layout `_app`).

| ID | Nombre | Ruta | Propósito | Permisos requeridos | Estados de RQ que muestra |
|---|---|---|---|---|---|
| **P1** | Bandeja de mis requisiciones | `/compras/requisiciones` | Listado paginado del usuario actual con filtros opcionales (estado, depto, búsqueda por folio). Default: `requisitanteId = current_user`. Quien tiene `ver-todos-departamentos` puede limpiar el filtro. | `compras.requisiciones.leer` | Todos |
| **P2** | Bandeja de pendientes de autorización | `/compras/pendientes` | Bandeja del autorizador. Solo `EnAutorizacion`. Filtros: depto. | `compras.requisiciones.leer` (+ visibilidad gateada por `autorizar.nivel1` o `nivel2` para que aparezca en el menú) | `EnAutorizacion` |
| **P3** | Detalle de requisición | `/compras/requisiciones/$id` | Pantalla principal: cabecera + líneas + cubrimiento + autorizaciones + acciones contextuales por estado (§7). | `compras.requisiciones.leer` (acciones: gateadas individualmente) | Todos |
| **P4** | Nueva requisición (cabecera) | `/compras/requisiciones/nueva` | Wizard paso 1: cabecera. Al guardar, redirige a P3 con la RQ ya en `Borrador` para agregar líneas. | `compras.requisiciones.crear` (+ `seleccionar-requisitante` si delega) | — |
| **P5** | Editor de líneas (parte de P3) | `/compras/requisiciones/$id` (sección embebida) | Tabla densa con add/edit/delete inline en `Borrador`; read-only con notas editables en `EnAutorizacion`/`Autorizada`/`EnSurtido`; read-only completo en terminales. | `compras.requisiciones.editar` | `Borrador`, `EnAutorizacion`, `Autorizada`, `EnSurtido` (read-only en terminales) |
| **P6** | Modal de motivo (rechazo / eliminación / cancelación) | overlay sobre P3 | Selector de motivo (`MotivoRechazoSelector`) + textarea opcional. Filtrado por bitmask `aplicaA`. | el permiso de la acción (`rechazar`, `eliminar`, `cancelar`) | la RQ donde se invoca |
| **P7** | Catálogo de artículos (read-only, dentro de selector) | overlay (combobox) en P5 | Búsqueda lazy contra `GET /api/v1/catalogos/articulos`. | `compartido.catalogos.leer` | — |
| **P8** | Catálogo de proveedores (read-only, dentro de selector de cabecera) | overlay (combobox) en P4 / P3 | Búsqueda lazy contra `GET /api/v1/catalogos/proveedores`. | `compartido.catalogos.leer` | — |
| **P9** | Admin de aprobadores | `/compras/admin/aprobadores` | Matriz vigente + alta/revocación + tab histórico. | `compras.aprobadores.administrar` | — (no muestra RQs) |

**Total: 9 pantallas**, donde P5/P6/P7/P8 son embebidas/overlay
sobre otras (no rutas top-level). Top-level **5 rutas**: P1, P2, P3,
P4, P9.

> **Trasladadas a módulo Datos Maestros** (Rev. 5): la antigua P10
> (reclasificar naturaleza bulk) y las pantallas CRUD de proveedores
> y artículos (que en Rev. 4 estaban como P11/P12 v1.1) **NO viven
> en Compras Requisiciones**. Son operaciones sobre catálogos
> cross-empresa que el módulo Datos Maestros administra. Compras
> solo CONSUME los catálogos vía selectores read-only (P7/P8). Ver
> [docs/modulos/datos-maestros/README.md](../datos-maestros/README.md).

> **Diferidas explícitamente:**
>
> - Pantalla de "Mis transmitidas / pendientes" (subset de P1 con
>   filtro pre-aplicado). Se cubre con un *quick filter* en P1.
> - Pantalla de admin de umbrales por departamento. Sin endpoint
>   backend (ver §14.4).

---

## 6. Mapeo estado ↔ acciones disponibles

Esta tabla es la **fuente única de la lógica condicional de acciones**
en P3. Cualquier botón / ítem de menú deriva su `disabled` o
`hidden` de aquí, no de hardcoded `if estado === ...`.

### 6.1 Tabla maestra

> Convención: ✅ = aparece habilitado · ⚪ = aparece deshabilitado con
> tooltip explicativo · ❌ = no aparece (`hidden`).
> "Permiso" además del general; sin el permiso = ❌ siempre.

| Acción | Borrador | EnAutorizacion | Autorizada | EnSurtido | Cerrada | Cancelada | Rechazada | Eliminada | Permiso |
|---|---|---|---|---|---|---|---|---|---|
| **Editar cabecera** | ⚠️ ver §14.2 | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | `editar` |
| **Agregar línea** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | `editar` |
| **Editar línea (estructural)** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | `editar` |
| **Eliminar línea** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | `editar` |
| **Editar notas de línea** | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | `editar` |
| **Transmitir** | ✅ (si N≥1 línea) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | `editar` |
| **Aprobar Nivel1** | ❌ | ✅ (si no firmaste N1) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | `autorizar.nivel1` |
| **Aprobar Nivel2** | ❌ | ✅ (si N1 firmado y no firmaste N2) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | `autorizar.nivel2` |
| **Rechazar** | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | `rechazar` |
| **Eliminar** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | `eliminar` |
| **Cancelar** | ❌ | ❌ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | `cancelar` |
| **Ver detalle (read-only)** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | `leer` |

> **"Aprobar N2" no aparece nunca antes de N1**: el invariante del
> agregado lo exige (01-diseno.md §4.4). El frontend lo deshabilita
> con tooltip "Falta autorización Nivel 1" en lugar de ocultarlo,
> para que el autorizador N2 entienda por qué no puede aún.
>
> **Auto-aprobación cuando el solicitante es jefe del depto**: la
> regla la decide el backend (preg. abierta §3.bis.2 del diseño); la
> UI confía en la lista de "pendientes" — si la RQ aparece para un
> usuario, puede aprobarla. No replicamos la lógica.

### 6.2 Implementación sugerida

```typescript
// features/compras/lib/acciones-disponibles.ts
import type { EstadoRequisicion, RequisicionResponse } from '../api/types';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export interface AccionDisponible {
  visible: boolean;
  habilitada: boolean;
  motivoDeshabilitada?: string;
}

export function accionEditarCabecera(
  rq: RequisicionResponse,
  permisos: readonly string[],
): AccionDisponible {
  const tienePermiso = permisos.includes(PermisosCanonicos.ComprasRequisicionesEditar);
  if (!tienePermiso) return { visible: false, habilitada: false };
  return {
    visible: true,
    habilitada: rq.estado === 'Borrador',
    motivoDeshabilitada:
      rq.estado !== 'Borrador'
        ? `No se puede editar en estado ${rq.estado}`
        : undefined,
  };
}

// ... una función por acción de la tabla §6.1
```

Tests: tabla parametrizada que recorre cada celda de §6.1 y valida la
función correspondiente.

---

## 7. Estrategia de datos

### 7.1 Cliente HTTP — extensión necesaria

`apiFetch` actual (auth + 401 → clearSession) es buena base pero
**no**:

1. Parsea `ProblemDetails` (ADR-0010).
2. Inyecta `Idempotency-Key` (ADR-0020).
3. Maneja `ETag`/`If-Match` (ADR-0012).
4. Tipado de errores tipo `ApiError`.

**[Decidido]** Capa nueva en `lib/api/`:

```text
lib/api/
├── client.ts             # extiende apiFetch con problem-details parsing
├── error.ts              # class ApiError + helpers (esApiError, esConflicto, etc.)
├── idempotency.ts        # useFormIdempotencyKey() + addIdempotencyHeader()
└── etag.ts               # extractEtag() + addIfMatchHeader()
```

```typescript
// lib/api/client.ts (esquema)
export async function apiRequest<T>(
  path: string,
  options: ApiRequestOptions = {},
): Promise<{ data: T; etag?: string }> {
  const headers = new Headers(options.headers);
  if (options.idempotencyKey) headers.set('Idempotency-Key', options.idempotencyKey);
  if (options.ifMatch) headers.set('If-Match', options.ifMatch);

  const response = await apiFetch(path, { ...options, headers });

  if (!response.ok) {
    const contentType = response.headers.get('Content-Type') ?? '';
    if (contentType.includes('application/problem+json')) {
      const problem = (await response.json()) as ProblemDetails;
      throw new ApiError(problem, response.status);
    }
    throw new ApiError(
      { type: 'about:blank', title: response.statusText, status: response.status },
      response.status,
    );
  }

  if (response.status === 204) return { data: undefined as T };
  const etag = response.headers.get('ETag') ?? undefined;
  const data = (await response.json()) as T;
  return { data, etag: etag?.replace(/"/g, '') };
}
```

### 7.2 TanStack Query — convenciones

**Query keys** (extiende ADR-0023 al dominio Compras):

```typescript
const comprasKeys = {
  all: ['compras'] as const,
  requisiciones: () => [...comprasKeys.all, 'requisiciones'] as const,
  requisicionesList: (filtros: ListarFiltros) =>
    [...comprasKeys.requisiciones(), 'list', filtros] as const,
  requisicion: (id: string) =>
    [...comprasKeys.requisiciones(), 'detail', id] as const,
  pendientes: (filtros: PendientesFiltros) =>
    [...comprasKeys.all, 'pendientes', filtros] as const,
  motivosRechazo: () => [...comprasKeys.all, 'motivos-rechazo'] as const,
  aprobadores: (filtros: AprobadoresFiltros) =>
    [...comprasKeys.all, 'aprobadores', filtros] as const,
  catalogos: {
    proveedores: (filtros: ProvFiltros) => ['catalogos', 'proveedores', filtros],
    articulos: (filtros: ArtFiltros) => ['catalogos', 'articulos', filtros],
  },
};
```

**Custom hooks** por endpoint, en `features/compras/api/`:

```text
features/compras/api/
├── types.ts                       # mirror manual de DTOs (mientras no exista codegen)
├── useRequisicion.ts              # GET /{id} → guarda etag en query meta
├── useRequisiciones.ts            # GET / con filtros
├── usePendientesAutorizacion.ts   # GET /pendientes-autorizacion
├── useMotivosRechazo.ts           # GET /motivos-rechazo (staleTime 1h, casi inmutable)
├── useCrearRequisicion.ts         # POST / (mutation, idempotent)
├── useAgregarLinea.ts             # POST /{id}/lineas
├── useActualizarLinea.ts          # PATCH /{id}/lineas/{lineaId}
├── useActualizarNotasLinea.ts     # PATCH /{id}/lineas/{lineaId}/notas
├── useEliminarLinea.ts            # DELETE /{id}/lineas/{lineaId}
├── useTransmitirRequisicion.ts    # POST /{id}/transmitir
├── useAutorizarRequisicion.ts     # POST /{id}/autorizaciones
├── useRechazarRequisicion.ts      # POST /{id}/rechazar
├── useEliminarRequisicion.ts      # POST /{id}/eliminar
├── useCancelarRequisicion.ts      # POST /{id}/cancelar
└── useAprobadores.ts              # F9-PR1
```

Y para catálogos en `features/catalogos/api/`:

```text
features/catalogos/api/
├── types.ts
├── useArticulos.ts                # GET /api/v1/catalogos/articulos
├── useArticulo.ts
├── useProveedores.ts
├── useProveedor.ts
└── useReclasificarNaturaleza.ts
```

### 7.3 Cache, invalidación y staleTime por recurso

| Recurso | `staleTime` | Invalidación tras |
|---|---|---|
| Detalle de RQ | 0s (fresh siempre que se monta) | cualquier mutation de esa RQ (`requisicion(id)`) o del listado |
| Bandeja general | 30s (default global) | `Crear`, `Eliminar`, `Transmitir`, `Cancelar`, `Rechazar`, `Autorizar` cuando el handler completa la matriz |
| Pendientes de autorización | 30s | mismas que arriba; además `Autorizar` invalida también la pendiente del usuario actual |
| `motivos-rechazo` | 1h (catálogo casi inmutable) | nunca durante una sesión |
| Aprobadores vigentes | 5min | tras `Designar` / `Revocar` |
| Catálogo de artículos | 5min | tras `Reclasificar naturaleza` |
| Catálogo de proveedores | 5min | nunca en v1 (no hay edición desde ERP) |

**[Diferido a SignalR]** Cuando el `CollaborationHub` se conecte
(deuda `<CollaborationHub>`), agregamos invalidación reactiva en
eventos `requisicion.estado.cambiado`. Mientras tanto, las
mutations invalidan manualmente.

### 7.4 Optimistic vs pessimistic updates

**[Decidido] Pessimistic por default en v1.** Razón: la mayoría de
las mutations toman <200ms y la mayoría devuelven `204 No Content`,
así que después de la mutation invalidamos y dejamos que se refetch.
**No vale la pena la complejidad de optimistic updates** para el
incremento de UX que nos darían en v1.

**Excepción**: edición de `notas` de línea (en cualquier estado no
terminal). Ahí sí aplicamos optimistic update porque el usuario
escribe rápido y el roundtrip se siente lento. Si falla, revert con
toast de error.

### 7.5 Manejo de ETag / If-Match (ADR-0012 Capa 1)

**Backend actual**: el endpoint `GET /{id}` (única ruta donde se
verificó) emite `ETag: "<version>"`. Las mutations **NO exigen
`If-Match`** en código actual — las migraciones de tablas tienen
`Version` como `IsConcurrencyToken`, así que EF lanza
`DbUpdateConcurrencyException` (→ 409) si el `Version` cargado en
memoria del handler no coincide con BD. **Pero el handler carga el
agregado fresh dentro de la transacción**, así que el `If-Match` no
es estrictamente necesario para detectar conflictos a nivel BD.

**[Asunción F15]** Aún así, el frontend mandará `If-Match` con el
último ETag conocido. Razones:

1. Defensa en profundidad: si en el futuro algún handler decide no
   recargar el agregado, el `If-Match` lo protege.
2. Diferenciación entre "tu cambio se aplicó pero ya estaba
   desactualizado" vs "tu cambio se aplicó limpio". La 1ª escenarios
   en bandeja del autorizador (cargas la bandeja, mientras tomas un
   café alguien aprueba la RQ, vuelves y rechazas → quieres saber).

El cliente HTTP guarda `etag` en la cache de TanStack Query como
`meta` por entrada. Las mutations leen el `etag` actual y lo envían
como `If-Match`. Si el backend lo ignora, no pasa nada; si lo respeta,
ganamos detección temprana.

> **[Verificar con backend]** si exige `If-Match` (412 sin él) o lo
> ignora (procesa normal, devuelve 409 si EF detecta conflicto). Ver §14.5.

### 7.6 Generación de Idempotency-Key

`useFormIdempotencyKey()` retorna un UUID v4 estable por la vida
del componente (vía `useRef` o `useId` + crypto.randomUUID).

```typescript
// lib/api/idempotency.ts
import { useRef } from 'react';

export function useFormIdempotencyKey(): string {
  const ref = useRef<string | null>(null);
  if (ref.current === null) {
    ref.current = crypto.randomUUID();
  }
  return ref.current;
}
```

Cada mutation que requiere idempotencia recibe el key como argumento:

```typescript
function NuevaRequisicionForm() {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearRequisicion();
  // ...
  crear.mutate({ command: values, idempotencyKey });
}
```

Si el form **se desmonta y se vuelve a montar** (navegación + back),
es un form nuevo → nueva key. Eso es coherente con el ADR-0020.

**Endpoints que requieren `Idempotency-Key`** (auditados contra
código):

- `POST /requisiciones` ✅
- `POST /requisiciones/{id}/transmitir` ✅
- `POST /requisiciones/{id}/autorizaciones` ✅
- `POST /requisiciones/{id}/rechazar` ✅
- `POST /requisiciones/{id}/cancelar` ✅
- `POST /requisiciones/{id}/lineas` ✅
- `POST /aprobadores` ✅
- `DELETE /aprobadores/{id}` ✅
- `POST /articulos/reclasificar-naturaleza` ✅

**No la requieren**:

- `POST /requisiciones/{id}/eliminar` (decisión backend: pre-aut sin
  impacto fiscal; ver `RequisicionesEndpoints.cs` línea ~290)
- `PATCH` de líneas (estructural y notas)
- `DELETE` de línea
- Cualquier `GET`

El frontend respeta el contrato del backend: solo manda el header
donde está declarado.

---

## 8. Manejo de errores

### 8.1 Mapping ProblemDetails → UI

ADR-0010 fija el shape:

```typescript
type ProblemDetails = {
  type: string;
  title: string;
  status: number;
  detail?: string;
  instance?: string;
  traceId?: string;
  errores?: { campo: string; codigo: string; mensaje: string }[];
};
```

**Mapeo por status**:

| Status | Modo de fallo | Tratamiento UI |
|---|---|---|
| 400 | Validación cliente (formato, parsing) | Toast con `title`. Si trae `errores[]`, mapear a campos del form vía `applyServerErrors`. |
| 401 | Sesión expiró | `apiFetch` ya limpia sesión → redirect a `/login`. Sin ruido. |
| 403 | Permiso denegado | Toast destructivo "No tienes permiso para esta acción". Loguear en consola con `traceId` (suele ser bug de UI: botón visible que no debería). |
| 404 | RQ inexistente o de otra empresa | Página de detalle: render fallback "Esta requisición no existe o no tienes acceso." con CTA "Volver a bandeja". Mutation: toast + invalidar query. |
| 409 | Conflicto de concurrencia (ADR-0012) o `IDEMPOTENCY_IN_PROGRESS` | Si el `code` empieza con `IDEMPOTENCY_`, retry automático respetando `Retry-After`. Si es `CONCURRENCY_CONFLICT`, abrir `<ConflictResolutionDialog />` (v1: ofrece "Refrescar" o "Sobrescribir si tienes permiso"). |
| 412 | (no debería ocurrir; ver §14.5) | Toast genérico + log. |
| 422 | Regla de negocio (`code` específico) o `BODY_MISMATCH` idempotency | Toast con `title` + `detail`. Si trae `errores[]`, mapear a form. Casos típicos: `TRANSMITIR_SIN_LINEAS`, `ESTADO_INVALIDO`, motivo no permitido, etc. |
| 5xx | Error de servidor | Toast genérico "Algo salió mal. Si persiste, reporta el código X." donde X = `traceId`. NO exponemos `detail` real (suele estar oculto del backend de todas formas). |

### 8.2 `applyServerErrors` helper

Igual al de ADR-0023. Mapea `errores[].campo` → `form.setError`.
Convención de naming del `campo`: el backend ya usa camelCase
(verificado en código), así que el helper es un `pass-through`.

### 8.3 Códigos de error específicos del módulo (auditados)

Vistos en código backend (`backend/src/Compras/`):

| Código | HTTP | Significado | UX sugerida |
|---|---|---|---|
| `EMPRESA_NO_SELECCIONADA` | 403 | El JWT no tiene `current_empresa_id` | Toast + abrir EmpresaSelector. |
| `SELECCIONAR_REQUISITANTE_DENEGADO` | 403 | Sin permiso para crear a nombre de otro | Inline error en el campo "Requisitante". |
| `AUTORIZAR_NIVEL_DENEGADO` | 403 | Sin permiso para el nivel | Esconder el botón de antemano (es bug de UI si llega). |
| `NIVEL_INVALIDO` | 422 | El nivel del body no es 1 ó 2 | Bug de UI; loguear. |
| `TRANSMITIR_SIN_LINEAS` | 422 | Intentaste transmitir con 0 líneas | Toast + ancla a la sección de líneas con CTA "Agregar línea". |
| `CANCELAR_FALLO` | 422 | Almacén u OC fallaron al liberar/abortar | Toast con "Reintenta. Si persiste, contacta soporte." + `traceId`. |
| `USUARIO_NO_ENCONTRADO` | 404 | (Aprobadores) usuario inactivo | Inline error en `<UsuarioSelector>`. |
| `FILTRO_OBLIGATORIO` | 422 | Histórico de aprobadores sin filtro | UI lo previene: el form no permite submit sin al menos un filtro. |
| `PROVEEDOR_NO_ENCONTRADO` / `ARTICULO_NO_ENCONTRADO` | 404 | id desconocido | Solo en pantallas dedicadas (no usual desde combobox). |
| `IDEMPOTENCY_IN_PROGRESS` | 409 | Otro request idéntico está corriendo | Retry automático del cliente con `Retry-After`. Sin UI. |
| `IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY` | 422 | Bug del cliente | Loguear, toast genérico. |

> Mantener actualizada esta tabla con cada PR backend que agregue un
> nuevo `code`. Incluirla en code review checklist.

### 8.4 Ejemplo de flujo de error: capturar con conflicto

Caso real: capturador con 15 líneas, alguien tocó la cabecera. Es
el escenario de F9 que define adopción.

1. Capturador edita una RQ `Borrador` con cabecera + 15 líneas
   (`Version=12` cargada al montar P3).
2. Está modificando una línea en el dialog `LineaDialog` y
   simultáneamente un colega con `editar-de-otros-usuarios` cambia
   la cabecera (la `Descripcion`) → BD avanza a `Version=13`.
3. Capturador envía `PATCH /lineas/{id}` con `If-Match: "12"`.
4. Backend retorna **409** con `code: CONCURRENCY_CONFLICT`.
5. Frontend abre `<ConflictResolutionDialog />` v1 (preserve form
   state + sub-decisiones de Rev. 4):
   - **Captura el form state local** (los campos que el capturador
     había modificado) en memoria antes de cualquier refresh.
   - Mensaje: "Otro usuario modificó esta requisición mientras la
     editabas. Tu trabajo NO se perdió — abajo puedes revisar qué
     cambió y decidir si lo reaplicas."
   - **Sección 1 ("Cambios remotos relevantes" — diff filtrado)**:
     muestra **solo los campos remotos que solapan con tus cambios
     locales**. En este caso, no hay solape (capturador editó
     `Cantidad` de línea, colega editó `Descripcion` de cabecera) →
     sección dice "Tus cambios no se solapan con los del otro
     usuario." + nota "El otro usuario también cambió 1 campo. [Ver
     todos los cambios remotos]" (expandible).
   - **Sección 2 ("Tus cambios pendientes")**: los valores que el
     capturador había editado, listados.
   - Botones:
     - **"Reaplicar mis cambios"** (botón primario, default): refresca
       query → repopula el form con los valores del capturador →
       usuario ajusta si quiere → submit con el nuevo `Version=13`.
       Es el primario porque conservar trabajo es lo costoso.
     - "Solo refrescar (descartar mis cambios)" (secundario): invalida
       query y vuelve a la versión del servidor sin reaplicar.
     - "Cancelar" (terciario): cierra el dialog sin acción; el
       capturador queda en el form con sus cambios pero el siguiente
       submit volverá a fallar.
6. Capturador elige "Reaplicar" (Enter directo confirma, ya que es
   el botón primario) → form vuelve a tener sus 15 líneas editadas →
   submit funciona.

Aquí "trabajé 20 minutos en 15 líneas" no se pierde. Sin esta
salvaguarda (versión simple "refrescar y reintentar"), el flujo
mata la confianza en el sistema. Ver §14.6 para el riesgo combinado
con F8 NoOp.

### 8.5 Ejemplo de flujo de error: aprobar con conflicto (caso simple)

Caso simple donde no hay form local que preservar:

1. Autorizador entra a `/compras/pendientes`, ve RQ `MID2026-000042`
   con `Version=5`.
2. Click "Aprobar Nivel1" → POST `/autorizaciones` con
   `If-Match: "5"` y `Idempotency-Key: <uuid>`.
3. Otro autorizador firmó hace 200ms — la BD tiene `Version=6`.
4. Backend retorna **409** con `code: CONCURRENCY_CONFLICT`.
5. Frontend abre `<ConflictResolutionDialog />` simplificado (no hay
   form local que preservar, solo una acción):
   - "Esta requisición fue actualizada por otro usuario. Refresca
     para ver el estado actual."
   - Botones: "Refrescar y revisar" (default) | "Cancelar".
6. Refrescar → invalida query → vuelve a cargar el detalle (ya con
   N1 firmado por el otro usuario). El UX recae en la tabla §6.1: el
   botón "Aprobar Nivel1" ya no aparece.

> El componente `<ConflictResolutionDialog />` detecta
> automáticamente si hay form state que preservar (recibe el form
> como prop opcional) y elige el flujo §8.4 o §8.5. Mismo componente,
> dos modos.

---

## 9. Soft lock (ADR-0012 Capa 2) — UX

### 9.1 Estado actual

**[Decidido]** El `CollaborationHub` SignalR es deuda de plataforma
(`<CollaborationHub>`, ver §8.6 del 01-diseno.md). Mientras tanto,
Capa 1 (`Version`/`IsConcurrencyToken`) protege los datos.

### 9.2 Diseño de UX para cuando llegue

**Indicador colaborativo en P3 (detalle de RQ)**:

- Posición: arriba a la derecha, junto a las acciones.
- Componente: `<CollaborationIndicator entidad="requisicion" id={id} />`
  (en `components/erp/collaboration/`, hoy stubeado, después wireado
  al hub real).
- Render:
  - 0 otros: nada visible.
  - ≥1 otros viendo: avatares apilados (max 3 + "+N") con tooltip
    "Pedro García también está aquí".
  - ≥1 otros editando: avatares con badge animado y label "está
    editando" (si solo 1) / "están editando" (si ≥2).
- Heartbeat: 30s (handled por el hook `useCollaboration`).

**Comportamiento si "alguien edita"**:

- Los inputs no se deshabilitan (Capa 2 es awareness, no lock).
- Banner sutil arriba del editor: "⚠️ Pedro García está editando
  esta requisición. Revisa con él antes de guardar para evitar
  conflictos."
- Si guardas y hay 409, abre `<ConflictResolutionDialog />`.

**Estado intermedio (mientras `<CollaborationHub>` no exista)**:

- `<CollaborationIndicator />` renderiza `null` (silently noop).
- El stub del hook está documentado: `useCollaboration(...) → []`.
- Cuando el hub se conecte, el componente cobra vida sin tocar las
  pantallas que ya lo invocan.
- **Riesgo combinado con F9**: sin awareness, dos usuarios chocan
  recién al guardar. La mitigación crítica es el
  `<ConflictResolutionDialog />` con preserve-form-state de F9
  (Rev. 3). Ver §14.6 para los tres caminos de mitigación
  (priorizar hub vs reforzar F9 vs comunicar al cliente).

### 9.3 Entidades del módulo con soft lock

Solo el agregado raíz `Requisicion` (declarado por el backend en
F2-PR1, ver `PLATFORM-TODO(<CollaborationHub>)` en
`RequisicionConfiguration.cs`). Las líneas y autorizaciones viven
dentro del agregado y no requieren tracking propio.

---

## 10. Permisos en UI

### 10.1 Estrategia

> **El backend es siempre el gate de seguridad.** El frontend solo
> oculta/deshabilita por UX. Si el frontend tiene un bug que muestra
> un botón a quien no debería, el backend lo rechaza con 403.
> Conversamente, el frontend nunca habilita una acción sin el
> permiso correspondiente.

ADR-0007 fija el patrón: `useHasPermission(code)` lee de
`auth-store.permisos[]`. El store se hidrata en login y en cambio de
empresa, con TTL server-side de 5min.

### 10.2 Manifiesto de permisos del módulo

Agregar a [permission-codes.ts](../../../frontend/src/lib/auth/permission-codes.ts):

```typescript
// Compras — Requisiciones
ComprasRequisicionesLeer: 'compras.requisiciones.leer',
ComprasRequisicionesCrear: 'compras.requisiciones.crear',
ComprasRequisicionesEditar: 'compras.requisiciones.editar',
ComprasRequisicionesEliminar: 'compras.requisiciones.eliminar',
ComprasRequisicionesCancelar: 'compras.requisiciones.cancelar',
ComprasRequisicionesAutorizarNivel1: 'compras.requisiciones.autorizar.nivel1',
ComprasRequisicionesAutorizarNivel2: 'compras.requisiciones.autorizar.nivel2',
ComprasRequisicionesRechazar: 'compras.requisiciones.rechazar',
ComprasRequisicionesEditarDeOtros: 'compras.requisiciones.editar-de-otros-usuarios',
ComprasRequisicionesSeleccionarRequisitante: 'compras.requisiciones.seleccionar-requisitante',
ComprasRequisicionesVerTodosDepartamentos: 'compras.requisiciones.ver-todos-departamentos',

// Compras — Aprobadores (F9-PR1)
ComprasAprobadoresAdministrar: 'compras.aprobadores.administrar',

// Compartido — Catálogos (solo LEER se usa en Compras vía selectores)
CompartidoCatalogosLeer: 'compartido.catalogos.leer',
// `compartido.catalogos.administrar` lo consume el módulo Datos Maestros, no este.
```

Estos códigos **deben coincidir exactamente** con los del backend
(`PermisosCanonicos.cs`). Hasta que exista el codegen ADR-0017 son
mirror manual; un test puede comparar las constantes leyendo `/api/v1/identidad/permisos` y fallar si difieren.

### 10.3 Aplicación por pantalla / acción

| UI element | Permisos |
|---|---|
| Item de sidebar "Compras" (root) | `compras.requisiciones.leer` (mínimo para entrar) |
| Sub-item "Mis requisiciones" (P1) | `compras.requisiciones.leer` |
| Sub-item "Pendientes de autorización" (P2) | `compras.requisiciones.leer` **+** (`autorizar.nivel1` ∨ `autorizar.nivel2`) — sin permiso de autorizar, no tiene sentido entrar. Usa `useHasAnyPermission`. |
| Sub-item "Admin · aprobadores" (P9) | `compras.aprobadores.administrar` |
| Botón "Nueva requisición" en P1 | `compras.requisiciones.crear` |
| Selector de requisitante en P4 | `compras.requisiciones.seleccionar-requisitante` (oculta el campo si no lo tiene; backend igual respeta `RequisitanteId = current_user`) |
| Filtro "Todos los departamentos" en P1/P2 | `compras.requisiciones.ver-todos-departamentos` (sin él, el filtro queda fijo al departamento del usuario) |

> **`compras.requisiciones.editar-de-otros-usuarios`**: aplica al
> backend cuando el solicitante de la RQ ≠ usuario actual y la RQ
> está en `Borrador`. La UI no necesita lógica especial: si el botón
> "Editar línea" aparece, es porque el backend lo permite (devuelve
> 403 si no). Mantenemos el patrón "preguntar al backend, no
> replicar la regla".

### 10.4 Filtrado server-side de bandejas

**[Decidido]** El backend ya filtra por `EmpresaId` del JWT
(multi-tenant, ADR-0011). El frontend no agrega filtros de seguridad.

Lo que el frontend SÍ hace:

- Si el usuario NO tiene `ver-todos-departamentos`, **el frontend
  envía `departamentoId=<su depto>`** automáticamente. Su depto vive
  en el `me` (verificar — ver §14.7) o en el JWT extendido. Si no
  está disponible, el filtro queda visible y el usuario lo elige; el
  backend igual deja ver todo dentro de la empresa (no es seguridad
  fina).

---

## 11. Formularios y validación

### 11.1 Estrategia

ADR-0023 fija el patrón: `react-hook-form` + Zod schema con tipos
inferidos.

**Reglas estructurales** (validación cliente Y servidor):

- Formato/tipo del campo (email, UUID, decimal con N decimales).
- Requerido / opcional según el shape del DTO.
- Longitud máxima (mirror del CHECK de BD, p.ej. `descripcion ≤ 500`).
- Rangos numéricos básicos (`Cantidad > 0`, `PrecioEstimado > 0`).

Estas se duplican en Zod para UX inmediata. Aceptable y necesario.

**Reglas de negocio** (validación SOLO servidor):

- "TRANSMITIR_SIN_LINEAS" — el backend lo decide.
- Naturaleza más restrictiva → nivel requerido — solo el motor del
  backend lo sabe.
- Permisos — el backend.
- Estado válido para la transición — el backend.

**No replicamos FluentValidation en Zod.** Inviable mantenerlo en
sync. Si Zod marca algo como inválido y el backend lo aceptaría,
peor: bloqueamos al usuario sin razón.

### 11.2 Schemas Zod del módulo

```text
features/compras/schemas/
├── crear-requisicion.ts
├── linea.ts                       # agregar y actualizar comparten shape
├── notas-linea.ts
├── transmitir.ts                  # (no body; solo el id)
├── autorizar.ts
├── terminar-requisicion.ts        # rechazar / eliminar / cancelar
├── designar-aprobador.ts
└── reclasificar-naturaleza.ts
```

Ejemplo:

```typescript
// features/compras/schemas/crear-requisicion.ts
import { z } from 'zod';

export const CrearRequisicionSchema = z.object({
  sucursalId: z.string().uuid('Sucursal requerida'),
  departamentoId: z.string().uuid('Departamento requerido'),
  almacenDestinoId: z.string().uuid('Almacén destino requerido'),
  requisitanteId: z.string().uuid().optional(),  // F11: solo si tiene delegación
  clasificacion: z.enum(['Servicio', 'OrdenCompra', 'MateriaPrima', 'Pinturas']),
  prioridad: z.enum(['Normal', 'Alta', 'Baja']),
  fechaSolicitud: z.string().datetime(),  // ISO UTC desde el frontend
  fechaEntregaDeseada: z.string().date().nullish(),
  proveedorSugeridoId: z.string().uuid().nullish(),
  descripcion: z
    .string()
    .max(500, 'Máximo 500 caracteres')
    .nullish(),
});

export type CrearRequisicionValues = z.infer<typeof CrearRequisicionSchema>;
```

### 11.3 Componentes ERP de formulario que necesitamos

**Nuevos en `components/erp/forms/`:**

| Componente | Uso | Notas |
|---|---|---|
| `<MoneyField>` | precio estimado de línea | Formatea con `formatMoney`, parsea string a `{amount, currency}`. Locale `es-MX`. |
| `<DatePickerField>` | fecha entrega deseada, fecha requerida por línea | Convierte ISO local → UTC al hacer submit. Calendar de shadcn (popover). |
| `<DecimalField>` | cantidad por línea | 5 decimales máx (ver §10.4 de 01-diseno). |
| `<TextAreaField>` | descripción de cabecera, motivo, notas | autosize; max length variable. |

**Nuevos en `components/erp/selectors/`:**

| Componente | Uso | Endpoint | Notas |
|---|---|---|---|
| `<ArticuloSelector>` | línea de RQ | `GET /api/v1/catalogos/articulos?clave=<q>` | Combobox con búsqueda lazy debounced, filtra por `estatus=Activo`. Muestra: clave + nombre + naturaleza badge. |
| `<ProveedorSelector>` | cabecera (proveedor sugerido, opcional) | `GET /api/v1/catalogos/proveedores?clave=<q>` | Igual al anterior; filtra por estatus. |
| `<UsuarioSelector>` | requisitante delegado, designar aprobador | **[Verificar §14.7]** endpoint de listado de usuarios | Combobox con búsqueda por nombre/email. |
| `<DepartamentoSelector>` | filtros de bandeja, cabecera de RQ | **[Verificar §14.7]** endpoint de listado de departamentos | Si el usuario no tiene `ver-todos-departamentos`, viene fijo al suyo. |
| `<AlmacenSelector>` | cabecera (almacén destino) | **[Verificar §14.7]** endpoint de almacenes | — |
| `<SucursalSelector>` | cabecera | **[Verificar §14.7]** endpoint de sucursales | — |
| `<MotivoRechazoSelector>` | modal de rechazo/eliminación/cancelación | `GET /api/v1/compras/motivos-rechazo` | Filtra por bitmask `aplicaA` según el flujo. Si el motivo seleccionado tiene `permiteTextoLibre=true`, exige textarea. |
| `<NaturalezaSelector>` | filtro en bandeja (las 4 naturalezas del enum) | enum local | 4 opciones fijas. P10 (reclasificar bulk) vive en Datos Maestros, no aquí. |

> Los selectores de catálogos legacy (Departamento, Sucursal,
> Almacén, Usuario) **dependen de endpoints que no he podido
> verificar** (ver §14.7). Si no existen aún, el primer PR de UI
> Compras los declara como `[Verificar con equipo]` y entrega
> mocks/seed-only mientras el backend los expone.

**Nuevos en `components/erp/display/`:**

| Componente | Uso | Notas |
|---|---|---|
| `<MoneyDisplay>` | montos en líneas, totales | wrap de `formatMoney`. |
| `<DateTimeDisplay>` | fechas de auditoría, fecha solicitud | wrap de `formatDateTime` o `formatDateLong` según contexto. |
| `<EstadoBadge>` | estado de RQ en bandejas y detalle | Color por estado: Borrador=gris, EnAutorizacion=ámbar, Autorizada=azul, EnSurtido=morado, Cerrada=verde, Cancelada/Rechazada/Eliminada=rojo claro/gris oscuro. |
| `<NaturalezaBadge>` | en línea: naturaleza del artículo | Estandar=neutral, Servicio=azul, Critico=rojo, Riesgo=naranja. |
| `<CubrimientoBar>` | en línea: visualización del cubrimiento | Barra horizontal segmentada (F6 — Rev. 3): 4 segmentos con **patrón visual distintivo** (sólido / rayas diagonales / punteado / cross-hatch) además de color, para que sean distinguibles sin depender de color (WCAG AA + táctil sin hover). **Números visibles al lado de la barra siempre** (no solo en tooltip): "10 total · 3 alm · 5 OC (2 rec) · 2 pend". Tooltip con desglose y porcentaje queda como detalle adicional para contextos desktop con mouse. |
| `<TimelineAutorizaciones>` | abajo del detalle | Cards verticales con avatar, nivel, fecha relativa, notas. Al alcanzar la matriz, agregar item "Matriz satisfecha — bifurcación ejecutada". |

> `<CubrimientoBar>` y `<TimelineAutorizaciones>` son los componentes
> nuevos más visuales y específicos. Ambos son fundadores —
> probablemente reutilizables en CxC, OC, etc., una vez que esos
> módulos diseñen sus equivalentes.

### 11.4 Estructura de carpetas

Sigue ADR-0023:

```text
frontend/src/
├── lib/
│   ├── api/                    # nuevo: cliente HTTP enriquecido
│   ├── auth/                   # existe
│   ├── datetime.ts             # existe
│   ├── money.ts                # existe
│   └── ...
├── components/
│   ├── ui/                     # existe (shadcn)
│   └── erp/
│       ├── forms/              # nuevo: MoneyField, DatePickerField, ...
│       ├── display/            # nuevo: MoneyDisplay, EstadoBadge, ...
│       ├── selectors/          # nuevo: ArticuloSelector, ...
│       └── collaboration/      # nuevo: CollaborationIndicator (stub)
├── features/
│   ├── compras/
│   │   ├── api/                # custom hooks de TanStack Query
│   │   ├── components/         # editor de líneas, header de RQ, modal motivo, etc.
│   │   ├── lib/                # acciones-disponibles.ts, mappers, etc.
│   │   ├── schemas/            # Zod schemas
│   │   └── pages/              # P1, P2, P3, P4, P9
│   └── catalogos/
│       └── api/                # hooks de catálogos cross-empresa
└── routes/
    └── _app/
        └── compras/
            ├── requisiciones/
            │   ├── index.tsx        # P1
            │   ├── nueva.tsx        # P4
            │   └── $id.tsx          # P3
            ├── pendientes.tsx       # P2
            └── admin/
                └── aprobadores.tsx  # P9
```

---

## 12. Accesibilidad, i18n y responsive

### 12.1 Accesibilidad

**[Asunción F3]** Objetivo: WCAG 2.1 AA.

Heredamos gratis de shadcn/Radix:

- Focus management en modales y popovers.
- ARIA labels en primitives.
- Navegación por teclado.

**Atención particular**:

- **Tablas largas (P1, P2, editor de líneas P5)**: header sticky,
  `<caption>` con resumen, `aria-rowcount`/`aria-rowindex` cuando
  paginamos virtualizado. Tab order razonable.
- **Modales de confirmación (P6)**: focus trap, `aria-labelledby`,
  ESC cierra, Enter envía solo desde botones primarios.
- **Soft lock indicator (cuando exista)**: `aria-live="polite"` para
  que screen readers anuncien cuando entra/sale un colaborador.
- **Combobox de selectores**: las primitives `Combobox` de shadcn
  cubren WCAG; verificar que el resultado del fetch lazy se anuncia
  con `aria-busy`.
- **Toast de errores**: `aria-live="assertive"` para errores,
  `polite` para informativos.
- **Color contrast**: badges de estado deben tener ≥ 4.5:1. Validar
  los colores de `<EstadoBadge>` y `<NaturalezaBadge>` con un linter
  de contraste (manual antes de cada PR de UI).
- **`<CubrimientoBar>` no depende de color** (F6 Rev. 3): los 4
  segmentos usan **patrón visual distintivo** (sólido / rayas /
  punteado / cross-hatch) además del color. Los **números viven al
  lado de la barra siempre**, no solo en tooltip — para que el dato
  esté disponible sin hover (táctil/móvil/screen reader). Tooltip
  queda como detalle adicional. Valida WCAG 1.4.1 (use of color) y
  cubre el caso móvil donde no hay hover.

### 12.2 i18n y formato

**[Asunción F4]** Locale: `es-MX` único. Sin toggle. Bibliotecas
configuradas:

- Fechas: `formatDate`, `formatDateTime`, `formatDateLong` (con
  locale `es` ya importado de `date-fns/locale`). TZ fija
  `America/Mexico_City`.
- Números: `Intl.NumberFormat('es-MX', ...)` (ya en `formatMoney`).
- Moneda: MXN sin código sufijo (`$1,234.56`); otras monedas con
  código (`$1,234.56 USD`).

**Strings**: hard-coded en español por componente (igual que el
shell actual). Si en v2 se requiere multi-locale, encapsular cada
string en un helper que mañana sea `t('key')`. Por ahora no
introducimos `react-intl` ni similar (overhead innecesario).

### 12.3 Responsive / mobile

**[Asunción F1]** Captura desktop-only (≥ 1280px). El editor de
líneas asume tabla densa; en pantallas pequeñas se vería degradado
pero técnicamente funciona.

**[Asunción F2]** Bandeja del autorizador (P2) sí vale móvil:

- Breakpoints: `<768px` mobile / `≥768px` tablet+ / `≥1280px` full.
- En mobile, P2 cambia layout a cards apilables: cada RQ una card
  con folio + monto + depto + acciones primarias (Aprobar /
  Rechazar). El detalle se abre full-screen.
- P1 y P3 también degradan razonablemente con cards, aunque la
  experiencia full está pensada desktop.

Sidebar:

- Mobile (`<768px`): sidebar oculto por default, abierto vía
  hamburger en topbar (componente nuevo). Cuando se abre, ocupa
  full height + overlay.
- Tablet+: sidebar fijo como hoy.

> El shell actual no implementa el toggle móvil del sidebar. UI
> Compras lo introduce si entrega P2-mobile.

---

## 13. Patrones UX transversales

Patrones que aplican a múltiples pantallas y deben definirse una sola
vez para evitar que cada PR los reinvente. Cada sub-sección decide
qué entra en v1 y qué se difiere.

### 13.1 Estados de loading / empty / error por pantalla

**[Decidido]** Cada pantalla con datos asíncronos debe tener los **3
estados explícitamente diseñados**, no solo el "happy path". Un PR
sin los 3 estados no se mergea.

| Pantalla | Loading | Empty | Error |
|---|---|---|---|
| **P1 Bandeja general** | `<TableSkeleton rows={8} columns={...} />` | "Aún no tienes requisiciones." + CTA "Nueva requisición" (si tiene `crear`) | `<ErrorState>` con `traceId` + botón "Reintentar" |
| **P2 Bandeja pendientes** | Skeleton cards / table según breakpoint | "No hay requisiciones pendientes de tu autorización." (con icono ✨ o equivalente) | `<ErrorState>` + Reintentar |
| **P3 Detalle** | Skeleton de cabecera + N filas de líneas | (no aplica, GET por id) | 404 → fallback con CTA "Volver a bandeja"; 403 → "No tienes acceso" (ver §13.6); 5xx → `<ErrorState>` |
| **P4 Nueva** | (render inmediato; selectores cargan lazy con su propio spinner) | (no aplica) | Errores inline en campos vía `applyServerErrors` + toast con `title` |
| **P5 Editor de líneas** | Skeleton de filas mientras carga el detalle | "Esta requisición no tiene líneas. Agrega la primera." (en `Borrador`) o "Sin líneas registradas." (read-only) | Inline error en la fila afectada o toast por mutación fallida |
| **P6 Modal motivo** | (catálogo cacheado 1h; carga sincrónica casi siempre) | (no aplica) | Toast + cierra modal sin acción |
| **P9 Admin aprobadores** | Skeleton table | "No hay aprobadores designados. Agrega el primero." | `<ErrorState>` + Reintentar |

**Componentes auxiliares en `components/erp/feedback/`** (nuevos en
F0):

- `<EmptyState icon={...} title="..." description="..." action={...} />`
  — patrón estándar.
- `<ErrorState problem={apiError.problem} onRetry={...} />` — muestra
  `title` + `detail` + `traceId` para soporte.
- `<TableSkeleton rows={8} columns={[...]} />` — wrapper de Skeleton
  shadcn ajustado a la forma de cada tabla.

### 13.2 Auto-save y draft protection en captura

**[Decidido v1]** Protección básica anti-pérdida en P4 (cabecera) y
P5 (editor de líneas en `Borrador`). Costo bajo, evita el pánico
clásico de "perdí 15 líneas porque se cayó la red".

**Mecanismos**:

1. **`beforeunload` handler** mientras el form tiene cambios sin
   guardar (`form.formState.isDirty`). El navegador muestra el
   diálogo nativo "¿Seguro que quieres salir?". Hook reusable
   `useUnsavedChangesGuard(isDirty)` en `lib/hooks/`.
2. **Borrador en `localStorage`** para P4 cabecera. Key:
   `compras:rq:draft:nueva:<userId>:<empresaId>`. Persiste con
   debounce 500ms cada vez que el form cambia. Al montar P4, si
   existe un draft, modal opcional: "Tienes un borrador guardado de
   hace X. ¿Recuperar?" con botones "Recuperar" / "Descartar".
   Borrar tras submit exitoso.
3. **Para P5 (líneas)**: el agregado backend ya persiste cada línea
   independientemente vía POST/PATCH/DELETE. Las líneas que ya
   hicieron round-trip exitoso quedan en BD. Las que están en el
   formulario abierto (dialog Add/Edit) sí se pierden si cae la red
   — `beforeunload` aplica al dialog también; opcional draft de
   `LineaDialog` en localStorage con key
   `compras:rq:linea-draft:<requisicionId>:<lineaId|new>`.

**Diferido v1.1**: sincronización entre pestañas (BroadcastChannel),
restauración cross-device.

### 13.3 Confirmaciones destructivas con motivo (P6)

**[Decidido]** El backend exige `motivoId` (catálogo) + `motivoTexto`
opcional para Eliminar / Rechazar / Cancelar. **UI única** vía
componente `<ModalMotivo>` reutilizable, no inventado por pantalla.

**Shape**:

```text
┌─────────────────────────────────────────┐
│  ⚠️ Rechazar requisición MID2026-000042 │
│                                         │
│  Esta acción es definitiva. La RQ no    │
│  podrá volver a Borrador ni re-enviarse │
│  a autorización.                        │
│                                         │
│  Motivo *                                │
│  [ Selector ────────────────────  ▼ ]   │
│                                         │
│  Detalle (opcional / requerido)          │
│  [ Textarea autosize, max 500 ─────── ] │
│                                         │
│  [ Cancelar ]    [ Confirmar rechazo ]  │
└─────────────────────────────────────────┘
```

**Variantes** (gateadas por `aplicaA` del motivo):

- **Eliminar** (`aplicaA=Eliminacion`): título y botón rojos. Texto:
  "Esta requisición se moverá al estado Eliminada y ya no podrá ser
  editada."
- **Rechazar** (`aplicaA=Rechazo`): título rojo, botón "Confirmar
  rechazo".
- **Cancelar** (`aplicaA=Cancelacion`): título naranja, botón
  "Confirmar cancelación". Texto extra: "Las reservas de stock se
  liberarán y cualquier OC borrador asociada se cancelará."

**Validación cliente**:

- `motivoId` requerido (Zod).
- Si el motivo seleccionado tiene `permiteTextoLibre=true`, la
  textarea pasa de "Detalle (opcional)" a "Detalle (requerido)" y
  Zod lo exige antes de habilitar el botón de confirmación.
- El botón de confirmación está deshabilitado hasta cumplir las dos
  condiciones, con tooltip explicativo.

**Submit** gateado adicionalmente por permiso de la acción
(`eliminar`, `rechazar`, `cancelar`) — el backend rechaza con 403
de todas formas.

### 13.4 Búsqueda global por folio

**[Diferido v1.1]** Una caja de búsqueda en la topbar para localizar
una RQ por folio (`MID2026-001234`) es valiosa para soporte. **No
entra v1.**

**Workaround v1**: el filtro de búsqueda por folio en P1 cubre el
caso (client-side sobre la página actual). Si soporte necesita una
RQ específica que no está en su rango actual, paginar o consultar
BD directamente.

**v1.1** (cuando se priorice):

- Endpoint backend `GET /api/v1/compras/requisiciones/buscar?q=<folio>`
  con permiso `leer`, filtra por empresa del JWT.
- UI: input en topbar con shortcut `Cmd+K` / `Ctrl+K`, dropdown de
  resultados con folio + estado + monto + depto. Click → P3
  detalle.
- Componente `<GlobalSearch>` reusable en `components/erp/search/`
  (futuro fundador para CxC, OC, etc.).

### 13.5 Notificaciones in-app

**[Diferido v1.1 a v2]** Badge en topbar con "tienes 3 RQs
pendientes de autorizar" requiere uno de:

1. **SignalR push** desde `<CollaborationHub>` (deuda de plataforma
   `<CollaborationHub>`, ver §14.6).
2. **Polling periódico** de un endpoint de "summary del usuario"
   (no existe en backend).

Ninguno está disponible en v1. El módulo Notificaciones cubre el
canal email (ADR-0026), suficiente para el ciclo operativo
inicial.

**v1**: el autorizador entra a `/compras/pendientes` sin badge previo
y ve sus RQs. Si quiere atajo desde otra pantalla, lo encuentra en
el sidebar.

**Cuando el hub esté disponible**, evaluar con el cliente si quiere
in-app push y abrir un ticket de scope (UI badge + summary endpoint
backend + plantilla por evento).

### 13.6 Permissions-denied UX (403 vs 404)

**[Decidido]** Sigue ADR-0007 y la política del backend definida en
[04-cuidados-infra.md](04-cuidados-infra.md) §8.1:

- Sin permiso `leer`, **403** sin importar si el id existe.
- Con permiso `leer`, id de otra empresa → **404** (para no leak de
  existencia cross-empresa).
- Con permiso `leer`, id inexistente → **404**.

**Tratamiento UI** en P3 detalle (única pantalla GET-by-id donde
puede ocurrir):

| Status | Render |
|---|---|
| **403** | Página completa con icono de candado: "No tienes permiso para ver esta requisición. Si crees que es un error, contacta a tu admin." + CTA "Volver a bandeja". Loguea `traceId` en consola para debugging (suele ser bug de UI: navegación a una RQ que no debería ser accesible). |
| **404** | Página completa con icono neutro: "Esta requisición no existe o no pertenece a tu empresa actual." + CTA "Volver a bandeja". **Sin distinguir** entre los dos casos para no leak de existencia. |
| **5xx** | `<ErrorState>` con mensaje genérico + `traceId` + Reintentar. |

**Mutaciones** (POST/PATCH/DELETE) no tienen pantalla dedicada — los
errores van a toast con la convención de §8.1 de este doc.

### 13.7 Glosario y onboarding de términos del dominio

**[Decidido v1]** Los términos del modelo (`Naturaleza`,
`Cubrimiento`, `EnSurtido`, `Autorizada`, etc.) no son evidentes
para un usuario nuevo. UI ofrece **dos capas**:

1. **Tooltips contextuales** sobre los términos clave en cada
   pantalla:
   - `<EstadoBadge>` con `<Tooltip>` que explica el estado en una
     frase ("EnSurtido: la requisición está autorizada y esperando
     que el almacén o el proveedor entreguen el material").
   - `<NaturalezaBadge>` con tooltip ("Crítico: requiere autorización
     N2 sin importar el monto").
   - `<CubrimientoBar>` con números visibles al lado siempre + patrón
     visual + tooltip detallado (ya cubierto en F6 Rev. 3).
   - Ícono `?` junto a campos no obvios en P4 (clasificación,
     prioridad, almacén destino).

2. **Página de ayuda estática** en `/compras/ayuda` (link en topbar
   `?` o en el footer del sidebar):
   - **Glosario** de términos del dominio, alineado con el
     [01-diseno.md](01-diseno.md).
   - **Diagrama del ciclo de vida** de la RQ (los 8 estados con
     transiciones; reproduce el §5.2 del 01-diseno.md).
   - **Tabla simplificada de matriz de aprobación** (cuándo requiere
     N2, basada en §3.bis.2).
   - **FAQ corto**: "¿Qué es Cubrimiento?", "¿Por qué no puedo
     aprobar esta RQ?", "¿Qué pasa al cancelar?".
   - Versionado con el doc 01-diseno.md (cambios de modelo se
     reflejan aquí en el mismo PR).

**Componentes**:

- `<DomainTermTooltip term="EnSurtido">` que lee de un diccionario
  centralizado (`features/compras/lib/glosario.ts`). Coherente con
  el resto del sistema cuando otros módulos lleguen.
- Página `/compras/ayuda` como ruta `routes/_app/compras/ayuda.tsx`
  con contenido MDX o markdown servido como string.

### 13.8 Exportación a PDF

**[Diferido v1.1]** Imprimir o descargar PDF de una RQ es un pedido
casi seguro post-go-live (firmas físicas, archivo, envío al
proveedor). **No entra v1** porque requiere endpoint backend
(ADR-0025 PDF generation con QuestPDF) que no existe todavía y
levantar la infra de generación de PDFs es un track propio.

**Workaround v1 (incluido en v1)**: el usuario imprime con el
navegador (`Ctrl+P`). Para que el resultado sea legible, P3 detalle
define un **stylesheet de impresión** (`@media print`) que:

- Oculta sidebar, topbar y botones de acción.
- Formatea cabecera + líneas + cubrimiento + autorizaciones en una
  sola página A4 portrait.
- Incluye membrete simple (logo Millet + folio + fecha de impresión
  + usuario que imprime).
- Ajusta colores a alto contraste para impresoras B/N.

**Costo bajo** (un stylesheet bien hecho), **valor alto** (cubre el
caso 80% de los pedidos sin endpoint nuevo).

**v1.1**:

- Endpoint backend `GET /api/v1/compras/requisiciones/{id}/pdf` con
  QuestPDF + plantilla con membrete corporativo, firmas digitales si
  aplica.
- Botón "Descargar PDF" en P3.

### 13.9 Breadcrumbs y preservación de filtros

**[Decidido v1]** Los filtros de bandeja se **preservan** al navegar
de bandeja → detalle → bandeja. La gente lo odia si se pierden, y
TanStack Router lo facilita gratis con search params.

**Mecanismo**: TanStack Router expone los filtros como **search
params** validados con Zod (ya documentado en ADR-0023 §"Routing").
La ruta de bandeja declara su `validateSearch`:

```typescript
// routes/_app/compras/requisiciones/index.tsx
const BandejaSearchSchema = z.object({
  estado: z.nativeEnum(EstadoRequisicion).optional(),
  departamentoId: z.string().uuid().optional(),
  requisitanteId: z.string().uuid().optional(),
  q: z.string().optional(),  // búsqueda client-side por folio
  offset: z.coerce.number().int().min(0).optional().default(0),
  limit: z.coerce.number().int().min(1).max(200).optional().default(50),
});
```

Cuando el usuario aplica filtros, la URL se vuelve
`/compras/requisiciones?estado=EnAutorizacion&departamentoId=...`.
Click en una RQ → `/compras/requisiciones/<id>` (los search params
de la bandeja **no** se llevan al detalle, pero el back del navegador
o el botón "Volver a bandeja" los restaura).

**Breadcrumbs** (componente nuevo `<Breadcrumbs items={...} />` en
`components/erp/`):

- Top de cada pantalla: `Compras / Requisiciones / <folio>` o
  `Compras / Pendientes / <folio>` o `Compras / Admin /
  Aprobadores`.
- Click en un nivel intermedio navega a esa ruta **preservando los
  filtros** vía `useSearch()` para reconstruir el query string del
  ancestro (cuando aplique).

**Persistencia entre sesiones**: los filtros viven solo en URL. **No
se guardan en localStorage**. Si el usuario cierra el navegador y
vuelve, parte de la bandeja sin filtros. Aceptable; persistir
genera confusión ("¿por qué me aparecen filtros que no puse hoy?").

**Excepción**: el filtro implícito "mis requisiciones" en P1
(`requisitanteId = current_user`) se aplica automáticamente cuando
el usuario entra **sin** parámetros de búsqueda. Se desactiva
cualquier momento que el usuario añada / cambie filtros (porque
incluye explícitamente `requisitanteId`).

---

## 14. Brechas con el backend

Tickets a abrir contra el equipo backend antes / durante la
implementación de UI. **Ninguna bloquea arrancar P1/P3/P4 con scope
acotado**, pero algunas restringen alcance.

### 14.1 [P0] No hay endpoint `GET /historico` de RQ

El 01-diseno.md §9 lista
`GET /api/v1/compras/requisiciones/{id}/historico` (línea de
tiempo). El código backend **no lo expone**
([RequisicionesEndpoints.cs](../../../backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs)
no contiene `MapGet("/historico")` ni equivalente). Lo único que el
detalle expone es la lista de `Autorizaciones` (que sí basta para un
timeline reducido pero **deja ciegos a soporte y al usuario** ante
escenarios típicos: dos rechazos sucesivos, autorización tardía,
cancelación post-aut, recepciones parciales).

**Por qué P0** (elevado de P1 en Rev. 3): el modelo de auditoría
del ADR-0008 (`AuditSaveChangesInterceptor` + `core.audit_log`) **ya
captura todas las transiciones** del agregado. El faltante es solo
exponerlo vía un endpoint que lea esa tabla filtrando por
`requisicionId` y formatee al shape que la UI necesita. **No es
arquitectura faltante; es un endpoint chico de un día de trabajo
backend.** Si esto se difiere a "post-MVP", probablemente nunca se
haga, y la primera vez que un cliente pregunte "¿quién rechazó esta
RQ y cuándo?" no habrá respuesta sin abrir BD. Ese es el caso de
adopción que se pierde.

**Acción**: abrir ticket P0 al equipo backend para implementar
`GET /api/v1/compras/requisiciones/{id}/historico` con shape
`{ items: [{ tipo, actor, timestamp, payload }] }` donde `tipo` ∈
{`Creada`, `LineaAgregada`, `LineaActualizada`, `LineaEliminada`,
`Transmitida`, `AutorizadaN1`, `AutorizadaN2`, `Rechazada`,
`Eliminada`, `Cancelada`, `Cubrimiento`, `Recepcion`,
`SaldoNoSurtido`, `Cerrada`}. Coordinar para que llegue **antes de
F7 (hardening v1)** — UF1-PR4 entrega el componente con timeline
reducido (solo autorizaciones del detalle); UF7-PR5 (o un PR
dedicado en F7) lo amplía cuando el endpoint exista. Si el ticket
backend no se prioriza, F7-end documenta como riesgo de adopción
explícito en el plan de UAT.

> **Nota de proceso**: este pushback vino del owner en revisión Rev.
> 3. La inercia de "si el endpoint no está, lo dejamos para v1.1"
> es exactamente el patrón que termina en deudas que nadie cierra.
> Convertirlo en ticket concreto es la mitigación.

### 14.2 [P1] No hay endpoint `PATCH /requisiciones/{id}` (editar cabecera)

El 01-diseno.md §9 lista
`PATCH /api/v1/compras/requisiciones/{id}` (editar cabecera en
`Borrador`). El código backend **no lo expone**. Las únicas
mutaciones de cabecera son `crear` y los terminales (`rechazar`,
`eliminar`, `cancelar`).

**Acción**: si el cliente edita la cabecera tras crear, requiere
recrear la RQ. UX pobre. Abrir ticket backend para `PATCH` cabecera
o confirmar que "se crea bien o se elimina y se rehace". Marcado en
§6.1 como ⚠️.

### 14.3 [Resuelta — Rev. 5] CRUD de catálogos: backend listo, UI en módulo Datos Maestros

**Actualizado en Rev. 5 (2026-05-09)**: ya no es brecha para
Compras. **El backend del CRUD está mergeado** (PR #73 — endpoints
POST/PATCH/DELETE-lógico para `compartido.proveedores` y
`compartido.articulos` con permiso `compartido.catalogos.administrar`).
**El frontend del CRUD vive en módulo Datos Maestros**, no en
Compras Requisiciones (los catálogos son transversales al ERP).

Para Compras, esto implica:

- **v1**: los selectores read-only (`<ArticuloSelector>`,
  `<ProveedorSelector>`) en UF2-PR1 consumen los GET ya disponibles.
  Sin pantallas CRUD, sin necesidad de seed inicial complicado
  (cuando el módulo Datos Maestros entregue su UI, alta directa
  desde el ERP).
- **Sin v1.1 condicional**: la promesa "CRUD en v1.1" se transfiere
  al plan del módulo Datos Maestros (con su propio scope, plan y
  breakdown). Compras Requisiciones no tiene dependencia.

Ver [docs/modulos/datos-maestros/README.md](../datos-maestros/README.md).

### 14.4 [P2] No hay endpoints de admin de umbrales por departamento

`compras.umbrales_aprobacion_departamento` es seed-only en F2-PR3 y
F9 lo refina, pero **no hay UI/endpoint para que el cliente los
ajuste**. Si el cliente cambia umbrales (escenario realista cada
trimestre), hoy requiere migración EF Core.

**Acción**: abrir ticket backend para CRUD de umbrales (post-MVP).
Mientras tanto, P9 cubre solo aprobadores; los umbrales se gestionan
fuera del ERP. UI Compras anota la limitación.

### 14.5 [P2] Política de `If-Match` no documentada explícitamente

El backend declara `ETag` en `GET /{id}` pero las mutations **no
exigen `If-Match`** vía middleware (verificado en código: no hay
filtro/handler que retorne 412 sin `If-Match`). EF Core detecta
conflictos vía `IsConcurrencyToken` (→ 409). El 04-cuidados-infra.md
hallazgo 1 marca la inconsistencia 412 vs 409 (resuelto: ADR manda
409).

**Acción**: confirmar la política. Propuesta UI: enviar `If-Match`
de todas formas (es info adicional para el handler). Si nunca se
usa, el header lo ignora. Sin riesgo. Cubre §7.5 F15.

### 14.6 [P0] `CollaborationHub` SignalR — Camino A confirmado (priorizar antes de release v1)

`<CollaborationHub>` en §8.6 del 01-diseno. **Decisión del owner
2026-05-09: Camino A**. UF8-PR1 (soft lock real) se ejecuta como
parte de release v1, antes del UAT, no post-release.

**Contexto del riesgo** (motivo del Camino A): sin awareness
colaborativo, dos usuarios pueden editar la misma RQ sin enterarse
— el primer indicio que reciben es el 409 al guardar. Combinado con
F9, "trabajé 10 minutos → guardo → 409" es destructor de adopción
en day-1. Camino A elimina el riesgo desde release v1.

**Camino A — implementación**:

- Coordinar con plataforma para cerrar `<CollaborationHub>` **antes
  de Fase 7 hardening**. Esto requiere escalamiento al owner del
  ticket de plataforma HOY (el plazo realista determina si A es
  viable o si caemos a B).
- UF8-PR1 entra al final de F7, antes del UAT. Total v1 sube de 16
  a 17 PRs.
- Awareness activo desde día 1: badge de avatares apilados, banner
  "está editando", invalidación reactiva de queries en eventos del
  hub.
- F9 robusto (preserve form state) sigue activo como defensa
  secundaria — si dos usuarios escapan al banner y conflictúan, el
  dialog reaplica cambios.

**Fallback automático a Camino B** si plataforma no puede cerrar
el hub a tiempo:

- F8 stub se queda hasta v1.1.
- F9 robusto (UF0-PR2 + UF3-PR2) compensa parcialmente: los
  conflictos no destruyen trabajo, pero la advertencia previa no
  existe.
- Plan de UAT documenta como riesgo conocido + plan de remedio en
  v1.1.

**Camino C** (aceptar riesgo + dialog simple) descartado por el
owner.

**Acción inmediata**: escalar plazo del ticket `<CollaborationHub>`
con el equipo de plataforma. Si en 4-6 semanas no hay plan claro,
fallback a Camino B sin sorpresas para release v1.

### 14.7 [P0] Selectores de Sucursal/Departamento/Almacén/Usuario sin endpoints públicos visibles

Necesarios para los selectores de cabecera y aprobadores. No están
en `backend/src/Api/Endpoints/`. Tres opciones:

1. Existen en otro endpoint group que no encontré → buscar.
2. Existen como seed con IDs hardcodeados → el frontend trae los
   IDs hardcoded como constantes (frágil).
3. No existen → abrir ticket backend para entregarlos
   (probablemente parte de un módulo Identidad expandido o un
   módulo "Catálogos organizacionales" nuevo).

**Acción**: bloquea P4 (formulario de cabecera) y P9 (designar
aprobador) hasta resolver. Marcar como `[Verificar con backend]`
explícito en el ticket de UI.

### 14.8 [P2] El `me` no expone `departamentoId` del usuario

Para auto-aplicar el filtro de bandeja por depto del usuario sin
`ver-todos-departamentos`, necesitamos saber su departamento. El
[`MeResponse`](../../../frontend/src/lib/auth/types.ts) actual solo
trae `userId`, `email`, `nombre`, `currentEmpresaId`, `permisos[]`.

**Acción**: extender `MeResponse` con `departamentoId?: string`
cuando aplique. O alternativamente, leer del `<DepartamentoSelector>`
que el usuario tiene un solo depto disponible y forzarlo. Solución
más pragmática: extender `me`.

### 14.9 [Resuelta — Rev. 5] Endpoints CRUD de catálogos cierran en backend

**Cerrada en Rev. 5 (2026-05-09)**: el backend ya entregó los
endpoints CRUD en PR #73 (POST + PATCH + DELETE-lógico para
`compartido.proveedores` y `compartido.articulos`, todos con
`Idempotency-Key` y permiso `compartido.catalogos.administrar`).

El frontend del CRUD vive en módulo Datos Maestros, no en Compras
Requisiciones. Esta entrada queda como referencia histórica de la
brecha que motivó el CRUD; ya no aplica a Compras.

---

## 15. Hallazgos para revisión

Discrepancias menores entre diseño, código y este doc:

1. **`PATCH /requisiciones/{id}` documentado pero no implementado**
   (§14.2). Diseño debe alinearse con código o backend agregar el
   endpoint.

2. **`GET /historico` documentado pero no implementado** (§14.1).
   Idem.

3. **Mismo hallazgo "412 vs 409"** del 04-cuidados-infra (hallazgo
   1) — repetido aquí porque afecta UX directamente. Confirmado:
   código y ADR-0012 mandan 409. Diseño 01-diseno.md §9 dice 412 —
   debería corregirse.

4. **`POST /eliminar` (en lugar de `DELETE`)** — el código usa POST
   con body `{motivoId, motivoTexto?}`; diseño 01-diseno.md §9 dice
   `DELETE /api/v1/compras/requisiciones/{id}`. La elección de POST
   es razonable (conviene preservar body), pero el diseño debería
   actualizarse.

5. **`POST /eliminar` NO requiere `Idempotency-Key`** mientras que
   los demás terminales (rechazar, cancelar) sí. El comentario del
   código justifica: "pre-aut sin impacto fiscal". Razonable; UI lo
   respeta.

6. **`compartido.catalogos.administrar` no aparece en el manifiesto
   §8.3 del 01-diseno.md** pero existe en código. **Lo consume el
   módulo Datos Maestros**, no Compras Requisiciones (Rev. 5);
   debe agregarse al manifiesto del 01 cuando arranque ese módulo.

7. **El detalle de RQ no expone `Cubrimiento` aún** — verificado por
   el shape de DTOs implícito en el handler (no leí cada DTO). Si
   `RequisicionResponse` no incluye los campos `cantDeAlmacen`,
   `cantDeCompra`, `cantRecibida` por línea, la `<CubrimientoBar>`
   no tiene datos que mostrar. **[Verificar con backend]** antes de
   diseñar la barra.

---

## 16. Plan de ejecución — primeras pantallas

> Este doc es de **diseño**, no de implementación. Pero conviene
> indicar el orden razonable cuando se materialice:

1. **PR 1 — Plataforma de UI Compras**: agregar permisos canónicos
   (mirror), `lib/api/` con `apiRequest`, `ApiError`, idempotency,
   etag. Stub de `<CollaborationIndicator />`. Componentes shadcn
   adicionales (`dialog`, `select`, `combobox`, `data-table`,
   `form`, `toast`, `calendar`, `card`, `badge`, `skeleton`).
   Habilitar el item del sidebar.
2. **PR 2 — Bandeja general (P1) read-only + Detalle (P3) read-only**:
   useRequisiciones + useRequisicion. Sin acciones. Permite verificar
   shape de DTOs contra el código real.
3. **PR 3 — Crear (P4) + Editor de líneas (P5) básico**: solo
   `Borrador`. Activa flow capturador hasta transmitir. Incluye
   `<ArticuloSelector>`. Bloquea hasta resolver §14.7.
4. **PR 4 — Transmitir + Bandeja autorizador (P2) + Aprobar/Rechazar**.
5. **PR 5 — Cancelar / Eliminar + Modal motivos (P6)**.
6. **PR 6 — Cubrimiento visible (P3) + comprador notas**: requiere
   confirmar §15.7.
7. **PR 7 — Admin aprobadores (P9)**.
8. **PR 8 — Soft lock real**: SignalR client + UI activa
   (`<CollaborationHub>` ya está mergeado en backend, ver §14.6).
9. **PR 9+ — UAT polish, accesibilidad audit, mobile P2**.

Sizing aproximado por PR: S–M cada uno, **total 16 PRs para v1**
(ver [07-frontend-pr-breakdown.md](07-frontend-pr-breakdown.md)
Rev. 6 para granularidad real). Calibrar contra capacidad del
equipo de UI.

---

## 17. Cambios respecto a versiones previas

### Rev. 5 — separación arquitectónica: catálogos a módulo Datos Maestros (2026-05-09)

Tras verificar el estado real del backend post-merge de PR #68:
todos los endpoints CRUD de catálogos cross-empresa ya están
mergeados (PR #73 backend). El owner identificó que **el frontend
del CRUD no debe vivir en el módulo Compras** — los catálogos son
transversales (los consumirán CxC, OC, CxP, Activos Fijos cuando
lleguen) y se administran desde un módulo Datos Maestros separado.

**Cambios al scope del módulo Compras Requisiciones (FE)**:

- **P10 (reclasificar naturaleza bulk) sale**. Era operación sobre
  `compartido.articulos` que vivía en `/compras/admin/...`. Se
  traslada a módulo Datos Maestros.
- **P11 (proveedores CRUD) y P12 (artículos CRUD)** que en Rev. 4
  estaban anunciadas para v1.1 — **ya no son scope de Compras**.
  Se trasladan a Datos Maestros (con su propio plan/breakdown).
- **P9 (admin de aprobadores) se queda** en Compras: tabla
  `compras.aprobadores_departamento`, permiso
  `compras.aprobadores.administrar` — config local del módulo.
- **F10 reescrita**: catálogos read-only desde Compras (selectores
  consumen los GET); el CRUD vive en Datos Maestros.
- **§14.3 y §14.9 cerradas**: el backend ya entregó CRUD; lo
  pendiente es la UI, que no es scope de Compras.
- **Total v1**: 17 → **16 PRs** (UF6-PR2 reclasificar naturaleza
  sale; F6 baja a 1 PR con solo P9 aprobadores).
- **Total pantallas**: 10 → **9** (P10 sale).
- **Inventario top-level**: 6 → **5 rutas** (P1, P2, P3, P4, P9).

**Nuevo módulo de UI (placeholder en este PR)**:
[docs/modulos/datos-maestros/README.md](../datos-maestros/README.md)
queda como placeholder. Cuando arranque, tendrá su propio
01-diseno.md, plan, breakdown.

Sin cambios en F1, F2, F3, F4, F8, F9 (asunciones validadas Rev. 4
siguen vigentes). Las brechas backend de §14 que aún aplican a
Compras (§14.1 historico, §14.2 PATCH cabecera, §14.4 umbrales,
§14.5 If-Match, §14.6 hub, §14.7 selectores org, §14.8 me) están
todas resueltas en backend post-merge de PR #68 — confirmar
shapes en UF0-PR1.

### Rev. 4 — sesión de validación de asunciones con owner (2026-05-09)

Las 6 asunciones críticas (F1, F2, F4, F8, F9, F10) se validaron en
sesión 1-a-1 con el owner. Resultados:

- **F1 — Captura desktop-only**: confirmada CON CLARIFICACIÓN. El
  baseline NO es 1280px (trampa típica) sino **1366×768** (laptop
  corporativa típica MX). Agrega: toggle "Mostrar columnas
  contables" en P5 + tabla de líneas scrollea internamente con
  header sticky.
- **F2 — Mobile P2 (bandeja autorizador)**: confirmada (vale la
  inversión, ~3 días en UF7-PR3).
- **F4 — es-MX único locale**: confirmada (sin i18n).
- **F8 — Soft lock**: **Camino A confirmado** — UF8-PR1 entra antes
  del UAT (no post-release). §14.6 reescrita con la decisión.
  Total v1: 16 → **17 PRs**. Bloqueante operativo nuevo: coordinar
  plazo de `<CollaborationHub>` con plataforma HOY.
- **F9 — Conflict 409**: confirmada con dos sub-decisiones de UX
  cerradas: (a) **diff filtrado** (solo solapes) con expandible
  "Ver todos los cambios remotos"; (b) **"Reaplicar mis cambios"
  como botón primario default**. §8.4 actualizada con el flujo
  completo.
- **F10 — Catálogos**: **CAMBIA RAÍZ**. La asunción Rev. 3 era
  "catálogos no editables porque vienen de SAP". El owner clarificó
  que **el ERP es la fuente de verdad**, SAP solo seed inicial
  one-shot. **Opción B confirmada**: v1 sale read-only (seed via
  script en cutover), CRUD completo en v1.1 (P11 proveedores + P12
  artículos). §14.3 elevada de P2 a P1. **Nueva brecha §14.9** con
  endpoints CRUD para v1.1.

**Action items nuevos surgidos de la sesión**:

- [ ] Coordinar plazo de `<CollaborationHub>` con plataforma HOY
      (define si Camino A es viable o cae a B).
- [ ] Decidir mecanismo de seed inicial de catálogos para cutover:
      script SQL crudo, importer SAP (F7-PR2 backend deferida),
      o endpoint admin temporal one-shot.
- [ ] Comunicar al cliente la limitación de v1: alta de proveedor
      durante v1 → v1.1 requiere contactar a soporte/dev.
- [ ] Agregar §14.9 (CRUD catálogos v1.1) al backlog post-release v1.

Las asunciones restantes (F3, F5, F6, F7, F11–F15) NO se validaron
en esta sesión y siguen en estado "propuesta del tech lead". Sin
bloqueantes nuevos detectados en ninguna.

Plan (06) y PR breakdown (07) actualizados con las decisiones.

### Rev. 3 — pushbacks del owner sobre F6/F7/F8/F9 (2026-05-09)

Cuatro asunciones reescritas tras review del owner — todas
identificaban riesgos de adopción reales que la versión Rev. 2
infravaloraba:

- **F6 (CubrimientoBar)** — el tooltip era trampa de a11y y móvil.
  Nuevo default: **barra + números visibles al lado siempre +
  patrón visual distintivo** (no solo color). Tooltip queda como
  detalle adicional. Cumple WCAG 1.4.1 y funciona en táctil sin
  hover. §12.1 actualizada para reflejar el patrón.
- **F7 (Timeline)** — convertido de "asunción condicional" a
  "ticket P0 al backend". El modelo de auditoría ADR-0008 ya
  captura las transiciones; falta solo exponer un endpoint chico.
  Si se difiere, probablemente nunca se haga, y la primera RQ con
  dos rechazos deja a soporte sin respuesta. §14.1 elevada de P1
  a **P0** con re-escritura del cuerpo (ticket concreto, shape de
  payload sugerido, plazo "antes de F7 hardening").
- **F8 (Soft lock NoOp)** — elevado a **P0** con análisis de
  riesgo combinado con F9. Sin awareness, dos usuarios chocan al
  guardar. Combinado con F9 destructivo = day-1 destructor de
  confianza. §14.6 reescrita con tres caminos de mitigación
  (priorizar hub vs reforzar F9 vs comunicar al cliente),
  recomendación del tech lead documentada.
- **F9 (Conflict 409)** — el dialog v1 ya no es "refrescar y
  perder cambios". **Preserva form state local + permite
  reaplicar**. +1-2 días de trabajo, salva al sistema de mala
  fama. §8.4 reescrita con el flujo completo (caso 15 líneas).
  §8.5 nueva con el caso simplificado (aprobar sin form que
  preservar). El componente detecta automáticamente cuál modo
  aplica.

**Renumeración bug fix**: las sub-secciones de §14 brechas seguían
numeradas `### 13.X` desde Rev. 2 (bug del rename). Corregidas a
`### 14.X` y elevadas las prioridades (§14.1 P1→P0, §14.6 P1→P0).

Sin cambios en inventario de pantallas. Plan (06) y PR breakdown
(07) actualizados con los items derivados (UF0-PR3 conflict dialog
con preserve-form-state, UF1-PR4 ampliación de timeline + sección
para cuando endpoint llegue, UF5-PR1 con números visibles + patrón,
UF8 fase decisión: priorizar antes de release o aceptar riesgo).

### Rev. 2 — patrones UX transversales (2026-05-09)

Tras feedback del owner sobre piezas de UX que no estaban
explícitamente cubiertas, se agrega §13 "Patrones UX transversales"
con 9 sub-secciones que decisión por sub-sección qué entra v1 y qué
se difiere:

- **§13.1** Estados de loading / empty / error por pantalla
  (todos v1, obligatorio por PR — gate de mergeable).
- **§13.2** Auto-save y draft protection en captura (v1: beforeunload
  + localStorage para P4 cabecera y opcional para LineaDialog).
- **§13.3** Confirmaciones destructivas con motivo (refinamiento del
  shape de P6, variantes por `aplicaA`).
- **§13.4** Búsqueda global por folio (diferida v1.1).
- **§13.5** Notificaciones in-app (diferidas v1.1+, dependen de
  SignalR / endpoint de summary).
- **§13.6** Permissions-denied UX (decisión 403 vs 404 alineada con
  ADR-0007 + 04-cuidados-infra §8.1).
- **§13.7** Glosario y onboarding (v1: tooltips + página de ayuda
  estática `/compras/ayuda`).
- **§13.8** Exportación a PDF (diferida v1.1; v1 entrega `@media
  print` para uso con Ctrl+P).
- **§13.9** Breadcrumbs y preservación de filtros (v1: TanStack
  Router search params + componente `<Breadcrumbs>`).

Renumeración: brechas backend §13 → §14, hallazgos §14 → §15, plan
de ejecución §15 → §16, changelog §16 → §17. Todas las referencias
internas se actualizaron.

Sin cambios en asunciones F1–F15 ni en el inventario de pantallas.
Plan (06) y PR breakdown (07) actualizados para reflejar los items
v1 nuevos (estados, auto-save, glosario, breadcrumbs).

### Rev. 1 — versión inicial (2026-05-09)

Primer corte del diseño de frontend. Calibrado contra:

- 01-diseno.md Rev. 14
- 02-plan-implementacion.md Rev. 5
- 03-pr-breakdown.md Rev. 2
- 04-cuidados-infra.md Rev. 1
- ADRs 0002, 0007, 0010, 0012, 0017, 0020, 0023
- Auditoría del repo `backend/src/Api/Endpoints/Compras/` y `Catalogos/`
- Auditoría del repo `frontend/` (stack al 2026-05-09)

8 brechas con backend identificadas (§14). 7 hallazgos para revisión
de docs (§15). 15 asunciones de frontend (§3) que requieren
validación con cliente / equipo.

Pendiente de revisión con el owner antes de iniciar la
implementación de UI.
