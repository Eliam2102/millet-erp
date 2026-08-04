# PR Breakdown — Frontend Módulo Administración

> **Construido sobre:** [05-frontend-diseno.md](05-frontend-diseno.md) (Rev. 1) y [06-frontend-plan-implementacion.md](06-frontend-plan-implementacion.md) (Rev. 1).
>
> **Estado:** Rev. 1 — propuesta inicial.
> **Fecha:** 2026-05-13.

---

## 0. Cómo leer

- Cada fila es **un PR**. ID `UF-Admin-PR<n>` (UF = User Flow).
- **Tamaños**: XS (≤ 200 líneas netas), S (200–500), M (500–800). Techo absoluto: 800.
- **Branch naming**: `admin/uf<n>-<slug-corto>` (ej. `admin/uf1-andamio`, `admin/uf2-empresas`).
- **Dependencias backend**: PRs `F-Admin-PRx` que deben estar mergeados antes.
- **Reuso primero**: cada PR indica qué pieza hereda.

> Convención: PR mergeable = build verde + Playwright E2E feliz + revisión de 1 dev + cobertura del slice.

---

## UF-Admin-PR1 — Andamio + Auto Settings Form (M)

**Branch:** `admin/uf1-andamio`
**Depende de:** F-Admin-PR1.1, F-Admin-PR1.2

| Alcance | Archivos |
|---|---|
| Tipo `AdminSection` + barrel `adminRegistry` | `frontend/src/lib/admin/registry.ts` |
| Hooks `useAdminRegistry`, `useAdminAccess`, `useSettingsSchema(modulo)` | `frontend/src/lib/admin/use-admin-registry.ts`, `frontend/src/lib/admin/use-settings-schema.ts` |
| Componente `<AutoSettingsForm modulo>` (mapeo Tipo → componente shadcn, validación con Zod dinámico) | `frontend/src/components/admin/AutoSettingsForm.tsx` |
| Página `/admin` (landing): grid agrupado por `grupo`, header colapsable, filtrado por permisos | `frontend/src/routes/admin/index.tsx` |
| Página `/admin/<modulo>/settings` que consume `<AutoSettingsForm>` | `frontend/src/routes/admin/$modulo/settings.tsx` |
| Engrane wireado en Topbar | `frontend/src/components/layout/Topbar.tsx` (edit línea 182) |
| Card Compras con `displayMode: 'custom'` | `frontend/src/modules/compras/admin.ts` |
| Tests Playwright | `frontend/tests/e2e/admin/landing.spec.ts`, `frontend/tests/e2e/admin/auto-settings-compras.spec.ts` |

**Mergeable cuando:** engrane visible/oculto según permisos, click → `/admin`, landing muestra al menos el card de Compras (`displayMode: 'custom'`), click en card abre `/compras/configuracion`. Página `/admin/compras/settings` carga schema y muestra link "Abrir configuración".

**Sizing:** M.

---

## UF-Admin-PR2 — Empresas + Sucursales + Departamentos (M)

**Branch:** `admin/uf2-empresas`
**Depende de:** UF-Admin-PR1, F-Admin-PR2.3

| Alcance | Archivos |
|---|---|
| Ruta `/admin/empresas` (P3 master-detail) | `frontend/src/routes/admin/empresas/index.tsx` |
| Ruta `/admin/empresas/$id` con tabs (datos, sucursales, departamentos, series link, settings link) | `frontend/src/routes/admin/empresas/$id.tsx` |
| Componentes `EmpresasMasterDetail`, `EmpresaDetalle`, `EmpresaDatosForm` | `frontend/src/components/admin/empresas/*` |
| Inline forms `SucursalesInlineForm`, `DepartamentosInlineForm` (memoria `feedback_inline_no_modal_para_items`) | `frontend/src/components/admin/empresas/SucursalesInlineForm.tsx`, `DepartamentosInlineForm.tsx` |
| `SheetNuevaEmpresa` + provider `NuevaEmpresaProvider` + hook `useNuevaEmpresa()` | `frontend/src/components/admin/empresas/SheetNuevaEmpresa.tsx`, `frontend/src/providers/NuevaEmpresaProvider.tsx` |
| Cards en registry | `frontend/src/modules/administracion/admin.ts` |
| Tests Playwright | `frontend/tests/e2e/admin/empresas-crud.spec.ts` |

