# Plan de implementación — Módulo Administración (incluye Identidad, Catálogos, Datos Maestros y Almacén MVP-light)

> **Construido sobre:** [01-diseno.md](01-diseno.md) (Rev. 2, decisiones A1–A7 cerradas), [ADR-0034](../../decisiones/0034-area-administracion-settings-hibrido.md) y [ADR-0035](../../decisiones/0035-relocalizar-entidades-sharedkernel-a-modulos.md).
>
> **Estado:** propuesta de plan para revisión con el owner. Sizing en bandas (XS/S/M/L/XL) — calibrar contra capacidad real del equipo.
>
> **Fecha:** 2026-05-13.

---

## 0. Cómo leer

- Sizing en bandas:
  - **XS** ≈ 1–2 días
  - **S** ≈ 3–5 días
  - **M** ≈ 1–2 semanas
  - **L** ≈ 3–4 semanas
  - **XL** > 1 mes
- Cada fase produce algo **deployable y validable** — no son entregables internos del equipo.
- Las fases son secuenciales por dependencia técnica, pero dentro de cada fase hay paralelismo posible (anotado como "‖").
- **Reuso primero (regla del proyecto):** cada fase indica qué pieza hereda del estado actual antes de listar lo nuevo (memoria `feedback_reutilizacion_codigo`).

---

## 1. Resumen ejecutivo

**Objetivo:** entregar v1 del área de Administración del ERP — el primer área **transversal** no-de-dominio del back-office, que servirá como exemplar para los 9 módulos restantes (Facturación, CxC, CxP, Almacén-no-prod completo, Activos Fijos, Contabilidad, BI, A+W).

**Estrategia:**

1. **Refactor primero, features después.** F-Admin-PR0 ejecuta ADR-0035 (re-localización SharedKernel → módulos dueños) **antes** de cualquier feature work. Esto garantiza que el módulo nazca coherente.
2. **Andamio que abre puertas.** F-Admin-PR1 entrega el shell mínimo (registry, landing, gear wireado, contrato `SettingsSchema`). Cualquier módulo futuro se enchufa con cero cambios al shell.
3. **Iterativo por concepto.** Cada fase suma un concepto cerrado: empresas → roles → usuarios → datos maestros → catálogos SAT → series → auditoría/parámetros.
4. **Reuso máximo.** `Identidad` ya implementado (dominio completo, falta UI). `AppLauncherModal`/`AppLauncherCard` ya cubren el patrón de cards (ADR-0032). `EmpresaSelector` ya existe. `ComprasSettings` (ADR-0033) es exemplar del patrón "settings por módulo" y se adapta al contrato `SettingsSchema` en PR1.
5. **Strangler Fig respetado.** No se migran datos masivos desde SAP en MVP; catálogos SAT vía seeds versionados (consistente con `project_fase7_mvp_scope`).
6. **Almacén MVP-light** (decisión A1=b): nace el módulo con solo la entidad `Almacen`; su alcance completo se aborda en su propio sprint cuando el back-office llegue a inventario.

**Pendientes que NO bloquean arranque:**

- Sync automatizado Entra ID → roles (`<EntraIdMapping>`) — manual en MVP (A3=a).
- Sync de tipos de cambio DOF/Banxico (`<TipoCambioSync>`) — manual en MVP (A4=a).
- Schema rename físico (Fase B ADR-0035) — diferido sin fecha.

---

## 2. Prerrequisitos — audit del repo (2026-05-13)

