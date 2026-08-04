# Plan de implementación frontend — Módulo Administración

> **Construido sobre:** [05-frontend-diseno.md](05-frontend-diseno.md) (Rev. 1) y los PRs backend de [03-pr-breakdown.md](03-pr-breakdown.md).
>
> **Estado:** propuesta de plan para revisión. Sizing en bandas (XS/S/M/L/XL).
>
> **Fecha:** 2026-05-13.

---

## 0. Cómo leer

- Sizing en bandas (mismas que el backend):
  - **XS** ≈ 1–2 días
  - **S** ≈ 3–5 días
  - **M** ≈ 1–2 semanas
  - **L** ≈ 3–4 semanas
- Las fases frontend (`UF-Admin-PRx`) **siguen** a los PRs backend correspondientes (`F-Admin-PRx`) — el backend mergea primero para que tipos TS estén disponibles.
- **Reuso primero**: cada fase indica qué pieza hereda del shell o Compras antes de listar lo nuevo.

---

## 1. Resumen ejecutivo

**Objetivo:** entregar la UI completa del área `/admin/*` siguiendo el diseño aprobado en `05-frontend-diseno.md`.

**Estrategia:**

1. **Andamio primero, features después.** UF-Admin-PR1 entrega registry + landing + engrane wireado. Cada feature siguiente suma una card al registry.
2. **Reuso del shell de Compras.** `AppLauncherModal`, `AppLauncherCard`, `Sheet` provider pattern, `Topbar`, inline forms ya implementados en Compras se replican.
3. **Tipos TS autogenerados.** OpenAPI → TS (ADR-0017) elimina el costo de mantener tipos manuales para los endpoints admin.
4. **Tests E2E mínimos** por feature: happy path con Playwright (ADR-0016). Test exhaustivo posterior cuando el UAT lo pida.

**Pendientes que NO bloquean arranque:**

- i18n — sin i18n en MVP; strings hardcoded.
- Export CSV — diferido.
- Dark mode — heredado del shell, sin trabajo específico.

---

## 2. Prerrequisitos — audit del repo (2026-05-13)