**Mergeable cuando:** crear empresa via Sheet, agregar sucursal inline, agregar departamento inline, desactivar empresa OK (si sin sucursales activas), aislamiento multi-empresa OK.

**Sizing:** M.

---

## UF-Admin-PR3 — Roles + matriz de permisos + grupos Entra ID (M)

**Branch:** `admin/uf3-roles`
**Depende de:** UF-Admin-PR1, F-Admin-PR3.2, F-Admin-PR3.3

| Alcance | Archivos |
|---|---|
| Ruta `/admin/roles` (P3) | `frontend/src/routes/admin/roles/index.tsx` |
| Ruta `/admin/roles/$id` con 4 tabs (datos, permisos matriz, grupos Entra ID, usuarios asignados) | `frontend/src/routes/admin/roles/$id.tsx` |
| Componente `MatrizPermisos` con `<Collapsible>` por módulo | `frontend/src/components/admin/roles/MatrizPermisos.tsx` |
| `GruposEntraIdInlineForm` con autocompletar via Graph search | `frontend/src/components/admin/roles/GruposEntraIdInlineForm.tsx` |
| Hook `useEntraIdGrupoSearch(q)` que llama backend | `frontend/src/lib/identidad/use-entra-id-grupo-search.ts` |
| Ruta read-only `/admin/permisos` (lista completa agrupada por módulo) | `frontend/src/routes/admin/permisos.tsx` |
| `SheetNuevoRol` + provider | `frontend/src/components/admin/roles/SheetNuevoRol.tsx` |
| Cards en registry | `frontend/src/modules/identidad/admin.ts` |
| Tests Playwright | `frontend/tests/e2e/admin/roles-permisos.spec.ts` |

**Mergeable cuando:** asignar 5 permisos batch a un rol → 1 `AuditLogEntry`; asociar/desasociar grupo Entra ID; intentar eliminar rol asignado → 422 con mensaje claro; matriz colapsable funcional.

**Sizing:** M.

---

## UF-Admin-PR4 — Usuarios + asignación rol×empresa (M)

**Branch:** `admin/uf4-usuarios`
**Depende de:** UF-Admin-PR1, UF-Admin-PR3 (porque la UI de "asignar rol" linkea a roles), F-Admin-PR4.2

| Alcance | Archivos |
|---|---|
| Ruta `/admin/usuarios` (P3) | `frontend/src/routes/admin/usuarios/index.tsx` |
| Ruta `/admin/usuarios/$id` con 3 tabs (datos, roles por empresa, preferencias) | `frontend/src/routes/admin/usuarios/$id.tsx` |
| `RolesPorEmpresaInlineForm` (selector Empresa + Rol) | `frontend/src/components/admin/usuarios/RolesPorEmpresaInlineForm.tsx` |
| `SheetNuevoUsuario` con autocompletar Entra ID por email | `frontend/src/components/admin/usuarios/SheetNuevoUsuario.tsx` |
| Hook `useEntraIdUsuarioSearch(email)` | `frontend/src/lib/identidad/use-entra-id-usuario-search.ts` |
| Cards en registry | (extiende `frontend/src/modules/identidad/admin.ts`) |
| Tests Playwright | `frontend/tests/e2e/admin/usuarios-crud.spec.ts` |

**Mergeable cuando:** alta usuario por email + autocompletar Entra ID; asignar rol en 2 empresas; desactivar usuario OK; intentar eliminar último super-admin → 422.

**Sizing:** M.

---

## UF-Admin-PR4.5 — Datos Maestros UI (S)

**Branch:** `admin/uf4-5-datos-maestros`
**Depende de:** UF-Admin-PR1, F-Admin-PR4_5

