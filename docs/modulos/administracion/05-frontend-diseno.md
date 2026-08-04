# Diseño de frontend — Módulo Administración

> **Construido sobre:** [01-diseno.md](01-diseno.md) (Rev. 2), [02-plan-implementacion.md](02-plan-implementacion.md), [03-pr-breakdown.md](03-pr-breakdown.md), [04-cuidados-infra.md](04-cuidados-infra.md).
>
> **Hereda contexto de:** [`frontend/docs/patrones-compras.md`](../../../frontend/docs/patrones-compras.md) — el exemplar de patrones de UI del back-office. Los patrones P1/P2/P3/P4 + inline forms son los que Admin replica.
>
> **Construido contra:** la API real implementada en F-Admin-PR1..PR7 cuando los PRs estén mergeados.
>
> **Estado:** propuesta de diseño UI v1 para revisión con el owner. Las decisiones marcadas como `[Asunción FADM-xx]` requieren confirmación.
>
> **Fecha:** 2026-05-13.

---

## 0. Cómo leer

- `[Decidido]` — fijado por ADR existente, decisión del backend ya implementada, o por `frontend/docs/patrones-compras.md`.
- `[Asunción FADM-xx]` — propuesta del frontend tech lead, razonable pero pendiente. Listadas en §3.
- `[Diferido]` — fuera de alcance v1.

> **Reuso primero (regla del proyecto):** donde un patrón ya está validado en Compras, este doc apunta a `§X del patrones-compras.md` en lugar de duplicar. Solo se expande lo específico de Admin.

---

## 1. Posicionamiento en el shell

### 1.1 Qué cubre este documento

UI del área `/admin/*` y los settings auto-renderizados de cada módulo (`/admin/<modulo>/settings`). Cubre los entry points del shell (engrane del topbar) y la landing.

### 1.2 Qué NO cubre

- Pantallas operativas de cada módulo de negocio (Compras, Facturación, CxC, CxP, ...) — viven en sus propios docs.
- UI custom de settings por módulo (ej. `/compras/configuracion` ya implementado vía ADR-0033) — viven en el módulo correspondiente.
- Pantallas de Almacén-no-prod completo — el módulo `Almacen` MVP-light solo tiene UI básica de gestión de almacenes; inventario y movimientos quedan diferidos.
- 08-runbook y 09-go-live-checklist — viven en PR doc-only #3.

### 1.3 Entry point único: engrane del topbar