| Prerrequisito | Estado | Detalle |
|---|---|---|
| `AppShell.tsx` + sidebar + topbar | ✅ existe | Topbar tiene engrane `disabled` ([Topbar.tsx:182](../../../frontend/src/components/layout/Topbar.tsx#L182)) listo para wireup. |
| `AppLauncherModal` + `AppLauncherCard` (ADR-0032) | ✅ existe | Cards agrupadas. Reusado en `/admin` landing. |
| Patrones P1/P2/P3/P4 + inline forms | ✅ documentados | `frontend/docs/patrones-compras.md`. |
| `EmpresaSelector` | ✅ existe | Sin cambios; consumirá empresas desde Administracion (post-refactor backend). |
| `useAuth()` + permisos cacheados (Zustand) | ✅ existe | `useAdminAccess()` lo consume. |
| OpenAPI → TS pipeline | ✅ existe | Tipos admin se generan automáticamente al actualizar `openapi.json`. |
| `Sheet` provider pattern (`useNuevaOC`, `useNuevaRequisicion`) | ✅ existe | Replicar para `useNuevaEmpresa`, `useNuevoRol`, `useNuevoUsuario`. |
| Playwright + harness | ✅ existe (UF7-PR3 OC) | E2E para Admin sigue el patrón. |
| shadcn/ui components (`Card`, `Sheet`, `Tabs`, `Form`, `Switch`, `DatePicker`, `Select`, `Collapsible`, `Dialog`) | ✅ disponibles | Sin instalación nueva. |
| `react-diff-viewer` | ⏳ no instalado | UF-Admin-PR7 (auditoría detail drawer) lo agrega. |

---

## 3. Fases

### UF-Admin-PR1 — Andamio + Auto Settings Form + Compras adoption (M)

**Depende de:** F-Admin-PR1.1 (backend SettingsSchema), F-Admin-PR1.2 (backend permisos del andamio).

**Reuso:** `AppLauncherModal`, `AppLauncherCard`, `Topbar`.

**Lo nuevo:**

- `frontend/src/lib/admin/registry.ts` con tipo `AdminSection` + barrel.
- Hooks `useAdminRegistry()`, `useAdminAccess()`, `useSettingsSchema(modulo)`.
- Página `/admin` (landing) con grupos.
- Página `/admin/<modulo>/settings` con `<AutoSettingsForm>`.
- Engrane wireado en `Topbar.tsx:182`.
- Card de Compras en `frontend/src/modules/compras/admin.ts` con `displayMode = 'custom'` (link a `/compras/configuracion` existente, ADR-0033).
- Tests Playwright: engrane oculto sin permisos, visible con permisos; landing renderiza secciones; link a `/compras/configuracion` funciona; `/admin/compras/settings` lee schema y muestra card de "abrir configuración".

**Sizing:** M.

---

### UF-Admin-PR2 — Empresas + Sucursales + Departamentos (M)

**Depende de:** F-Admin-PR2.3 (endpoints backend).

**Reuso:** patrones P3 (master-detail), P4 (Sheet), inline forms (memoria `feedback_inline_no_modal_para_items`).

**Lo nuevo:**

- Rutas: `/admin/empresas`, `/admin/empresas/$id`, `/admin/departamentos`.
- Componentes: `EmpresasMasterDetail`, `EmpresaDetalle`, `EmpresaDatosForm`, `SucursalesInlineForm`, `DepartamentosInlineForm`, `SheetNuevaEmpresa`.
- Providers: `NuevaEmpresaProvider` + hook `useNuevaEmpresa()`.
- Cards en `frontend/src/modules/administracion/admin.ts` con permisos `admin.empresas.leer`, `admin.departamentos.gestionar`.
- Tests Playwright: crear empresa, agregar sucursal inline, eliminar departamento, multi-empresa aislamiento.

**Sizing:** M.

---

### UF-Admin-PR3 — Roles + matriz de permisos + grupos Entra ID (M)

**Depende de:** F-Admin-PR3.2 (endpoints), F-Admin-PR3.3 (seeds roles MVP).

**Reuso:** P3 master-detail, inline forms, `<Collapsible>` para agrupación.

**Lo nuevo:**

- Rutas: `/admin/roles`, `/admin/roles/$id`, `/admin/permisos`.
- Componentes: `RolesMasterDetail`, `RolDetalle` con 3 tabs (datos, permisos matriz, grupos Entra ID, usuarios asignados), `MatrizPermisos` (con Collapsible por módulo), `GruposEntraIdInlineForm`, `SheetNuevoRol`.
- Hook `useEntraIdGrupoSearch(q)` que llama `GET /api/v1/identidad/graph/grupos?q=`.
- Cards en `frontend/src/modules/identidad/admin.ts`.
- Tests Playwright: asignar permisos batch, asociar grupo Entra ID, eliminar rol sin asignaciones, intentar eliminar rol con asignaciones → bloqueado.

**Sizing:** M.

---

### UF-Admin-PR4 — Usuarios + asignación rol×empresa (M)

**Depende de:** F-Admin-PR4.2 (endpoints).

**Reuso:** P3, inline forms, integración con `EmpresaSelector`.

**Lo nuevo:**

- Rutas: `/admin/usuarios`, `/admin/usuarios/$id`.
- Componentes: `UsuariosMasterDetail`, `UsuarioDetalle` con 3 tabs (datos, roles por empresa, preferencias), `RolesPorEmpresaInlineForm`, `SheetNuevoUsuario`.
- Form de "Nuevo usuario" con autocompletar `EntraIdObjectId` via búsqueda por email (consulta a Graph).
- Tests Playwright: alta usuario, asignación rol×empresa, desactivar, intentar eliminar último super-admin → bloqueado.

**Sizing:** M.

---

### UF-Admin-PR4.5 — Datos Maestros UI (S)

**Depende de:** F-Admin-PR4_5 (queries enriquecidas).

**Reuso:** P3 master-detail. Forms de Proveedor/Articulo ya existen via Compras (se enriquecen con filtros).

**Lo nuevo:**

- Rutas: `/admin/datos-maestros/proveedores`, `/admin/datos-maestros/proveedores/$id`, `/admin/datos-maestros/articulos`, `/admin/datos-maestros/articulos/$id`.
- Filtros server-side: por RFC, razón social, naturaleza, estatus, etc.
- Cards en `frontend/src/modules/datos-maestros/admin.ts`.

**Sizing:** S.

---

### UF-Admin-PR5 — Catálogos SAT (M, dividible en 3)

**Depende de:** F-Admin-PR5.1, F-Admin-PR5.2, F-Admin-PR5.3.

**Reuso:** P1 + Sheet, P3 con inline.

**Lo nuevo (5.1):** `/admin/catalogos/monedas` (P3 con tipos de cambio inline). Componente `MonedaDetalle` con tab "Tipos de cambio" + inline form `TipoCambioInlineForm` (fecha + valor + origen Manual default).

**Lo nuevo (5.2):** `/admin/catalogos/condiciones-pago`, `/admin/catalogos/incoterms`, `/admin/catalogos/transportistas`, `/admin/catalogos/unidades-medida`. P1 + Sheet + edición inline desde la fila.

**Lo nuevo (5.3):** `/admin/catalogos/formas-pago`, `/admin/catalogos/usos-cfdi`, `/admin/catalogos/regimenes-fiscales`. P1 read-only (sin botones de crear/editar/eliminar).

- Cards en `frontend/src/modules/catalogos/admin.ts` (uno por catálogo, agrupados en grupo `catalogos`).

**Sizing:** M total. Dividible en UF-Admin-PR5.1, 5.2, 5.3 si conviene paralelizar.

---

### UF-Admin-PR6 — Series y folios (S)

**Depende de:** F-Admin-PR6.1 (endpoints) — UF no depende de PR6.2 (migración Compras, que es transparente).

**Reuso:** P1 + Sheet.

**Lo nuevo:**

- Ruta: `/admin/series`.
- Componentes: `SeriesBandeja`, `SheetNuevaSerie` con RadioGroup para `ReinicioPeriodo` (Eterno | Anual | Mensual) + preview del próximo folio (consume `ObtenerSerieQuery`).
- Edición inline desde la fila con confirm `<Dialog>` si cambia `ReinicioPeriodo`.
- Cards en `frontend/src/modules/administracion/admin.ts` (agrega "Series y folios").

**Sizing:** S.

---

### UF-Admin-PR7 — Auditoría + parámetros (S)

**Depende de:** F-Admin-PR7.1 (parámetros) y F-Admin-PR7.2 (auditoría).

**Reuso:** P2 bandeja filtrada (heredado de Compras OC bandeja autorizador).

**Lo nuevo:**

- Ruta `/admin/parametros`: formulario simple con campos editables (TimezoneDefault, FormatoFecha, RedondeoMonetario, etc.).
- Ruta `/admin/auditoria`: P2 bandeja con filtros (rango fechas obligatorio, módulo, recurso, acción, usuario, empresa) + paginación server-side + drawer con detalle JSON diff.
- Componente `AuditoriaDetalleDrawer` con `<Sheet>` que muestra `before`/`after` via `react-diff-viewer`.
- Sub-topbar atajo a `/admin/auditoria` (decisión §5.4).
- Tests Playwright: filtrar por rango, expandir detalle, intentar consulta sin rango → error.

**Sizing:** S. Instalar `react-diff-viewer` en este PR.

---

## 4. Paralelización entre fases

```
UF-Admin-PR1 (andamio) ─┬─→ UF-Admin-PR2 (Empresas)
                         ├─→ UF-Admin-PR3 (Roles)
                         ├─→ UF-Admin-PR4 (Usuarios)
                         ├─→ UF-Admin-PR4.5 (Datos Maestros)
                         ├─→ UF-Admin-PR5 (Catálogos)
                         ├─→ UF-Admin-PR6 (Series)
                         └─→ UF-Admin-PR7 (Auditoría + Parámetros)
```

UF-Admin-PR1 es prerrequisito obligatorio. A partir de ahí, las fases son **completamente paralelizables** desde el lado frontend (cada una toca dominios independientes), siempre que el backend correspondiente esté mergeado.

## 5. Sizing total

| PR | Sizing |
|---|---|
| UF-Admin-PR1 | M |
| UF-Admin-PR2 | M |
| UF-Admin-PR3 | M |
| UF-Admin-PR4 | M |
| UF-Admin-PR4.5 | S |
| UF-Admin-PR5 | M (dividible) |
| UF-Admin-PR6 | S |
| UF-Admin-PR7 | S |

**Total estimado:** ~8 semanas en sequential. Con paralelización (2 devs frontend), ~5–6 semanas.

## 6. Riesgos transversales

| Riesgo | Mitigación |
|---|---|
| Tipos TS desactualizados | OpenAPI pipeline regenera al cambiar `openapi.json` (ADR-0017). CI valida. |
| Inline forms vs modal por error en algún PR | Reviewer enforza memoria `feedback_inline_no_modal_para_items`. |
| Cards proliferantes en `/admin` | Si supera 15 cards, agregar filtro client-side (FADM-02). Re-evaluar agrupación. |
| Matriz de permisos muy larga | `<Collapsible>` por módulo (FADM-03). |
| Performance de bandeja auditoría con muchos rows | Paginación server-side obligatoria. Backend enforza rango de fechas (P0 §4.4). |
| Frontend out-of-sync con backend (deploy lag) | TanStack Query con `staleTime` corto en cambios sensibles. |

## 7. Próximos pasos

1. Validar este plan con el owner.
2. UF-Admin-PR1 arranca cuando F-Admin-PR1.1 + F-Admin-PR1.2 estén en `main`.
3. Las fases frontend se ejecutan en paralelo a las features backend siguientes (mismo equipo o split).

## Rev.

- **2026-05-13** — Rev. 1. Plan frontend inicial. Autor: Claude.