| Alcance | Archivos |
|---|---|
| Ruta `/admin/datos-maestros/proveedores` (P3) | `frontend/src/routes/admin/datos-maestros/proveedores/index.tsx` |
| Ruta `/admin/datos-maestros/proveedores/$id` | `frontend/src/routes/admin/datos-maestros/proveedores/$id.tsx` |
| Idem artículos | `frontend/src/routes/admin/datos-maestros/articulos/*` |
| Filtros server-side: RFC, razón social, naturaleza, estatus | (en route + query) |
| Reusa forms de Proveedor/Articulo existentes (Compras) | `frontend/src/modules/compras/components/*` (sin cambios) |
| Cards en registry | `frontend/src/modules/datos-maestros/admin.ts` |
| Tests Playwright | `frontend/tests/e2e/admin/datos-maestros-filtros.spec.ts` |

**Mergeable cuando:** filtros aplican; lista paginada; click en fila abre detalle; forms existentes siguen funcionando.

**Sizing:** S.

---

## UF-Admin-PR5.1 — Catálogos: Monedas + Tipos de cambio (S)

**Branch:** `admin/uf5-1-monedas`
**Depende de:** UF-Admin-PR1, F-Admin-PR5.1

| Alcance | Archivos |
|---|---|
| Ruta `/admin/catalogos/monedas` (P3) | `frontend/src/routes/admin/catalogos/monedas/index.tsx` |
| `MonedaDetalle` con tab "Tipos de cambio" | `frontend/src/components/admin/catalogos/MonedaDetalle.tsx` |
| `TipoCambioInlineForm` (fecha + valor + origen Manual default) | `frontend/src/components/admin/catalogos/TipoCambioInlineForm.tsx` |
| `SheetNuevaMoneda` | `frontend/src/components/admin/catalogos/SheetNuevaMoneda.tsx` |
| Cards en registry | `frontend/src/modules/catalogos/admin.ts` |
| Tests Playwright | `frontend/tests/e2e/admin/monedas-tipos-cambio.spec.ts` |

**Mergeable cuando:** crear moneda; registrar tipo de cambio inline; ver historial paginado por fecha desc.

**Sizing:** S.

---

## UF-Admin-PR5.2 — Catálogos editables: CondicionesPago, Incoterm, Transportista, UnidadMedida (S)

**Branch:** `admin/uf5-2-catalogos-editables`
**Depende de:** UF-Admin-PR1, F-Admin-PR5.2

| Alcance | Archivos |
|---|---|
| Rutas `/admin/catalogos/{condiciones-pago, incoterms, transportistas, unidades-medida}` (P1) | `frontend/src/routes/admin/catalogos/<recurso>/index.tsx` |
| `Sheet<Recurso>Nuevo` para cada uno (4 componentes, factory genérica si simpligfica) | `frontend/src/components/admin/catalogos/*` |
| Edición inline desde fila | (en route) |
| Cards en registry | (extiende `frontend/src/modules/catalogos/admin.ts`) |
| Tests Playwright | `frontend/tests/e2e/admin/catalogos-editables.spec.ts` (uno por recurso o consolidado) |

**Mergeable cuando:** CRUD funciona para los 4; intentar desactivar referenciado → 422.

**Sizing:** S.

---

## UF-Admin-PR5.3 — Catálogos SAT read-only (XS)

**Branch:** `admin/uf5-3-catalogos-sat`
**Depende de:** UF-Admin-PR1, F-Admin-PR5.3

| Alcance | Archivos |
|---|---|
| Rutas read-only `/admin/catalogos/{formas-pago, usos-cfdi, regimenes-fiscales}` (P1) | `frontend/src/routes/admin/catalogos/<recurso>/index.tsx` |
| Sin botones de crear/editar/eliminar; banner explicativo "Mantenido vía migración SAT" | (en routes) |
| Cards en registry | (extiende `frontend/src/modules/catalogos/admin.ts`) |
| Tests Playwright | `frontend/tests/e2e/admin/catalogos-sat-readonly.spec.ts` |

**Mergeable cuando:** listas se renderizan; banner visible; sin acciones de mutación.

**Sizing:** XS.

---

## UF-Admin-PR6 — Series y folios (S)

**Branch:** `admin/uf6-series`
**Depende de:** UF-Admin-PR1, F-Admin-PR6.1