Auditado contra `c:\Users\UserSP\Desktop\Project_Millet_ERP\backend\` y `frontend\`. Estado real:

| Prerrequisito | Estado | Detalle / plan B |
|---|---|---|
| Proyecto `Millet.Identidad` | ✅ existe | Dominio completo (Usuario, Rol, Permiso, RolPermiso, PermisosCanonicos, UsuarioEmpresaRol). UI se construye desde F-Admin-PR3. |
| `IdentidadDbContext` + schema `identidad` | ✅ existe | Sin cambios estructurales. |
| `BaseEntity` con `Version IsConcurrencyToken` (ADR-0012) | ✅ existe | Cada nueva entidad de Admin hereda. |
| `BaseDbContext` con interceptors (Audit, Empresa, Metadata) | ✅ existe | Auditoría e idempotencia funcionan gratis en los DbContexts nuevos. |
| EF Core + migraciones por esquema (ADR-0005) | ✅ existe | Nuevos DbContexts agregan migraciones bajo `<Modulo>/Infrastructure/Migrations/`. |
| Entra ID + RBAC granular | ✅ existe | UI consume el dominio sin cambios. |
| `RequirePermissionAttribute` + handler | ✅ existe | Endpoints admin se anotan igual que Compras. |
| Problem Details (ADR-0010) | ✅ existe | Errores de admin siguen el mismo formato. |
| `Money`, `Empresa`, `Sucursal`, catálogos SAT | ✅ existe en `SharedKernel/Domain/` | **F-Admin-PR0 los re-localiza** a sus módulos dueños (ADR-0035). |
| `CompartidoDbContext` | ✅ existe | F-Admin-PR0 lo divide en `AdministracionDbContext` + `CatalogosDbContext` + `DatosMaestrosDbContext` + `AlmacenDbContext`. Schema físico permanece `compartido` (Fase A). |
| Health checks + `MigrationsAppliedHealthCheck` | ✅ existe | Cada DbContext nuevo registra su check (memoria `feedback_dbcontext_nuevo_checklist`). |
| Serilog + masking | ✅ existe | Logging estructurado gratis. |
| `IClock`, `ICurrentUserContext`, `ICurrentEmpresaContext` | ✅ existe | Inyectados en handlers. |
| `BootstrapSuperAdminHostedService` | ✅ existe | Se extiende en F-Admin-PR3 para seedear los 7 roles MVP. |
| `AppLauncherModal` + `AppLauncherCard` (ADR-0032) | ✅ existe | Reusados en F-Admin-PR1 para landing `/admin`. |
| `EmpresaSelector` en topbar | ✅ existe | Consume Empresa desde `Administracion` post-refactor. Sin cambios funcionales. |
| **Icono Settings (engrane) en topbar** | ✅ existe, `disabled` | F-Admin-PR1 lo wireba a `/admin`. |
| `ComprasSettings` (ADR-0033) | ✅ existe | Exemplar del patrón `<modulo>/settings`. F-Admin-PR1 lo adapta al contrato `SettingsSchema`. |
| Patrones P1/P2/P3/P4 + inline forms | ✅ documentados en `frontend/docs/patrones-compras.md` | Replicar en bandejas, detalles y Sheets de admin. |
| OpenAPI + tipos TS (ADR-0017) | ✅ existe | Endpoints admin se agregan al pipeline automáticamente. |
| Idempotencia HTTP (ADR-0020) | ✅ existe | Endpoints PATCH/POST de admin aplican el header. |
| Auditoría modelo (ADR-0008) | ✅ existe (`AuditLogEntry`) | F-Admin-PR7 agrega UI consolidada. |
| Outbox (ADR-0009) | ⏳ infra activa | Eventos de admin (EmpresaCreadaEvent, etc.) usan el outbox cuando aplique; listeners in-process via MediatR mientras tanto. |
| Multi-DbContext (ADR-0030) | ✅ aceptada | Admin crea 4 DbContexts (Administracion, Catalogos, DatosMaestros, Almacen) — alineado con la decisión. |

---

## 3. Fases

### Fase 0 — Refactor ADR-0035 (M)

**Objetivo:** mover entidades de `SharedKernel/Domain/` a módulos dueños sin cambios funcionales. Schema físico permanece `compartido`. Resultado: el módulo Administración (y los 3 hermanos) nacen con su dominio coherente.

**Reuso:** todo el código y dominio existente. Es solo reorganización.

**Lo nuevo:**

- 4 proyectos .NET: `backend/src/Administracion/`, `backend/src/Catalogos/`, `backend/src/DatosMaestros/`, `backend/src/Almacen/`.
- 4 DbContexts: `AdministracionDbContext`, `CatalogosDbContext`, `DatosMaestrosDbContext`, `AlmacenDbContext`. Cada uno apunta a schema `compartido` por `ToTable(..., schema: "compartido")`.
- Endpoints reagrupados: `Api/Endpoints/Administracion/`, `Api/Endpoints/Catalogos/`, `Api/Endpoints/DatosMaestros/`, `Api/Endpoints/Almacen/`. **URLs estables.**
- Application commands movidos a sus módulos: comandos de Proveedor/Articulo a `DatosMaestros.Application`.

**Riesgos:**

- Imports rotos en ~100 archivos. Mitigación: cambio mecánico guiado por compilador; CI obligatorio antes de merge.
- Navegación EF cross-módulo (Usuario → Departamento). Mitigación: cambiar a FK por id sin navegación EF (recomendado en ADR-0035).

**Sizing:** M (~1–2 semanas).

**Entrega:** `dotnet build` verde, `dotnet test` 100% pasando, sin cambios funcionales observables.

---

### Fase 1 — Andamio + contrato SettingsSchema (M)

**Objetivo:** activar el área `/admin` con sus piezas mínimas + el contrato `SettingsSchema` (A7=b). Resultado: el engrane del topbar abre `/admin` con un landing de cards vacío (porque cada feature suma su card en su PR). Cualquier módulo nuevo (Facturación, CxC, etc.) puede enchufar settings auto-renderizados sin tocar el shell.

**Reuso:** `AppLauncherModal`/`AppLauncherCard` (ADR-0032), `Topbar`, registry pattern de la sidebar.

**Lo nuevo:**

- Registry `AdminSection` + barrel `adminRegistry` (frontend).
- Página `/admin` (landing) renderizando registry filtrado por permisos.
- Engrane wireado en `Topbar.tsx:182` (de `disabled` a `<Link to="/admin">` con hook `useAdminAccess()`).
- Permisos canónicos mínimos: `admin.empresas.leer`, `identidad.usuarios.leer`, `identidad.roles.leer`, `admin.auditoria.leer`. Seed en migración de `Identidad`.
- Schemas de los 4 DbContexts creados (`admin`, `catalogos`, `datos_maestros`, `almacen` cuando se evalúe Fase B; en Fase A solo cambia el código, no la BD).
- **Contrato `SettingsSchema`**: `ISettingsSchemaProvider`, `SettingItem`, endpoints genéricos `GET /api/v1/{modulo}/settings/schema` y `PATCH /api/v1/{modulo}/settings/{clave}`.
- **Compras adopta el contrato como exemplar**: `ComprasSettingsSchemaProvider` expone `AutoGenerarOcAlAutorizar` con `Mostrar = Custom` (link a `/compras/configuracion`).
- Hook `useSettingsSchema(modulo)` en frontend para consumir el schema.
- Componente genérico `<AutoSettingsForm>` que renderiza el form desde el schema.
- Página `/admin/<modulo>/settings` que usa `<AutoSettingsForm>`.

**Riesgos:**

- Engrane visible sin permisos. Mitigación: hook `useAdminAccess()` con tests.
- Contrato `SettingsSchema` mal diseñado bloquea adopción. Mitigación: validar con `ComprasSettings` como caso real.

**Sizing:** M (~1–2 semanas).

**Entrega:** click en engrane → `/admin` muestra grid de cards (vacío por ahora; las features siguientes lo llenan). Sin permisos, engrane oculto. Compras visible como card `displayMode=custom` con link a `/compras/configuracion`. `GET /api/v1/compras/settings/schema` responde 200 con `AutoGenerarOcAlAutorizar`.

---

### Fase 2 — Empresas + Sucursales + Departamentos (M)

**Objetivo:** UI master-detail completa para gestión organizacional. Es la **primera feature funcional** del área y la que Compras OC multi-empresa va a empezar a consumir.

**Reuso:** patrones P3 (master-detail) y P4 (Sheet) ya documentados. `EmpresaSelector` ya carga empresas; ahora hay UI para gestionarlas.

**Lo nuevo:**

- Commands/queries: `CrearEmpresaCommand`, `ActualizarEmpresaCommand`, `DesactivarEmpresaCommand`, `CrearSucursalCommand`, `ActualizarSucursalCommand`, `DesactivarSucursalCommand`, `CrearDepartamentoCommand`, `ActualizarDepartamentoCommand`, `ListarEmpresasQuery`, `ObtenerEmpresaQuery`.
- Endpoints REST bajo `/api/v1/admin/empresas/*` y `/api/v1/admin/departamentos/*`.
- Permisos canónicos: `admin.empresas.crear|editar|desactivar`, `admin.empresas.sucursales.gestionar`, `admin.departamentos.gestionar`.
- UI: `/admin/empresas` (P3 master-detail), `/admin/empresas/$id` con tabs (datos | sucursales (inline form) | departamentos | series | settings link).
- Eventos: `EmpresaCreadaEvent`, `SucursalCreadaEvent` publicados via Outbox.

**Riesgos:**

- Multi-empresa: la UI debe respetar el contexto activo. Mitigación: tests integration que verifican filtro por `EmpresaId` activo.
- Invariantes (RFC único, no eliminar empresa con sucursales activas). Mitigación: validators + tests unitarios.

**Sizing:** M.

**Entrega:** un super-admin puede crear empresa, agregar sucursales y departamentos via inline forms, ver lista en `/admin/empresas`.

---

### Fase 3 — Roles + matriz de permisos (M)

**Objetivo:** UI completa de gestión de roles y permisos sobre el dominio `Identidad` existente. **Aquí se implementa el mapeo manual de grupos Entra ID → rol** (A3=a).

**Reuso:** dominio `Identidad.Rol`, `Identidad.Permiso`, `Identidad.RolPermiso`, `PermisosCanonicos.cs`. **Seeds** vía `BootstrapSuperAdminHostedService.cs` extendido con los 7 roles MVP (A2 cerrada).

**Lo nuevo:**

- Commands/queries: `CrearRolCommand`, `ActualizarRolCommand`, `EliminarRolCommand` (solo si sin asignaciones), `AsignarPermisosARolCommand`, `AsociarGrupoEntraIdARolCommand`, `DesasociarGrupoEntraIdDeRolCommand`, `ListarRolesQuery`, `ObtenerRolQuery`, `ListarPermisosQuery`.
- Endpoints REST bajo `/api/v1/identidad/roles/*` y `/api/v1/identidad/permisos`.
- Migración: tabla `identidad.rol_grupo_entra_id` (`RolId`, `GrupoEntraIdObjectId`).
- Seeds: 7 roles MVP base + sus permisos asociados.
- Permisos canónicos: `identidad.roles.crear|editar|eliminar|asignar_permisos`, `identidad.permisos.leer`.
- UI: `/admin/roles` (P3 master-detail), `/admin/roles/$id` con tabs (datos | permisos (inline form, matriz agrupada por módulo) | grupos Entra ID (inline form) | usuarios asignados).
- Eventos: `RolPermisosActualizadosEvent` para invalidar caches de autorización.

**Riesgos:**

- Eliminar rol asignado a usuarios. Mitigación: invariante + validator.
- Cambios masivos a permisos generan auditoría ruidosa. Mitigación: PATCH transaccional + un solo `AuditLogEntry` por rol modificado.

**Sizing:** M.

**Entrega:** super-admin gestiona los 7 roles MVP; asigna permisos (matriz visual); asocia grupos Entra ID a cada rol.

---

### Fase 4 — Usuarios + asignación rol×empresa (M)

**Objetivo:** UI de usuarios y la asociación N:M usuario × empresa × rol.

**Reuso:** dominio `Identidad.Usuario`, `Identidad.UsuarioEmpresaRol`.

**Lo nuevo:**

- Commands/queries: `CrearUsuarioCommand` (alta manual o mapeo desde Entra ID por email), `ActualizarUsuarioCommand`, `DesactivarUsuarioCommand`, `AsignarRolAUsuarioCommand`, `RevocarRolDeUsuarioCommand`, `ListarUsuariosQuery`, `ObtenerUsuarioQuery`.
- Endpoints REST bajo `/api/v1/identidad/usuarios/*`.
- Permisos canónicos: `identidad.usuarios.crear|editar|desactivar`.
- UI: `/admin/usuarios` (P3 master-detail), `/admin/usuarios/$id` con tabs (datos | asignaciones rol×empresa (inline form) | preferencias).
- Eventos: `UsuarioRolAsignadoEvent`, `UsuarioRolRevocadoEvent`.

**Riesgos:**

- Sincronización con Entra ID en alta manual. Mitigación: validador resuelve `EntraIdObjectId` consultando Graph API en el handler (sync inmediato, no automático).
- Email único cross-empresa. Mitigación: invariante + índice único.

**Sizing:** M.

**Entrega:** super-admin da de alta usuarios, asigna roles por empresa, desactiva.

---

### Fase 4.5 — Datos Maestros UI (S)

**Objetivo:** UI de administración de Proveedores y Artículos bajo `/admin/datos-maestros/*`. El backend ya existe (commands movidos en F-Admin-PR0); aquí se agrega la cara de admin (filtros, búsqueda avanzada, listas grandes, exports).

**Reuso:** `CrearProveedorCommand`, `CrearArticuloCommand`, etc. ya implementados. Solo se enriquece queries y UI.

**Lo nuevo:**

- Queries enriquecidas: `ListarProveedoresQuery` con filtros (RFC, razón social, tipo persona, régimen, estatus, naturaleza); `ListarArticulosQuery` con filtros (código, descripción, naturaleza, unidad, estatus).
- UI: `/admin/datos-maestros/proveedores` (P3), `/admin/datos-maestros/articulos` (P3).
- Permisos canónicos: `datos_maestros.proveedores.gestionar`, `datos_maestros.articulos.gestionar`.
- Export CSV básico (opcional, evaluar).

**Sizing:** S.

**Entrega:** super-admin/admin de datos maestros administra proveedores y artículos desde `/admin`.

---

### Fase 5 — Catálogos SAT (M)

**Objetivo:** CRUD de catálogos globales: Moneda + tipos de cambio (carga manual, A4=a), CondicionesPago, FormasPago, UsosCfdi, RegimenFiscal, Incoterm, Transportista, UnidadMedida.

**Reuso:** entidades movidas en F-Admin-PR0. Seeds SAT existentes (`CatalogosOcSeed`).

**Lo nuevo:**

- Commands/queries para cada catálogo: `Crear*Command`, `Actualizar*Command`, `Desactivar*Command`, `Listar*Query`.
- Comando especial `RegistrarTipoCambioCommand` (inline en `Moneda`).
- Endpoints REST bajo `/api/v1/catalogos/*` (estables) + `/api/v1/catalogos/monedas/{id}/tipos-cambio`.
- Permisos canónicos: `catalogos.<recurso>.gestionar` (uno por catálogo).
- Migración: tabla `catalogos.tipos_cambio` (`MonedaId`, `Fecha`, `ValorEnMxn`, `Origen`).
- UI: `/admin/catalogos/<recurso>` por cada catálogo. Patrones:
  - **Moneda**: P3 con tipos de cambio inline (form sin modal por memoria `feedback_inline_no_modal_para_items`).
  - **CondicionesPago, Incoterm, Transportista, UnidadMedida**: P1 + Sheet.
  - **FormasPago, UsosCfdi, RegimenFiscal**: P1 read-only (SAT).
- Seeds completos SAT: catálogo de FormasPago (DOF), UsosCfdi (Anexo 20), RegimenFiscal (Anexo 24), UnidadMedida.

**Riesgos:**

- Catálogos SAT cambian; migraciones deben ser aditivas. Mitigación: seed con `ON CONFLICT DO NOTHING`.
- Eliminar catálogo referenciado por entidades operativas. Mitigación: invariante + desactivar en lugar de eliminar.

**Sizing:** M.

**Entrega:** admin de catálogos gestiona toda la batería SAT y catálogos operativos.

---

### Fase 6 — Series y folios (S)

**Objetivo:** UI de gestión de series y folios. Soporta **anual, mensual o eterno** (A5 ampliada). Compras OC reconcilia su `FolioSecuencia` propia con el nuevo modelo.

**Reuso:** `Compras.FolioSecuencia` y `Folio` (Compras-OC F1-PR1) — se rehacen sobre `Administracion.Serie` + `Administracion.SecuenciaFolio`.

**Lo nuevo:**

- Agregado `Serie` con `ReinicioPeriodo: None | Anual | Mensual`.
- Aggregate `SecuenciaFolio` (entidad hija) con `PeriodoClave` (`""` para None, `"2026"` para Anual, `"2026-05"` para Mensual).
- Command `ReservarFolioCommand` (idempotente, atómico) consumible por handlers de módulos de negocio (`CrearOrdenCompraVaciaHandler` migrará en este PR para usar `ReservarFolioCommand` en lugar de su `FolioSecuencia` actual).
- Commands/queries: `CrearSerieCommand`, `ActualizarSerieCommand`, `DesactivarSerieCommand`, `ListarSeriesQuery`, `ObtenerSerieQuery`.
- Endpoints REST bajo `/api/v1/admin/series/*` y `POST /api/v1/admin/series/{id}/reservar` (consumo interno).
- Migración: tabla `admin.series`, tabla `admin.secuencias_folio` con UNIQUE (`SerieId`, `PeriodoClave`).
- UI: `/admin/series` (P1 + Sheet de "Nueva serie") + inline form para preview del próximo folio según el modo de reinicio.
- Permisos canónicos: `admin.series.gestionar`.

**Riesgos:**

- Concurrencia en `ReservarFolioCommand` (race condition). Mitigación: SELECT FOR UPDATE + `Idempotency-Key` (ADR-0020) + tests con N reservas en paralelo.
- Migración del `FolioSecuencia` de Compras: cuidar que las series existentes se mapeen correctamente al nuevo modelo. Mitigación: migración seed que crea `Serie` por cada `FolioSecuencia` actual; deprecación gradual de `FolioSecuencia` con `PLATFORM-TODO(<FolioSecuenciaDeprecate>)`.

**Sizing:** S.

**Entrega:** admin crea serie "OC-2026" con `ReinicioPeriodo=Anual`, prefijo "OC", siguiente folio "OC-2026-0001". Compras OC usa el nuevo modelo.

---

### Fase 7 — Auditoría + parámetros globales (S)

**Objetivo:** UI consolidada de bitácora + parámetros generales del sistema.

**Reuso:** `AuditLogEntry` (ADR-0008) ya activo en todos los DbContexts. Interceptor `AuditSaveChangesInterceptor` ya está mapeado.

**Lo nuevo:**

- Query `ConsultarBitacoraQuery` con filtros (módulo, recurso, acción, usuario, rango de fechas, empresa).
- Commands: `ActualizarParametroGlobalCommand`.
- Endpoints REST: `GET /api/v1/admin/auditoria` (P2 server-side), `GET /api/v1/admin/parametros`, `PATCH /api/v1/admin/parametros/{clave}`.
- Migración: tabla `admin.parametros_globales` (`Clave`, `Valor`, `Tipo`, `Modulo?`, `Descripcion`, `UltimaActualizacion`). Seed con parámetros iniciales (TimezoneDefault, FormatoFecha, RedondeoMonetario).
- Permisos canónicos: `admin.parametros.editar`, `admin.auditoria.leer`.
- UI: `/admin/auditoria` (P2 bandeja filtrada), `/admin/parametros` (form simple).

**Riesgos:**

- Bandeja de auditoría con millones de rows. Mitigación: índices por (FechaUtc, Modulo) + paginación server-side + filtros obligatorios (rango de fechas).
- Cerrar dependencia de plataforma `<AuditUI>` (ADR-0031).

**Sizing:** S.

**Entrega:** auditor consulta bitácora con filtros; admin edita parámetros globales del sistema.

---

## 4. Paralelización entre fases

```
Fase 0 (refactor) ──┬─→ Fase 1 (andamio + SettingsSchema)
                    │
                    ├─→ Fase 2 (Empresas) ──┬─→ Fase 3 (Roles) ──→ Fase 4 (Usuarios)
                    │                       │
                    │                       ├─→ Fase 5 (Catálogos SAT)
                    │                       │
                    │                       ├─→ Fase 4.5 (Datos Maestros UI)
                    │                       │
                    │                       └─→ Fase 6 (Series y folios)
                    │
                    └─→ Fase 7 (Auditoría + parámetros)
```

Fases 2–7 pueden paralelizarse **dentro de fronteras**: Fase 2 (Empresas) es prerrequisito de Fase 3 (Roles) solo si la UI de Roles necesita filtrar por empresa; si no, son paralelizables.

Fase 5 (Catálogos), Fase 4.5 (Datos Maestros UI) y Fase 6 (Series) son **completamente paralelizables** entre sí (tocan dominios independientes). Fase 7 (Auditoría) puede arrancar después de Fase 2 (cuando hay datos para auditar).

## 5. Sizing total y secuencia recomendada

| Fase | Sizing | Acumulado |
|---|---|---|
| F-Admin-PR0 | M | ~1.5 sem |
| F-Admin-PR1 | M | ~3 sem |
| F-Admin-PR2 | M | ~5 sem |
| F-Admin-PR3 | M | ~7 sem |
| F-Admin-PR4 | M | ~9 sem |
| F-Admin-PR4.5 | S | ~10 sem |
| F-Admin-PR5 | M | ~12 sem |
| F-Admin-PR6 | S | ~13 sem |
| F-Admin-PR7 | S | ~14 sem |

**Total estimado:** ~14 semanas en sequential. Con paralelización razonable (1 dev backend + 1 dev frontend), ~9–10 semanas calendarias.

## 6. Riesgos transversales y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Refactor ADR-0035 deja imports rotos en producción. | Solo dev hasta MVP (ADR-0028). Sin producción que romper. Tests + CI obligatorios. |
| El super-admin queda inaccesible post-migración. | `BootstrapSuperAdminHostedService` idempotente garantiza al menos un super-admin. Documentado en `08-runbook` con recuperación. |
| Permisos canónicos se desincronizan entre código y BD. | Migración seedea desde `PermisosCanonicos.cs`. Tests verifican consistencia. |
| Contrato `SettingsSchema` mal diseñado. | F-Admin-PR1 valida con `ComprasSettings` como exemplar real antes de "blessing" del contrato. |
| Multi-empresa: contexto activo no respetado en endpoints admin. | Tests integration que cambian contexto y verifican aislamiento. |
| Migración serie de folios de Compras rompe OC en flight. | Migración seed que mapea `FolioSecuencia` actual a `Serie`. Compatibility shim hasta deprecación. |
| Catálogos SAT cambian; migración aditiva con seeds. | `ON CONFLICT DO NOTHING` en seeds. |

## 7. Dependencias de plataforma pendientes derivadas

Heredadas del `01-diseno.md §12`:

- `<EntraIdMapping>` — sync automático post-MVP.
- `<TipoCambioSync>` — sync DOF/Banxico post-MVP.
- `<SchemaRename>` — Fase B ADR-0035 sin fecha.
- `<AlmacenRemove>` — el módulo `Almacen` MVP-light se enriquece cuando se construya su alcance completo (no es "remove", es "expansion").
- `<CatalogosSapImport>` — migración masiva diferida.
- `<AuditUI>` — cerrado en F-Admin-PR7.
- `<SettingsAutoRender>` — cerrado en F-Admin-PR1.

## 8. Próximos pasos

1. Validar este plan con el owner (sizing y orden).
2. Producir `03-pr-breakdown.md` con detalle backend por PR.
3. Producir `04-cuidados-infra.md` con migraciones, healthchecks, deploy YAML.
4. Producir `05-frontend-diseno.md`, `06-frontend-plan-implementacion.md`, `07-frontend-pr-breakdown.md`.
5. Después del PR doc-only #2, producir PR doc-only #3 (`08-operacion-y-runbook.md` + `09-go-live-checklist.md`).
6. F-Admin-PR0 (refactor ADR-0035) arranca en `admin/sk-refactor-pr0`.

## Rev.

- **2026-05-13** — Rev. 1. Plan inicial. Autor: Claude. Incorpora decisiones A1–A7. Pendiente validación owner.
