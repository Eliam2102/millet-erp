# Levantamiento — Estado actual del repo (Módulo Administración)

> **Proyecto:** ERP Millet — Back-office
> **Módulo:** Administración (transversal, incluye Identidad y Accesos, Catálogos globales y Datos Maestros)
> **Versión:** 0.1 — Borrador para revisión
> **Fecha:** 2026-05-13
>
> **Origen:** este levantamiento **no** es ingeniería inversa de un sistema legacy. Es un inventario del propio repo Millet ERP: qué piezas relevantes a la administración del ERP ya existen, qué se reusa, qué falta. El objetivo es alimentar `01-diseno.md` sin proponer nada que dupliquen piezas ya presentes (memoria `feedback_reutilizacion_codigo`).
>
> **Estado:** sujeto a revisión por el owner. Las decisiones marcadas como `[Asunción]` requieren validación; están listadas en §10.

---

## 1. Propósito del módulo

El módulo Administración (Admin a partir de aquí) es el **área transversal** del ERP que centraliza las funciones de configuración y datos cross-módulo:

- **Identidad y accesos:** usuarios, roles, permisos, asignaciones.
- **Organización:** empresas (multi-tenant ADR-0011), sucursales, departamentos.
- **Catálogos globales y SAT:** monedas, condiciones de pago, formas de pago, usos CFDI, régimenes fiscales, incoterms, transportistas, unidades de medida.
- **Datos maestros operativos:** proveedores y artículos (entidades de negocio con ciclo de vida propio).
- **Series y folios:** secuencias por tipo de documento y empresa.
- **Parámetros globales:** zona horaria, formato de fecha, redondeos, política institucional.
- **Auditoría:** bitácora de cambios a configuración crítica (UI sobre el framework ADR-0008).

Cada módulo de negocio conserva la propiedad de sus **propios** settings (Compras tiene `ComprasSettings` por ADR-0033; Facturación tendrá `FacturacionSettings`, etc.). El módulo Admin no los administra — solo los **enlaza** desde el landing `/admin`.

> Decisión de ownership y división detallada: ADR-0034.

## 2. Lo que ya existe en el repo

### 2.1 Backend — módulo `Identidad`

Carpeta: [`backend/src/Identidad/`](../../../backend/src/Identidad/)

**Dominio (`Domain/`):**

| Pieza | Propósito |
|---|---|
| `Usuario.cs` | Aggregate root de usuario. Tiene `UsuarioEmpresaRol` (asignaciones por empresa), `UsuarioPreferencia`, `DepartamentoId`. |
| `Rol.cs`, `RolPermiso.cs` | Aggregate root rol, con permisos asignados. |
| `Permiso.cs`, `PermisosCanonicos.cs` | Catálogo de permisos. `PermisosCanonicos` es el seed-source: lista los permisos conocidos del sistema. |
| `RestriccionRol.cs` | Restricciones adicionales por rol (ámbito, scope). |
| `UsuarioEmpresaRol.cs` | Asignación N:M usuario × empresa × rol. |
| `UsuarioPreferencia.cs` | Preferencias por usuario (idioma, layout). |

**Infraestructura (`Infrastructure/`):**

- `IdentidadDbContext.cs` — schema `identidad`.
- Migraciones (las últimas relevantes):
  - `20260513034713_ComprasConfiguracionPermisos` (permisos `compras.configuracion.*`).
  - `20260511172001_OrdenesCompraPermisos` (permisos OC).
  - `20260509024124_UsuarioDepartamentoId` (FK a `Departamento`).
  - `20260508234457_AprobadoresYCatalogosPermisos`.
  - `20260508182706_CompartidoCatalogosPermiso`.
- `BootstrapSuperAdminHostedService.cs` — seedea el primer super-admin si no existe.

**Estado funcional:**

- ✅ Dominio completo.
- ✅ Permisos canónicos cubren Compras (RQ + OC).
- ❌ **Sin UI de administración** (no hay `/identidad/usuarios`, `/identidad/roles`).
- ❌ Sin endpoint REST de gestión de usuarios/roles (los actuales solo soportan flujos de Compras, no CRUD admin).