El icono `Settings` de [Topbar.tsx:182](../../../frontend/src/components/layout/Topbar.tsx#L182) pasa de `disabled` a un `Link to="/admin"` activo. **Visible** solo si el usuario tiene **al menos un** permiso que aparezca en el registry filtrado:

```tsx
const usuarioPuedeVerAdmin = useAdminAccess();

{usuarioPuedeVerAdmin && (
  <Button variant="ghost" size="icon" asChild aria-label="Configuración">
    <Link to="/admin"><Settings className="h-4 w-4" /></Link>
  </Button>
)}
```

`useAdminAccess()` retorna `true` si **alguna** card del `adminRegistry` pasa el filtro de permisos.

---

## 2. Registry `AdminSection` (contrato de extensibilidad)

### 2.1 Tipo

```typescript
// frontend/src/lib/admin/registry.ts

export type AdminGrupo = 'identidad' | 'organizacion' | 'catalogos' | 'datos_maestros' | 'modulos';

export type AdminModulo = 'admin' | 'identidad' | 'catalogos' | 'datos_maestros' | 'almacen'
                        | 'compras' | 'facturacion' | 'cxc' | 'cxp'
                        | 'activos' | 'contabilidad' | 'reportes' | 'aw';

export interface AdminSection {
  id: string;                          // 'admin-empresas', 'identidad-usuarios', etc.
  modulo: AdminModulo;
  titulo: string;
  descripcion: string;
  icon: LucideIcon;
  href: string;                        // ruta destino del card
  permisoRequerido: string;            // canónico de PermisosCanonicos
  orden: number;                       // orden dentro del grupo
  grupo: AdminGrupo;
  // Solo aplica para grupo='modulos'.
  //   'auto'   → href apunta a /admin/<modulo>/settings (form auto-renderizado).
  //   'custom' → href apunta a la UI custom del módulo (ej. /compras/configuracion).
  displayMode?: 'auto' | 'custom';
}
```

### 2.2 Composición del registry

```typescript
// frontend/src/lib/admin/registry.ts

import { identidadAdminCards } from '@/modules/identidad/admin';
import { administracionAdminCards } from '@/modules/administracion/admin';
import { catalogosAdminCards } from '@/modules/catalogos/admin';
import { datosMaestrosAdminCards } from '@/modules/datos-maestros/admin';
import { almacenAdminCards } from '@/modules/almacen/admin';
import { comprasAdminCards } from '@/modules/compras/admin';

export const adminRegistry: AdminSection[] = [
  ...identidadAdminCards,
  ...administracionAdminCards,
  ...catalogosAdminCards,
  ...datosMaestrosAdminCards,
  ...almacenAdminCards,
  ...comprasAdminCards,
  // facturacion, cxc, cxp, activos, contabilidad, reportes, aw — cada módulo agrega aquí
];
```

Cada módulo expone su array `<modulo>AdminCards: AdminSection[]` en `frontend/src/modules/<modulo>/admin.ts`. **Agregar un módulo nuevo al ERP = agregar entries al registry. Cero cambios al shell.**

### 2.3 Filtrado por permisos

```typescript
// frontend/src/lib/admin/use-admin-registry.ts
export function useAdminRegistry() {
  const { permisos } = useAuth();
  return useMemo(
    () => adminRegistry.filter(s => permisos.includes(s.permisoRequerido)),
    [permisos]
  );
}

export function useAdminAccess(): boolean {
  return useAdminRegistry().length > 0;
}
```

---

## 3. Decisiones pendientes / Asunciones FADM

- **[FADM-01]** Distribución visual de los grupos en `/admin`: header de sección + grid de cards. ¿Sticky headers durante scroll? Asunción: sí. **Recomendación**: implementar sin sticky en v1; agregar si UAT lo pide.
- **[FADM-02]** ¿El landing `/admin` permite buscar entre cards? Asunción: no en v1 (lista corta). Si pasa de 15 cards, agregar input de filtro client-side.
- **[FADM-03]** En la matriz de permisos de `/admin/roles/$id`, ¿se agrupan visualmente por módulo? Asunción: sí, con secciones colapsables tipo `<Collapsible>` de shadcn.
- **[FADM-04]** ¿El form auto-renderizado de `/admin/<modulo>/settings` muestra los items en orden alfabético o por `orden` declarado en el schema? Asunción: por `orden` declarado (defecto numérico).
- **[FADM-05]** Para `Mostrar = Custom`, ¿el card del módulo en `/admin` linkea **directamente** a `RutaCustom`, o pasa por una página intermedia `/admin/<modulo>/settings` que muestra "Esta configuración tiene UI dedicada, abrir →"? Asunción: link directo (cero fricción).
- **[FADM-06]** ¿Bandeja de auditoría `/admin/auditoria` permite exportar a CSV? Asunción: no en MVP.

---

## 4. Stack y patrones

### 4.1 Stack heredado

Heredado de ADR-0023 y `frontend/docs/patrones-compras.md`:

- React 19 + TanStack Router + TanStack Query + Zustand + react-hook-form + Zod.
- shadcn/ui + Tailwind v4.
- OpenAPI → tipos TypeScript autogenerados (ADR-0017).

### 4.2 Patrones aplicados

| Pantalla | Patrón |
|---|---|
| Landing `/admin` | Grid de cards agrupado (basado en `AppLauncherModal` ADR-0032) |
| `/admin/usuarios`, `/admin/roles`, `/admin/empresas`, `/admin/datos-maestros/*` | **P3** master-detail (lista 320px sticky + panel detalle) |
| `/admin/empresas/$id` | P3 con tabs (datos, sucursales, departamentos, series, settings link) |
| `/admin/catalogos/<recurso>` editables (CondicionesPago, Incoterm, Transportista, UnidadMedida) | **P1** bandeja + **P4** Sheet para "Nuevo" |
| `/admin/catalogos/<recurso>` SAT read-only (FormaPago, UsoCfdi, RegimenFiscal) | **P1** bandeja read-only |
| `/admin/catalogos/monedas` | **P3** master-detail con tipos de cambio inline |
| `/admin/series` | P1 + Sheet |
| `/admin/parametros` | Formulario simple |
| `/admin/auditoria` | **P2** bandeja con filtros server-side |
| "Nuevo X" en cualquier pantalla | **P4** Sheet (slide-from-right) |
| Items dentro de master (permisos a rol, sucursales a empresa, tipos de cambio a moneda, roles a usuario) | **Inline forms** (memoria `feedback_inline_no_modal_para_items`) — **nunca modal** |
| `/admin/<modulo>/settings` (auto-renderizado) | Form genérico desde schema |

### 4.3 Componentes shadcn/ui reutilizados

- `Card`, `CardHeader`, `CardTitle`, `CardDescription`, `CardContent` para cards del landing.
- `Table`, `TableHeader`, `TableBody`, `TableRow`, `TableCell` para bandejas.
- `Sheet`, `SheetTrigger`, `SheetContent` para slide-from-right.
- `Tabs`, `TabsList`, `TabsTrigger`, `TabsContent` para tabs en detalle.
- `Switch`, `Input`, `Select`, `DatePicker`, `Textarea` para form fields.
- `Button`, `Badge`, `Avatar`, `Tooltip`, `Dialog` (para confirms críticos).
- `Form` de react-hook-form + `FormField`, `FormControl`, `FormDescription`, `FormMessage`.
- `Collapsible` para agrupación de permisos en matriz.

---

## 5. Landing `/admin`

### 5.1 Layout

```
┌─ Topbar (engrane visible) ──────────────────────────────────┐
│ ┌─ Sub-topbar /admin ───────────────────────────────────┐  │
│ │  Configuración                            [Auditoría] │  │
│ └────────────────────────────────────────────────────────┘  │
│                                                              │
│  ┌─ Identidad y acceso ─────────────────────────────────┐  │
│  │  ┌─Usuarios────┐  ┌─Roles y permisos─┐               │  │
│  │  │ icono       │  │ icono             │               │  │
│  │  │ Gestionar..│  │ Configurar..      │               │  │
│  │  └─────────────┘  └───────────────────┘               │  │
│  └────────────────────────────────────────────────────────┘  │
│  ┌─ Organización ───────────────────────────────────────┐  │
│  │  ┌─Empresas──┐  ┌─Departamentos─┐  ┌─Series──┐       │  │
│  └────────────────────────────────────────────────────────┘  │
│  ┌─ Catálogos globales ────────────────────────────────┐  │
│  │  ┌─Monedas─┐  ┌─Cond. pago─┐  ┌─SAT─┐ ...           │  │
│  └────────────────────────────────────────────────────────┘  │
│  ┌─ Datos maestros ────────────────────────────────────┐  │
│  │  ┌─Proveedores─┐  ┌─Artículos─┐                      │  │
│  └────────────────────────────────────────────────────────┘  │
│  ┌─ Configuración por módulo ──────────────────────────┐  │
│  │  ┌─Compras (custom)─┐  ┌─Almacén─┐                  │  │
│  └────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

### 5.2 Comportamiento

- El `useAdminRegistry()` filtra por permisos.
- Si un grupo queda vacío, se oculta el header del grupo.
- Si el registry está completamente vacío (caso límite con permisos cero), el engrane no debe ser visible — pero ya se filtra antes (§1.3).
- Click en card → `navigate(card.href)`.

### 5.3 Mobile

- Mismo grid, single-column. Cards full-width.
- Header sticky con back chevron + título "Configuración".

### 5.4 Sub-topbar

Inspirada en la sub-topbar de Compras OC. Contiene atajos contextuales (ej. "Auditoría" siempre visible si el usuario tiene `admin.auditoria.leer`). En MVP de Admin: solo atajo a `/admin/auditoria`.

---

## 6. Pantallas master-detail (P3) — Usuarios, Roles, Empresas, Datos Maestros

### 6.1 Layout estándar

```
/admin/usuarios
┌─ Sub-topbar ────────────────────────────────────────────┐
│ Usuarios            [+ Nuevo]   [Filtros▾]              │
├─ 320px sticky ──┬──────────────────────────────────────┤
│ ┌─ Búsqueda  ─┐ │  Detalle del seleccionado            │
│ │ Buscar...   │ │  (con tabs)                          │
│ └─────────────┘ │                                       │
│ • Juan Pérez ✓  │  [Datos] [Roles] [Preferencias]      │
│   juan@..       │                                       │
│ • María López   │  Datos generales:                    │
│   maria@..      │  Email: juan@millet.mx               │
│ • Eduardo Par.. │  Departamento: Compras               │
│ ...             │  Estatus: Activo  [Desactivar]       │
└─────────────────┴───────────────────────────────────────┘
```

### 6.2 Inline forms (memoria `feedback_inline_no_modal_para_items`)

Dentro del detalle, los **items hijos** del master se editan via inline forms (no modal):

- En `/admin/empresas/$id` → tab "Sucursales": inline form con border dashed para "agregar sucursal" y border amber para "editar sucursal seleccionada".
- En `/admin/roles/$id` → tab "Permisos": **matriz colapsable por módulo** con checkboxes; tab "Grupos Entra ID": inline form para asociar/desasociar.
- En `/admin/usuarios/$id` → tab "Roles": inline form con selector Empresa + Rol.
- En `/admin/catalogos/monedas/$id` → tab "Tipos de cambio": inline form con fecha + valor.

### 6.3 Sheet "Nuevo X" (P4)

Para alta inicial del aggregate raíz, `<Sheet side="right">` con form de creación. Confirm al cerrar si `isDirty`. `Force: true` al success para invalidar queries.

Provider a nivel del shell: `useNuevaEmpresa().abrir()`, `useNuevoRol().abrir()`, `useNuevoUsuario().abrir()`. Patrón heredado de Compras OC.

---

## 7. Auto-render de settings (A7=b)

### 7.1 Ruta `/admin/<modulo>/settings`

Para cards del grupo `modulos` con `displayMode = 'auto'`:

```
/admin/compras/settings   (si Compras decidiera tener algunos auto + el custom)
/admin/facturacion/settings
/admin/cxc/settings
...
```

### 7.2 Componente `<AutoSettingsForm modulo="X">`

```tsx
function AutoSettingsForm({ modulo }: { modulo: AdminModulo }) {
  const { data: schema } = useSettingsSchema(modulo);
  const form = useForm({ resolver: zodResolver(zodSchemaFromSettings(schema)) });

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(onSubmit)}>
        {schema.items.filter(i => i.mostrar === 'Auto').map(item => (
          <FormField key={item.clave} name={item.clave}>
            {/* Switch | Input | Select | DatePicker según item.tipo */}
          </FormField>
        ))}
        {schema.items.filter(i => i.mostrar === 'Custom').map(item => (
          <Card key={item.clave}>
            <CardHeader>
              <CardTitle>{item.etiqueta}</CardTitle>
              <CardDescription>{item.descripcion}</CardDescription>
            </CardHeader>
            <CardContent>
              <Button asChild>
                <Link to={item.rutaCustom!}>Abrir configuración</Link>
              </Button>
            </CardContent>
          </Card>
        ))}
        <Button type="submit">Guardar cambios</Button>
      </form>
    </Form>
  );
}
```

### 7.3 Mapeo `TipoSetting` → componente

| Tipo | Componente |
|---|---|
| `Bool` | `<Switch>` |
| `Int`, `Decimal` | `<Input type="number">` con `step` y `min`/`max` desde `Validacion` |
| `String` | `<Input type="text">` con `pattern` desde `Validacion` |
| `Enum` | `<Select>` con `Validacion.Opciones` |
| `Fecha` | `<DatePicker>` |

### 7.4 Validación frontend

Zod schema generado dinámicamente desde el `SettingsSchema`:

```typescript
function zodSchemaFromSettings(schema: SettingsSchema): ZodSchema {
  const shape: Record<string, ZodType> = {};
  for (const item of schema.items.filter(i => i.mostrar === 'Auto')) {
    shape[item.clave] = zodTypeFromTipo(item.tipo, item.validacion);
  }
  return z.object(shape);
}
```

### 7.5 Alertas de cambio non-trivial

Si `item.alertaCambio` está presente, antes de guardar muestra `<Dialog>` confirmando "Este cambio afectará... ¿Continuar?".

---

## 8. Bandeja de auditoría `/admin/auditoria` (P2)

### 8.1 Layout

```
┌─ Sub-topbar ────────────────────────────────────────────────┐
│ Bitácora                                                     │
├─ Filtros (sticky) ──────────────────────────────────────────┤
│ Desde: [01/05/2026]  Hasta: [13/05/2026] *obligatorio       │
│ Módulo: [Todos ▾]   Recurso: [Todos ▾]   Acción: [Todas ▾] │
│ Usuario: [Buscar...]   Empresa: [Todas ▾]                   │
│ [Aplicar] [Limpiar]                                          │
├─ Tabla (server-side paginated) ─────────────────────────────┤
│ Fecha          Usuario      Módulo    Recurso   Acción      │
│ 13/05 14:23    Eduardo P.   Admin     Empresa   Actualizar  │
│ 13/05 14:20    Eduardo P.   Compras   Settings  PATCH       │
│ 13/05 14:15    María L.     Identidad Rol       AsignarPerm │
│ ...                                                          │
│ ‹ 1 2 3 ... 47 ›                                            │
└──────────────────────────────────────────────────────────────┘
```

### 8.2 Click en fila → drawer con detalle JSON

`<Sheet>` que muestra `Detalle.before` y `Detalle.after` en JSON diff visual (lib `react-diff-viewer`).

### 8.3 Restricciones

- **Rango de fechas obligatorio** (P0 backend §4.4). Frontend valida antes de submit.
- **Rango máximo 90 días.** Frontend muestra alerta si excede.
- Sin export CSV en MVP (FADM-06).

---

## 9. Mapeo grupos Entra ID en `/admin/roles/$id`

### 9.1 Tab "Grupos Entra ID"

```
┌─ Tab Grupos Entra ID ───────────────────────────────────────┐
│ Asociar grupos de Entra ID a este rol. Los miembros del      │
│ grupo heredarán el rol en su próxima sesión.                 │
│                                                              │
│ ┌─ Grupos asociados ─────────────────────────────────────┐  │
│ │ ┌─ Buscador inline ───────────────────────────────┐ ✕ │  │
│ │ │ [Buscar grupo en Entra ID...]                   │   │  │
│ │ └─────────────────────────────────────────────────┘   │  │
│ │ • Compras_Jefes (a1b2c3...)              [Desasociar] │  │
│ │ • Administradores_TI (d4e5f6...)         [Desasociar] │  │
│ └─────────────────────────────────────────────────────────┘  │
│                                                              │
│ ┌─ Agregar grupo (inline form, border dashed) ──────────┐  │
│ │ Object ID: [b2c3d4...]   o buscar: [Compras_]         │  │
│ │ [+ Agregar]                                           │  │
│ └─────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