| Alcance | Archivos |
|---|---|
| Ruta `/admin/series` (P1 + Sheet) | `frontend/src/routes/admin/series/index.tsx` |
| `SheetNuevaSerie` con RadioGroup `Eterno|Anual|Mensual` + preview del próximo folio | `frontend/src/components/admin/series/SheetNuevaSerie.tsx` |
| Edición inline desde fila con confirm `<Dialog>` al cambiar `ReinicioPeriodo` | `frontend/src/components/admin/series/SerieFilaEditable.tsx` |
| Cards en registry | (extiende `frontend/src/modules/administracion/admin.ts`) |
| Tests Playwright | `frontend/tests/e2e/admin/series-folios.spec.ts` |

**Mergeable cuando:** crear serie en cada modo; preview del próximo folio correcto; cambiar `ReinicioPeriodo` muestra confirm.

**Sizing:** S.

---

## UF-Admin-PR7 — Auditoría + parámetros (S)

**Branch:** `admin/uf7-auditoria-parametros`
**Depende de:** UF-Admin-PR1, F-Admin-PR7.1, F-Admin-PR7.2

| Alcance | Archivos |
|---|---|
| Ruta `/admin/parametros` (form simple) | `frontend/src/routes/admin/parametros.tsx` |
| Ruta `/admin/auditoria` (P2 bandeja server-side) | `frontend/src/routes/admin/auditoria.tsx` |
| Filtros: rango fechas obligatorio, módulo, recurso, acción, usuario, empresa | (en route) |
| `AuditoriaDetalleDrawer` con `<Sheet>` + `react-diff-viewer` mostrando before/after JSON | `frontend/src/components/admin/auditoria/AuditoriaDetalleDrawer.tsx` |
| Atajo "Auditoría" en sub-topbar `/admin` (decisión §5.4 del diseño) | `frontend/src/routes/admin/index.tsx` (edit) |
| Instalar `react-diff-viewer` | `frontend/package.json` |
| Cards en registry | (extiende `frontend/src/modules/administracion/admin.ts`) |
| Tests Playwright | `frontend/tests/e2e/admin/auditoria-filtros.spec.ts`, `frontend/tests/e2e/admin/parametros-edicion.spec.ts` |

**Mergeable cuando:** filtros aplican; intentar consulta sin rango → mensaje claro; drawer muestra diff JSON; PATCH parámetro OK.

**Sizing:** S.

---

## Resumen tabular total

| PR | Sizing | Branch | Backend deps |
|---|---|---|---|
| UF-Admin-PR1 | M | `admin/uf1-andamio` | F-Admin-PR1.1, PR1.2 |
| UF-Admin-PR2 | M | `admin/uf2-empresas` | F-Admin-PR2.3 |
| UF-Admin-PR3 | M | `admin/uf3-roles` | F-Admin-PR3.2, PR3.3 |
| UF-Admin-PR4 | M | `admin/uf4-usuarios` | F-Admin-PR4.2 |
| UF-Admin-PR4.5 | S | `admin/uf4-5-datos-maestros` | F-Admin-PR4_5 |
| UF-Admin-PR5.1 | S | `admin/uf5-1-monedas` | F-Admin-PR5.1 |
| UF-Admin-PR5.2 | S | `admin/uf5-2-catalogos-editables` | F-Admin-PR5.2 |
| UF-Admin-PR5.3 | XS | `admin/uf5-3-catalogos-sat` | F-Admin-PR5.3 |
| UF-Admin-PR6 | S | `admin/uf6-series` | F-Admin-PR6.1 |
| UF-Admin-PR7 | S | `admin/uf7-auditoria-parametros` | F-Admin-PR7.1, PR7.2 |

**Total: 10 PRs frontend** (1 XS, 6 S, 4 M). Coherente con `feedback_pr_granularidad`.

---

## Paralelización

UF-Admin-PR1 es prerrequisito de todo. A partir de ahí, las UFs son **completamente paralelizables** si:

- Su backend correspondiente ya está mergeado.
- Cada una toca dominios independientes.

UF-Admin-PR4 depende de UF-Admin-PR3 solo por el linkeo "usuario asignado a rol" — si se quiere desacoplar, se puede mergear UF4 antes y dejar la card de "usuarios asignados" como tab vacío hasta UF3.

## Rev.

- **2026-05-13** — Rev. 1. Frontend PR breakdown inicial. Autor: Claude.