### 2.2 Backend — entidades organizacionales y catálogos en `SharedKernel`

Carpeta: [`backend/src/SharedKernel/Domain/`](../../../backend/src/SharedKernel/Domain/) — **mezcla** infraestructura transversal con entidades de negocio.

**Entidades de organización** (deberían vivir en módulo Administración):

| Pieza | Schema actual | Naturaleza | Re-localización (ADR-0035) |
|---|---|---|---|
| `Empresa.cs` | `compartido.empresas` | Razón social multi-tenant (ADR-0011) | → `Administracion/Domain/` |
| `Sucursal.cs` | `compartido.sucursales` | Unidad geográfica/operativa | → `Administracion/Domain/` |
| `Departamento.cs` | `compartido.departamentos` | Unidad organizacional, referenciada por Usuario | → `Administracion/Domain/` |
| `Almacen.cs` | `compartido.almacenes` | Almacenes no-productivos | → `Almacen/Domain/` — módulo `Almacen` (MVP-light) creado en F-Admin-PR0 (A1=b cerrada 2026-05-13) |

**Catálogos globales y SAT** (deberían vivir en módulo Catálogos):

| Pieza | Schema actual | Re-localización |
|---|---|---|
| `Moneda.cs` | `compartido.monedas` | → `Catalogos/Domain/` |
| `RegimenFiscal.cs` | `compartido.regimenes_fiscales` | → `Catalogos/Domain/` |
| `CondicionesPago.cs` | `compartido.condiciones_pago` | → `Catalogos/Domain/` |
| `Incoterm.cs` | `compartido.incoterms` | → `Catalogos/Domain/` |
| `Transportista.cs` | `compartido.transportistas` | → `Catalogos/Domain/` |
| `TipoPersonaProveedor.cs` | enum | → `Catalogos/Domain/` |
| `Naturaleza.cs` | enum | → `Catalogos/Domain/` |
| `UsoPrincipal.cs` | `compartido.usos_principales` | → `Catalogos/Domain/` |
| `EstatusCatalogo.cs` | enum cross-catálogos | → `Catalogos/Domain/` |

**Datos maestros operativos** (deberían vivir en módulo DatosMaestros):

| Pieza | Schema actual | Re-localización |
|---|---|---|
| `Proveedor.cs` | `compartido.proveedores` | → `DatosMaestros/Domain/` |
| `Articulo.cs` | `compartido.articulos` | → `DatosMaestros/Domain/` |

**Infraestructura transversal genuina** (se queda en `SharedKernel`):

- `BaseEntity.cs`, `Money.cs` (value object).
- Interfaces: `IAuditable`, `INotAudited`, `IFiscalmenteRelevante`, `IPerteneceAEmpresa`, `IBelongsToAggregate`.
- `Audit/AuditLogEntry.cs` (modelo audit, ADR-0008).
- `Application/Idempotency/*`, `Application/Exceptions/*`.
- `Infrastructure/Outbox/*` (ADR-0009).
- `Infrastructure/Persistence/{CoreDbContext, BaseDbContext, Interceptors}`.

**Migraciones del schema `compartido`:**

- `20260503140537_Initial`.
- `20260508170724_ProveedoresArticulos`.
- `20260509024100_OrgCatalogos` (Empresa, Sucursal, Departamento, Almacen).
- `20260511232121_CatalogosOcSeed` (seeds de regímenes, incoterms, transportistas, condiciones de pago).
- `20260512034020_UsosPrincipalesSeed`.

Las migraciones **no se rebasean** en F-Admin-PR0; permanecen en `SharedKernel/Infrastructure/Persistence/Migrations/Compartido/`. Migraciones futuras de Admin / Catálogos / DatosMaestros nacen ya en sus módulos.

### 2.3 Backend — módulo `Compras` (referencia de patrón)