### 9.2 Consulta al Microsoft Graph

Para autocompletar el grupo por nombre, el frontend llama un endpoint backend que delega al Graph (`/api/v1/identidad/graph/grupos?q=Compras_`). Este endpoint usa el token de servicio del backend; no expone el token Graph al frontend.

### 9.3 Sincronización al asociar

Asociar grupo a rol **no** sincroniza usuarios inmediatamente. La sincronización ocurre cuando un usuario miembro del grupo entra (login orchestrator lee sus grupos y aplica `UsuarioEmpresaRol` automáticamente).

> Sync automatizado vía hosted service queda como `<EntraIdMapping>` deuda (A3=a + §12 del 01-diseño).

---

## 10. Series y folios `/admin/series`

### 10.1 P1 bandeja

```
┌─ Sub-topbar ────────────────────────────────────────────────┐
│ Series y folios                              [+ Nueva serie] │
├─────────────────────────────────────────────────────────────┤
│ Empresa          Tipo Doc   Prefijo  Reinicio   Próximo     │
│ Millet CDMX      OC         OC       Anual      OC-2026-...  │
│ Millet CDMX      OC         OCI      Eterno     OCI-00012    │
│ Millet MTY       OC         OC       Mensual    OC-2026-05-..│
│ ...                                                          │
└──────────────────────────────────────────────────────────────┘
```

