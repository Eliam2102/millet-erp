## ADR-0034: Área de Administración y settings — modelo híbrido con registry de extensibilidad

- **Estado**: Aceptada
- **Fecha**: 2026-05-13
- **Decisores**: Eduardo Paredes (owner), Claude (backend/frontend)
- **Etiquetas**: administracion, identidad, catalogos, settings, rbac, shell, transversal

## Contexto y problema

El ERP tiene hoy:

- Módulo `Identidad` (`backend/src/Identidad/`) con Rol, Permiso, RolPermiso, UsuarioEmpresaRol, Usuario, PermisosCanonicos. **Sin UI** que permita administrarlos.
- Catálogos y entidades de organización en `SharedKernel/Domain/` (Empresa, Sucursal, Departamento, Moneda, RegimenFiscal, CondicionesPago, Incoterm, Transportista, Proveedor, Articulo, Almacen, UsoPrincipal, Naturaleza, TipoPersonaProveedor, EstatusCatalogo). Operados parcialmente vía endpoints `Catalogos*` y un setting de Compras (`ComprasSettings`, ADR-0033), pero sin un área de administración cohesiva.
- Topbar con icono de engrane (`Settings` de lucide) en estado `disabled` ([Topbar.tsx:182](../../frontend/src/components/layout/Topbar.tsx#L182)) esperando ser wireado.
- 9 módulos del back-office por construir (Facturación, CxC, CxP, Almacén-no-prod, Activos Fijos, Contabilidad, BI, A+W, además del propio Compras en expansión), todos requerirán settings, permisos canónicos y catálogos.

No hay un patrón definido para administrar:
- Usuarios, roles y permisos con UI (RBAC, ADR-0007).
- Datos organizacionales (Empresas, Sucursales, Departamentos).
- Catálogos globales y SAT (Monedas, Condiciones de pago, Régimenes fiscales, Usos CFDI, etc.).
- Settings específicos de cada módulo (como `ComprasSettings` de ADR-0033).
- Auditoría de cambios a la configuración (ADR-0008 aplica al núcleo, pero falta UI de visualización).

**Pregunta principal:** ¿Cómo organizamos el área de Administración para que sirva al MVP de Compras y se extienda naturalmente a los 9 módulos restantes sin acoplarlos a un módulo central?

## Drivers de la decisión

- **Frontera hexagonal por módulo.** Cada módulo es dueño de sus reglas (CLAUDE.md). Un módulo "Admin" central que conozca los settings de Compras, Facturación, CxP, etc., rompe esa frontera.
- **Extensibilidad cero-fricción.** Agregar un módulo nuevo no debe requerir tocar el shell, el módulo Admin ni el área de configuración global.
- **UX unificada.** El usuario espera un único punto de entrada (engrane del topbar) para todo lo que sea configuración, independientemente de qué módulo provea cada pieza.
- **Aprovechar lo existente.** `Identidad` (Rol, Permiso), `AppLauncherModal`/`AppLauncherCard` (cards agrupadas), `EmpresaSelector`, `PermisosCanonicos`, y el patrón de `ComprasSettings` (ADR-0033) son piezas a reusar, no reinventar.
- **Multi-empresa** (ADR-0011). Empresas, sucursales y series de folios son entidades multi-tenant por construcción.
- **Coherencia con los módulos exemplar.** Compras-RQ y Compras-OC siguen la estructura de 7 docs + ADRs. Admin debe seguir la misma plantilla para que el equipo (y futuras sesiones) operen en terreno conocido.

## Opciones consideradas

1. **Área de Administración 100% transversal.** Un solo módulo `Administracion` posee todos los settings del ERP (Compras settings, Facturación settings, CxP settings, etc.). UI única en `/admin/*`.
2. **Modelo híbrido con registry de extensibilidad** — *elegida*. Un área transversal `/admin` para lo verdaderamente cross-módulo (RBAC, empresas, catálogos globales) + cada módulo expone sus propios settings en `/<modulo>/settings`. El landing `/admin` consume un registry tipado (`AdminSection`) al que cada módulo se suscribe.
3. **Settings 100% distribuidos.** No hay área central; cada módulo administra todo lo suyo. El usuario navega a `/identidad/usuarios`, `/identidad/roles`, `/compras/configuracion`, etc.

## Decisión

**Modelo híbrido (Opción 2):**

### Backend — partición por ownership

| Ámbito | Módulo dueño | Schema PostgreSQL |
|---|---|---|
| Roles, permisos, asignaciones, usuarios | `Identidad` (existente) | `identidad` |
| Empresas, Sucursales, Departamentos, Series de folios, Parámetros globales, Bitácora de configuración | **`Administracion`** (nuevo) | `admin` |
| Monedas, Condiciones de pago, Formas de pago SAT, Usos CFDI, Régimenes fiscales, Incoterms, Transportistas, Unidades de medida, Tipos de cambio | **`Catalogos`** (nuevo) | `catalogos` |
| Proveedor, Artículo | **`DatosMaestros`** (nuevo, ver ADR-0035) | `datos_maestros` |
| Almacenes no-productivos | **`Almacen`** (nuevo MVP-light, ver ADR-0035 y A1=b del 00-levantamiento) | `almacen` |
| Settings y catálogos específicos de cada módulo de negocio | Cada módulo (Compras, Facturación, CxC, CxP, ...) | El schema del módulo |

> El schema `compartido` actual queda como nombre legacy de las migraciones existentes — Fase A del refactor (ver ADR-0035) **no renombra el schema**; solo reorganiza el código. El rename físico es opcional y queda diferido.

### Permisos canónicos extensibles

Se extiende [`PermisosCanonicos.cs`](../../backend/src/Identidad/Domain/PermisosCanonicos.cs) con la convención `<modulo>.<recurso>.<accion>`:

```
admin.empresas.leer / crear / editar
admin.empresas.sucursales.gestionar
admin.departamentos.gestionar
admin.series.gestionar
admin.parametros.editar
admin.auditoria.leer
identidad.usuarios.leer / crear / editar / desactivar
identidad.roles.leer / crear / editar / eliminar / asignar_permisos
identidad.permisos.leer
catalogos.<recurso>.gestionar       (uno por catálogo)
datos_maestros.proveedores.gestionar
datos_maestros.articulos.gestionar
<modulo>.settings.leer / editar      (uno por módulo de negocio)
```

Cada módulo nuevo añade sus permisos al fichero `PermisosCanonicos.cs`. La UI de Roles (en `/admin/roles`) los lee y los muestra agrupados por módulo automáticamente — sin código nuevo en el shell o el módulo Administración.

### Frontend — landing y registry

- **Ruta raíz:** `/admin` — landing que renderiza un grid de cards agrupado por sección (Identidad y acceso, Organización, Catálogos globales, Datos maestros, Configuración por módulo). Reusa los componentes `AppLauncherModal`/`AppLauncherCard` (ADR-0032).
- **Engrane del topbar:** [`Topbar.tsx:182`](../../frontend/src/components/layout/Topbar.tsx#L182) pasa de `disabled` a `<Link to="/admin">`. El icono se oculta si el usuario no tiene **ningún** permiso `admin.*`, `identidad.*`, `catalogos.*`, `datos_maestros.*` ni `<modulo>.settings.*`.
- **Sub-rutas transversales:**
  - `/admin/usuarios`, `/admin/roles`, `/admin/permisos` (UI sobre módulo `Identidad`).
  - `/admin/empresas`, `/admin/empresas/$id` (incluye tabs sucursales + series + datos fiscales).
  - `/admin/departamentos`.
  - `/admin/catalogos/<recurso>` (uno por catálogo: monedas, condiciones-pago, regimenes-fiscales, usos-cfdi, formas-pago, unidades-medida, incoterms, transportistas, tipos-cambio).
  - `/admin/datos-maestros/proveedores`, `/admin/datos-maestros/articulos`.
  - `/admin/series`, `/admin/parametros`, `/admin/auditoria`.
- **Settings por módulo:** `/<modulo>/settings` (ya existe el patrón para Compras vía ADR-0033). El landing `/admin` linkea a cada uno como card en la sección "Configuración por módulo".

### Registry `AdminSection` — contrato de extensibilidad

```typescript
// frontend/src/lib/admin/registry.ts
export interface AdminSection {
  id: string;
  modulo: 'admin' | 'identidad' | 'catalogos' | 'datos_maestros' | 'almacen'
        | 'compras' | 'facturacion' | 'cxc' | 'cxp' | 'activos'
        | 'contabilidad' | 'reportes' | 'aw';
  titulo: string;
  descripcion: string;
  icon: LucideIcon;
  href: string;
  permisoRequerido: string;     // canónico de PermisosCanonicos
  orden: number;
  grupo: 'identidad' | 'organizacion' | 'catalogos' | 'datos_maestros' | 'modulos';
  /**
   * Si "auto", `/admin/<modulo>/settings` renderiza un form a partir del
   * SettingsSchema que el módulo expone vía
   * `GET /api/v1/<modulo>/settings/schema` (ver §SettingsSchema más abajo).
   * Si "custom", el card linkea a `/<modulo>/settings` (ruta del módulo).
   * Aplica solo para entries del grupo "modulos"; en otros grupos se ignora.
   */
  displayMode?: 'auto' | 'custom';
}
```

Cada módulo registra sus cards en su barrel (`frontend/src/modules/<modulo>/admin.ts`). El landing los descubre vía import + filtro por permisos del usuario. **Agregar un módulo = agregar entries al registry. Cero cambios al shell.**

### Contrato `SettingsSchema` — auto-renderizado de settings simples (A7=b)

Decisión cerrada en A7=b del 00-levantamiento (2026-05-13). Cada módulo de negocio expone su schema declarativo de settings y el área Admin renderiza un formulario genérico para los items marcados como `auto`.

**Backend:** cada módulo implementa `ISettingsSchemaProvider` y publica:

```
GET  /api/v1/<modulo>/settings/schema
  → { items: SettingItem[] }

PATCH /api/v1/<modulo>/settings/{clave}
  → body: { valor: any }     // valida contra el schema; 422 si inválido
  → header Idempotency-Key requerido (ADR-0020)
```

```csharp
public sealed record SettingItem(
    string Clave,                       // "AutoGenerarOcAlAutorizar"
    string Etiqueta,                    // "Auto-generar OC al autorizar"
    string Descripcion,                 // "Cuando true, el handler crea OC borrador..."
    TipoSetting Tipo,                   // Bool | Int | Decimal | String | Enum | Fecha
    object? Default,
    object? Valor,                      // valor actual para la empresa activa
    ValidacionSetting? Validacion,      // min, max, pattern, opciones (enum)
    string PermisoLeer,                 // canónico, ej. "compras.configuracion.leer"
    string PermisoEditar,               // canónico, ej. "compras.configuracion.editar"
    DisplayMode Mostrar,                // Auto | Custom (este último excluye del form)
    string? RutaCustom,                 // si Custom, link a UI dedicada
    string? AlertaCambio                // texto que aparece en confirm modal si non-trivial
);
```

**Frontend:** `/admin/<modulo>/settings` consume el endpoint del schema y renderiza un form con shadcn/ui:

- `Tipo.Bool` → Switch.
- `Tipo.Int/Decimal` → Input numérico con validación.
- `Tipo.String` → Input.
- `Tipo.Enum` → Select con opciones del `Validacion.Opciones`.
- `Tipo.Fecha` → DatePicker.
- Items con `Mostrar = Custom` se omiten del form y se listan como link a `RutaCustom`.

`ComprasSettings` (ADR-0033) se expone vía este schema con `Mostrar = Custom` para `AutoGenerarOcAlAutorizar` (porque tiene efectos cross-cutting en el flujo RQ→OC). Mientras tanto, otros settings de Compras que sean booleanos simples pueden marcarse `Mostrar = Auto` y aparecer en el form genérico.

**Auditoría:** cada PATCH al setting genera un `AuditLogEntry` vía interceptor (ADR-0008). La UI de `/admin/auditoria` filtra por módulo y muestra antes/después.

### Patrones de UI

Se aplican los patrones cross-módulo ya documentados en [`frontend/docs/patrones-compras.md`](../../frontend/docs/patrones-compras.md):

- **Master-detail** (P3) para empresas, usuarios, roles, catálogos con tabla auxiliar.
- **Sheet (slide-from-right)** (P4) para "Nuevo usuario", "Nuevo rol", "Nueva empresa", "Nueva moneda".
- **Inline forms** (memoria `feedback_inline_no_modal_para_items`) para asignar permisos a un rol, sucursales a una empresa, tipos de cambio a una moneda. **Nunca modal** para items dentro del master.
- **Bandeja P1** simple para listados read-mostly (catálogos SAT estables).

## Consecuencias

**Positivas**

- Respeta frontera hexagonal: cada módulo decide qué configura él y qué delega al área transversal.
- Punto de entrada unificado para el usuario (engrane), sin acoplar módulos entre sí.
- Cero fricción para agregar módulos: el módulo nuevo registra sus cards y ya aparecen en `/admin` con permisos respetados.
- Reusa código existente (`Identidad`, `AppLauncherModal`, `EmpresaSelector`, `ComprasSettings` como exemplar).
- Permisos canónicos crecen orgánicamente; la UI de Roles los descubre.
- Permite tener empresas con configuraciones distintas (multi-tenant real) — coherente con ADR-0011.

**Negativas**

- Dos lugares para configurar (transversal vs módulo) — requiere disciplina y un criterio claro de "qué va dónde". Mitigado documentando en este ADR + en `01-diseno.md` del módulo.
- Schemas adicionales (`admin`, `catalogos`, `datos_maestros`) — más DbContexts. Pago aceptable dada la frontera; ya hay precedente con ADR-0030 (multi-DbContext por familia de schemas).
- Hay que reorganizar `SharedKernel/Domain/` que hoy concentra catálogos y entidades organizacionales. Cubierto por ADR-0035.

## Descartadas

**Opción 1 (todo transversal).** El módulo "Administracion" terminaría conociendo los settings internos de Compras, Facturación, CxC, etc. Cada vez que un módulo agrega un setting nuevo, Administracion necesita un cambio. Esto invierte el ownership de configuración y rompe hexagonal. Además, settings de dominio (umbrales de autorización de OC, política de antigüedad de CxC, plantillas CFDI) son lógica del módulo dueño, no infraestructura compartida.

**Opción 3 (todo distribuido).** Catálogos globales (monedas, formas de pago SAT, usos CFDI) y empresas/sucursales son **inherentemente** cross-módulo: Facturación los necesita para emitir CFDI, CxC para aplicar pagos, CxP para programar pagos, Compras para precios y proveedores. Distribuirlos a un único "módulo dueño" arbitrario crea acoplamiento invertido. Empresas tampoco encajan en un módulo de negocio — son tenants, no entidades de un dominio. Y RBAC necesita una UI única (no hay rol "solo de Compras"; los roles cruzan módulos).

## Notas de implementación

Despliegue documentado en `docs/modulos/administracion/`:

| Doc | Contenido |
|---|---|
| `00-levantamiento-estado-actual.md` | Inventario del repo: qué piezas existen, qué se reusa, qué se construye nuevo. |
| `01-diseno.md` | Modelo de dominio del módulo Administración + registry + permisos + sección "Dependencias de plataforma pendientes" (ADR-0031). |
| `02-plan-implementacion.md` | Fases F-Admin-PR0..PR7 + sizing + riesgos. |
| `03-pr-breakdown.md` | Detalle backend por PR. |
| `04-cuidados-infra.md` | Schemas nuevos, migraciones, healthchecks, deploy YAML (memoria `feedback_dbcontext_nuevo_checklist`). |
| `05-frontend-diseno.md` | `AdminSection` registry, landing `/admin`, gear wireup, patrones master-detail. |
| `06-frontend-plan-implementacion.md` | Fases UI. |
| `07-frontend-pr-breakdown.md` | Detalle frontend por PR. |
| `08-operacion-y-runbook.md` (post-go-live) | Seed inicial de roles, bootstrap del primer super-admin, mapeo Entra ID → roles, recuperación. |
| `09-go-live-checklist.md` (post-go-live) | Migraciones aplicadas, healthchecks, permisos seedeados, E2E del super-admin, plan de rollback. |

### PR breakdown (resumen)

| PR | Branch | Scope |
|---|---|---|
| **PR doc-only #1** | `admin/docs-adr-0034-0035` | Este ADR + ADR-0035 + 00-levantamiento + 01-diseño + actualización a `docs/modulos/README.md`. |
| **PR doc-only #2** | `admin/docs-plan-pr-breakdown` | 02-plan + 03-pr-breakdown + 04-cuidados-infra + 05-frontend-diseno + 06-frontend-plan + 07-frontend-pr-breakdown. |
| **PR doc-only #3** | `admin/docs-runbook-golive` | 08-operacion-y-runbook + 09-go-live-checklist. |
| **F-Admin-PR0** | `admin/sk-refactor-pr0` | Code-only refactor: mover entidades de `SharedKernel/Domain/` a `Administracion`/`Catalogos`/`DatosMaestros` (ADR-0035). |
| **F-Admin-PR1** | `admin/andamio-pr1` | Andamio: schemas admin/catalogos creados, registry `AdminSection`, landing `/admin`, gear del topbar wireado, permisos `admin.*` canónicos. |
| **F-Admin-PR2** | `admin/empresas-pr2` | Empresas + Sucursales (UI master-detail). |
| **F-Admin-PR3** | `admin/roles-pr3` | Roles + matriz de permisos (UI sobre Identidad). |
| **F-Admin-PR4** | `admin/usuarios-pr4` | Usuarios + asignación rol×empresa + mapeo Entra ID. |
| **F-Admin-PR5** | `admin/catalogos-sat-pr5` | Catálogos SAT (monedas, condiciones, formas, usos CFDI, regímenes). |
| **F-Admin-PR6** | `admin/series-folios-pr6` | Series y folios. |
| **F-Admin-PR7** | `admin/auditoria-pr7` | Auditoría + parámetros globales. |

### Auto-mode

Todos los branches `admin/*` están bajo auto-mode N2 con gate de CI verde (ver `.claude/hooks/validate-auto-merge.ps1` y memoria `feedback_no_commits`).

### Pendientes derivados

- ADR-0035: re-localización de entidades de `SharedKernel/Domain/` a módulos dueños (Administracion, Catalogos, DatosMaestros).
- Diseño detallado del módulo Administración en `docs/modulos/administracion/01-diseno.md`.
- Auto-renderizado de settings simples genéricos (key/value/tipo) **cerrado por A7=b**: contrato `SettingsSchema` documentado arriba se implementa desde F-Admin-PR1 (andamio + contrato vacío) y cada módulo expone su schema (Compras como exemplar, ADR-0033). UI custom sigue soportada vía `Mostrar = Custom`.