Carpeta: [`backend/src/Compras/`](../../../backend/src/Compras/)

**Pieza relevante a Administración:**

- `Compras/Domain/ComprasSettings.cs` — **exemplar del patrón "settings por módulo"**. Tabla `compras.settings` con `auto_generar_oc_al_autorizar` y endpoints `GET/PATCH /api/v1/compras/configuracion`. Permisos canónicos `compras.configuracion.leer` / `.editar`.
- Documentado en ADR-0033. Lo replicarán Facturación, CxC, CxP, etc.

> Implicación: el área `/admin` **no** administra `ComprasSettings`. Cada módulo expone su propia UI en `/<modulo>/settings`. `/admin` solo enlaza.

### 2.4 Backend — endpoints actuales relacionados con catálogos

Carpeta: [`backend/src/Api/Endpoints/Catalogos/`](../../../backend/src/Api/Endpoints/Catalogos/)

| Endpoint | Propósito |
|---|---|
| `OrganizacionEndpoints.cs` | GET de empresas, sucursales, departamentos. |
| `CatalogosEndpoints.cs` | GET de catálogos genéricos (monedas, condiciones, regímenes, etc.). |
| `CatalogosOcEndpoints.cs` | GET de catálogos específicos para Compras OC. |

Comandos de creación/actualización viven en `SharedKernel/Application/Catalogos/`:

- `CrearProveedorCommand`, `ActualizarProveedorCommand`, `DesactivarProveedorCommand`.
- `CrearArticuloCommand`, `ActualizarArticuloCommand`, `DesactivarArticuloCommand`.
- `ReclasificarNaturalezaArticulosCommand`.

**Cobertura actual:**

- ✅ GET (read) de todos los catálogos relevantes.
- ✅ CRUD de Proveedor / Artículo (operados por Compras).
- ❌ Sin CRUD de Empresa, Sucursal, Departamento (solo seeds en migración).
- ❌ Sin CRUD de catálogos SAT (Moneda, Régimen Fiscal, Condiciones de pago) — son seeds inmutables hoy.
- ❌ Sin Series de folios admin (Compras tiene `FolioSecuencia` pero solo para sí mismo).

### 2.5 Frontend — shell de navegación y topbar

Carpeta: [`frontend/src/components/layout/`](../../../frontend/src/components/layout/)