### 10.2 Sheet "Nueva serie"

```tsx
<Sheet>
  <SheetContent side="right">
    <Form>
      <Empresa>      // Select (empresa activa por default)
      <Sucursal?>    // Select (opcional)
      <TipoDocumento> // Select enum
      <Prefijo>      // Input
      <Sufijo?>      // Input
      <ReinicioPeriodo> // RadioGroup: Eterno | Anual | Mensual
      <Preview>      // "Próximo folio: OC-2026-0001"
      <Button>Crear</Button>
    </Form>
  </SheetContent>
</Sheet>
```

### 10.3 Edición inline desde la bandeja

Click en fila → drawer con form simple (`/admin/series/$id` opcional). Cambios al `ReinicioPeriodo` requieren confirm (`Dialog`) — "Este cambio aplica solo a folios futuros; los actuales mantienen su correlativo".

---

## 11. Mobile

Patrones P1/P2/P3 ya soportan mobile (drill-down en P3, full-width en P1/P2). Sin trabajo adicional específico de Admin.

---

## 12. Accesibilidad

Heredado del exemplar Compras:

- WCAG AA. Lighthouse accessibility ≥ 95.
- Tabbing lógico en forms.
- `aria-label` en iconos sin texto.
- Anuncios live regions para acciones (toast → `aria-live="polite"`).

---

## 13. Internacionalización

Idioma del sistema: **español**. Sin i18n en MVP. Strings hardcoded en componentes; refactorizar cuando se priorice (deuda futura, no Admin-específica).

---

## 14. Performance

### 14.1 Bandeja de auditoría

- Paginación server-side obligatoria.
- Default page size: 50.
- Skeleton loader durante fetch.

### 14.2 Landing `/admin`

- Estático: una sola request para cargar los iconos (los cards no requieren fetch — el registry es client-side).
- Visible en < 200ms post-mount.

### 14.3 Permisos cache

- `useAuth()` ya cachea permisos del usuario en Zustand. Sin fetches adicionales.
- Cuando llega `RolPermisosActualizadosEvent` via SignalR (Hub futuro), invalida cache. Hoy: refresh on next nav.

---

## 15. Brechas con backend

| Brecha | Impacto | Plan |
|---|---|---|
| Endpoint `/api/v1/identidad/graph/grupos?q=` para autocompletar Entra ID groups | Mediano | F-Admin-PR3.2 expone endpoint; UI lo consume en F-Admin-PR3-frontend. |
| Endpoint `/api/v1/admin/series/{id}/preview-folio` para preview en el form | Bajo | Lo provee F-Admin-PR6.1 (computed en `ObtenerSerieQuery`). |
| Endpoint que retorna **todos** los permisos canónicos agrupados por módulo (para la matriz) | Alto (sin esto, no se renderiza la matriz) | F-Admin-PR3.2 expone `GET /api/v1/identidad/permisos?agrupado=true`. |
| Endpoint para ejecutar dry-run de migración Compras (PR6.2) | Bajo | Opcional, evaluar si UAT lo pide. |

---

## 16. Dependencias de plataforma pendientes

Heredadas del `01-diseno.md §12`:

- `<EntraIdMapping>` — frontend MVP solo soporta asociación manual de grupos a roles. Sync automatizado por hosted service queda diferido.
- `<TipoCambioSync>` — frontend MVP solo soporta carga manual de tipos de cambio. UI no anticipa el feed automatizado (cuando llegue, será un toggle "Origen: Manual/DOF/Banxico" en la fila — diseño futuro).
- `<AuditUI>` — cerrado por F-Admin-PR7.2.
- `<SettingsAutoRender>` — cerrado por F-Admin-PR1.1 (backend) + F-Admin-PR1-frontend (UI del `<AutoSettingsForm>`).

## 17. Rev.

- **2026-05-13** — Rev. 1. Diseño frontend inicial. Autor: Claude.