| Pieza | Estado |
|---|---|
| `AppShell.tsx` | Shell principal. |
| `Sidebar.tsx`, `SidebarNav.tsx` | Sidebar plano (memoria `project_nav_shell_pattern`). |
| `MobileSidebar.tsx` | Drawer móvil. |
| `Topbar.tsx` | Topbar con hamburger, search contextual, EmpresaSelector, QuickCreateMenu, ayuda, **icono Settings (engrane) en `disabled`** ([Topbar.tsx:182](../../../frontend/src/components/layout/Topbar.tsx#L182)), UserMenu. |
| `UserMenu.tsx` | Menú del usuario actual. |
| `QuickCreateMenu.tsx` | Popover "+" para acciones rápidas. |
| `AppLauncherModal.tsx`, `AppLauncherCard.tsx` | **Modal con cards agrupadas por sección** (ADR-0032). Patrón a reusar para `/admin` landing. |
| `EmpresaSelector.tsx` | Selector multi-empresa en topbar. |

**Implicaciones:**

- El engrane está **listo para wireup**. Cambiar de `disabled` a `<Link to="/admin">` cuando exista `/admin`.
- El patrón `AppLauncherModal` ya implementa el "modal con cards agrupadas" que el owner pidió para `/admin`.
- `EmpresaSelector` consumirá empresas desde el módulo Administración una vez se refactorice (sin cambios visibles para el usuario).

### 2.6 Frontend — patrones cross-módulo ya documentados

Documento: [`frontend/docs/patrones-compras.md`](../../../frontend/docs/patrones-compras.md)

Define los patrones de UI que Admin replicará:

- **P1** — bandeja general (tabla compacta).
- **P2** — bandeja filtrada server-side.
- **P3** — master-detail con lista 320px sticky + panel detalle.
- **P4** — Sheet (slide-from-right) para "Nuevo X".
- **Inline forms** — para items dentro del master (memoria `feedback_inline_no_modal_para_items`).

Aplica a Administración:

- Usuarios, roles, empresas → P3 master-detail.
- "Nuevo usuario", "Nuevo rol", "Nueva empresa", "Nueva moneda" → P4 Sheet.
- Asignar permisos a rol, sucursales a empresa, tipos de cambio a moneda → inline forms.
- Bandejas read-mostly (catálogos SAT) → P1.

### 2.7 ADRs ya existentes que aplican

| ADR | Aplicación a Administración |
|---|---|
| [ADR-0003](../../decisiones/0003-autenticacion-entra-id.md) | Autenticación Entra ID. Asignación grupo Entra ID → rol Admin es parte del go-live. |
| [ADR-0005](../../decisiones/0005-migraciones-ef-core-esquema-por-modulo.md) | EF Core esquema por módulo. Admin crea schemas `admin`, `catalogos`, `datos_maestros` (Fase A mantiene `compartido` físico; los DbContexts dueños conviven con el schema legacy). |
| [ADR-0007](../../decisiones/0007-autorizacion-rbac-granular.md) | RBAC granular. Admin construye la UI de roles/permisos sobre este modelo. |
| [ADR-0008](../../decisiones/0008-estrategia-auditoria.md) | Auditoría. Admin agrega vista de bitácora. |
| [ADR-0011](../../decisiones/0011-multi-empresa-empresa-id.md) | Multi-empresa. Empresas y sucursales son aggregates del módulo Admin. |
| [ADR-0030](../../decisiones/0030-multi-dbcontext-por-modulo.md) | Multi-DbContext. Admin crea 3 DbContexts adicionales. |
| [ADR-0031](../../decisiones/0031-deuda-de-plataforma-y-stubs-noop.md) | Deuda con `PLATFORM-TODO`. Aplica a cualquier stub que Admin introduzca. |
| [ADR-0032](../../decisiones/0032-shell-de-navegacion-app-launcher.md) | Shell con app launcher modal. Admin reusa los componentes. |
| [ADR-0033](../../decisiones/0033-setting-auto-generar-oc-al-autorizar.md) | Setting por módulo. Exemplar del patrón que cada módulo replica; Admin solo enlaza. |
| [ADR-0034](../../decisiones/0034-area-administracion-settings-hibrido.md) | Modelo híbrido. **Decisión principal** que sustenta este módulo. |
| [ADR-0035](../../decisiones/0035-relocalizar-entidades-sharedkernel-a-modulos.md) | Re-localización de entidades de SharedKernel. **Pre-requisito** para que el módulo nazca coherente. |

## 3. Lo que NO existe

| Falta | Severidad | Notas |
|---|---|---|
| Schema `admin` (PostgreSQL) | Alta | Se crea en F-Admin-PR1 (andamio). Por Fase A de ADR-0035, las tablas existentes se mantienen en schema `compartido` por ahora. |
| Schema `catalogos` | Alta | Idem. |
| Schema `datos_maestros` | Alta | Idem. |
| `AdministracionDbContext`, `CatalogosDbContext`, `DatosMaestrosDbContext` | Alta | F-Admin-PR0 (refactor ADR-0035) los crea splitteando `CompartidoDbContext`. |
| UI de Roles/Permisos | Alta | F-Admin-PR3. |
| UI de Usuarios | Alta | F-Admin-PR4. |
| UI de Empresas + Sucursales | Alta | F-Admin-PR2 (necesaria primero — la usa Compras OC multi-empresa). |
| UI de Departamentos | Media | F-Admin-PR2 o F-Admin-PR3 (junto con usuarios). |
| CRUD de catálogos SAT (Moneda, RegimenFiscal, etc.) | Media | F-Admin-PR5. Hoy son seeds inmutables. |
| Series y folios admin | Media | F-Admin-PR6. Hoy Compras tiene `FolioSecuencia` para sí mismo. |
| Parámetros globales (TZ, formato fecha) | Baja | F-Admin-PR7. |
| Bitácora de configuración UI | Baja | F-Admin-PR7. El modelo audit ya existe (ADR-0008). |
| Registry `AdminSection` en frontend | Alta | F-Admin-PR1. Contrato de extensibilidad. |
| Landing `/admin` | Alta | F-Admin-PR1. |
| Engrane wireado en topbar | Alta | F-Admin-PR1. Cambia [Topbar.tsx:182](../../../frontend/src/components/layout/Topbar.tsx#L182) de `disabled` a `<Link to="/admin">`. |
| Permisos canónicos `admin.*`, `identidad.*` (CRUD), `catalogos.*`, `datos_maestros.*` | Alta | F-Admin-PR1 (mínimos del andamio) + uno por PR posterior. |
| Mapeo grupos Entra ID → roles | Media | Documentado en `08-runbook` (post-go-live). |
| Endpoints CRUD admin (usuarios, roles, empresas) | Alta | Cada PR de feature aporta los suyos. |

## 4. Restricciones heredadas

- **Hexagonal + CQRS con MediatR** (CLAUDE.md). Todo command/query del módulo Admin pasa por handler.
- **PostgreSQL único con schemas por módulo** (CLAUDE.md, ADR-0005). Admin crea 3 schemas adicionales (eventualmente, post Fase B de ADR-0035).
- **Entra ID para identidad** (ADR-0003). No hay AD on-premises ni federación.
- **Strangler Fig** (CLAUDE.md). Admin no migra datos de SAP en MVP — los catálogos SAT se cargan vía seeds versionados. La importación de catálogos legacy es deuda separada cuando el cliente lo priorice (consistente con Fase 7 de Compras según memoria `project_fase7_mvp_scope`).
- **Idioma:** documentación, comentarios de negocio, nombres de módulos en español. Código y nombres técnicos en inglés.
- **Multi-empresa** (ADR-0011). Empresa es entidad de primera clase.
- **Tracking de deuda** (ADR-0031). Cualquier stub o NoOp introducido por Admin debe llevar `PLATFORM-TODO(<identificador>):`.

## 5. Actores y roles

| Rol propuesto | Acciones esperadas |
|---|---|
| **Super-administrador** | Acceso total a `/admin/*` y a settings de cada módulo. CRUD usuarios, roles, empresas, sucursales, departamentos, catálogos, series, parámetros. Asignar roles a usuarios. Ver bitácora completa. |
| **Administrador de identidad** | CRUD usuarios y roles. Asignar/desasignar roles. Sin acceso a settings de módulos. |
| **Administrador organizacional** | CRUD empresas, sucursales, departamentos. Sin acceso a usuarios/roles. |
| **Administrador de catálogos** | CRUD catálogos globales (incluye SAT). |
| **Administrador de datos maestros** | CRUD proveedores, artículos. |
| **Auditor** | Solo lectura sobre bitácora + listas (usuarios, roles, empresas). |
| **Administrador por módulo** | Hereda permisos `<modulo>.settings.*`. Configura solo su módulo (ej. "Administrador Compras" — ADR-0033 ya tiene `compras.configuracion.editar`). |

Los roles concretos seedeados en el MVP se definen en `01-diseno.md` §3 y se materializan vía migración + `BootstrapSuperAdminHostedService.cs` extendido.

## 6. Casos de uso principales

1. **Bootstrap inicial.** Un super-admin (seedeado) entra por primera vez, configura empresa(s), sucursales, departamentos, asigna grupo Entra ID → rol super-admin a usuarios concretos.
2. **Alta de un usuario operativo.** Admin crea usuario (o mapea desde Entra ID), le asigna rol(es) por empresa, lo activa.
3. **Configurar Compras.** Admin de Compras entra a `/compras/settings`, activa `AutoGenerarOcAlAutorizar` para la empresa A. (ADR-0033 ya implementado.)
4. **Agregar empresa nueva.** Super-admin crea Empresa B, le agrega sucursales y departamentos, se asignan usuarios.
5. **Auditar cambio.** Auditor entra a `/admin/auditoria`, busca cambios al setting de Compras en el último mes.
6. **Cargar tipo de cambio del día.** Admin de catálogos entra a `/admin/catalogos/monedas`, agrega tipo de cambio MXN/USD del día (inline form).
7. **Definir serie de folios.** Admin crea serie "OC-2026" para Empresa A, sucursal CDMX, tipo documento Orden de Compra, prefijo "OC", siguiente folio 1.

## 7. Integraciones internas

- **Identidad ↔ Admin UI:** la UI de roles/permisos consume el dominio `Identidad` directamente. No hay puerto nuevo.
- **EmpresaSelector:** lee desde `Administracion` (post-refactor ADR-0035). API estable.
- **Compras ↔ Admin:** Compras consume Empresa, Sucursal, Departamento, Almacen, Proveedor, Articulo, Moneda, CondicionesPago, RegimenFiscal, Incoterm, Transportista de los módulos correspondientes (post-refactor). FK por id, sin navegación cross-módulo (ADR-0035).
- **Login flow (`LoginOrchestrator`):** ya consume Empresa, Sucursal, Departamento. Post-refactor consume desde `Administracion`. Sin cambios funcionales.
- **A+W:** integración futura puede traer maestros (proveedores, artículos) desde A+W. Punto de extensión para Datos Maestros — no MVP.

## 8. Integraciones externas

- **Microsoft Entra ID** (ADR-0003). Mapeo grupos → roles. Documentado en `08-runbook`.
- **SAT** — catálogos oficiales (usos CFDI, formas de pago, régimenes fiscales) cargados como seeds. Actualización manual via migración cuando el SAT publique cambios.
- **SAP / Portal SAP** — no se integran con Admin en MVP (Strangler Fig).
- **A+W** — no en MVP.

## 9. Riesgos identificados

| Riesgo | Mitigación |
|---|---|
| Refactor ADR-0035 deja imports rotos. | F-Admin-PR0 es solo refactor, guiado por compilador + `dotnet test`. CI obligatorio antes de merge. |
| El primer super-admin queda inaccesible. | `BootstrapSuperAdminHostedService` ya existe; se extiende para garantizar idempotencia. Documentado en `08-runbook` con procedimiento de recuperación. |
| Permisos canónicos se desincronizan entre código y BD. | Mismo patrón actual: migración seedea desde `PermisosCanonicos.cs`. Tests de seed verifican consistencia. |
| Catálogos SAT cambian (SAT publica actualización). | Migración aditiva con seeds nuevos. Sin riesgo de pérdida de datos. |
| Confusión entre "settings transversal" vs "settings de módulo". | ADR-0034 define el criterio. `01-diseno.md` §2 lo elabora con ejemplos. |
| Multi-tenant: un super-admin global pero permisos por empresa. | El modelo `UsuarioEmpresaRol` ya cubre esto. UI debe respetar el `empresa_id` del contexto. |

## 10. Decisiones cerradas

Asunciones A1–A7 cerradas por el owner el **2026-05-13** durante la sesión de cierre del PR doc-only #1. Todas marcadas `[Decidido]`.

- **[A1 = b · Decidido]** `Almacen` **no** queda en `Administracion`. Se crea módulo `Almacen` (almacenes no-productivos, MVP-light) en F-Admin-PR0. El módulo nace con solo la entidad `Almacen.cs` y su DbContext; su superficie crece cuando se aborde el alcance completo del módulo Almacén-no-prod (inventario, movimientos, recepciones cross-Compras).
- **[A2 · Decidido]** Set inicial de roles validado: Super-administrador, Administrador de identidad, Administrador organizacional, Administrador de catálogos, Administrador de datos maestros, Auditor, Administrador Compras (este último ya existe vía ADR-0033). Se seedean en F-Admin-PR3 (Roles) extendiendo `BootstrapSuperAdminHostedService.cs`.
- **[A3 = a · Decidido]** Mapeo Entra ID → roles **manual por grupo** vía UI en `/admin/roles/$id`. Sync automatizado queda como dependencia de plataforma pendiente (`<EntraIdMapping>`).
- **[A4 = a · Decidido]** Tipos de cambio se cargan **manualmente** vía UI inline en `/admin/catalogos/monedas`. Sync DOF/Banxico es deuda futura (`<TipoCambioSync>`).
- **[A5 = a · Decidido (ampliada)]** Series de folios soportan tres modos de reinicio: **anual, mensual o eterno** (sin reinicio). El campo `ReinicioPeriodo` del agregado `Serie` es enum `None | Anual | Mensual`, donde `None = eterno`.
- **[A6 = a · Decidido]** `Departamento` permanece en `Administracion` como entidad organizacional. No se prevé módulo RRHH separado en el roadmap del back-office MVP.
- **[A7 = b · Decidido]** **Auto-renderizado de settings genéricos** se construye desde día 1. Contrato `SettingsSchema` (declarativo) en ADR-0034 §Notas de implementación. Cada módulo expone `GET /api/v1/<modulo>/settings/schema` con items `{ clave, etiqueta, descripcion, tipo, default, valor, validacion, permisoLeer, permisoEditar, mostrar (Auto | Custom), rutaCustom?, alertaCambio? }`. La página `/admin/<modulo>/settings` renderiza form genérico para items `Auto`; items `Custom` linkean a UI dedicada del módulo (ej. `ComprasSettings.AutoGenerarOcAlAutorizar` que tiene efectos cross-cutting). Las dos vías conviven.

## 11. Dependencias de plataforma pendientes

Sección obligatoria por ADR-0031. En el momento del levantamiento, el módulo Administración **no introduce stubs `NoOp` propios**. Sin embargo, hereda dos dependencias de plataforma activas que pueden requerir wireup tardío:

| Pieza | Ticket / Identificador | NoOp en uso hoy | Cómo se wirea |
|---|---|---|---|
| Bitácora UI (`/admin/auditoria`) | `<AuditUI>` | Modelo `AuditLogEntry` activo (ADR-0008); aún sin endpoint de listado consolidado | F-Admin-PR7 expone endpoint `GET /api/v1/admin/auditoria` + UI |
| Mapeo Entra ID → Roles automatizado | `<EntraIdMapping>` | Hoy se mapea manualmente vía `UsuarioEmpresaRol` | Q3/post-MVP: hosted service que sincroniza grupos Entra ID → roles |

Cualquier stub adicional que aparezca durante implementación se agrega aquí con `PLATFORM-TODO(<identificador>):` en código (memoria `feedback_platform_debt_tracking`).

## 12. Próximos pasos

1. Validar este levantamiento con el owner (especialmente §10 asunciones A1–A7).
2. Producir `01-diseno.md` con modelo de dominio detallado del módulo, registry, contratos de extensibilidad.
3. ADR-0034 y ADR-0035 ya redactados (parte de este PR doc-only #1).
4. PR doc-only #2 producirá 02 a 07.
5. PR doc-only #3 producirá 08 y 09 (runbook + go-live checklist).
6. F-Admin-PR0 (refactor ADR-0035) se ejecuta antes de cualquier feature work.

## Rev.

- **2026-05-13** — Rev. 2. Decisiones A1–A7 cerradas por el owner. Detalle en §10. A1 cambia ubicación de `Almacen` (nuevo módulo). A7 cambia arquitectura de settings (auto-renderizado desde día 1; contrato `SettingsSchema` en ADR-0034).
- **2026-05-13** — Rev. 1. Levantamiento inicial. Autor: Claude. Pendiente validación owner.
