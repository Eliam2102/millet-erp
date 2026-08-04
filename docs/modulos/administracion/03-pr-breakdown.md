# PR Breakdown — Módulo Administración (backend)

> **Construido sobre:** [01-diseno.md](01-diseno.md) (Rev. 2) y [02-plan-implementacion.md](02-plan-implementacion.md) (Rev. 1).
>
> **Estado:** Rev. 1 — propuesta inicial.
> **Fecha:** 2026-05-13.

---

## 0. Cómo leer

- Cada fila es **un PR**. ID `F-Admin-PR<n>` o `F-Admin-PR<n>.<sub>` si la fase requiere división.
- **Tamaños**: XS (≤ 200 líneas netas), S (200–500), M (500–800). Techo absoluto: 800. Por encima, partir.
- **Riesgo de romper main**: bajo / medio / alto. El riesgo se refiere al *blast radius* si se cuela un bug a `main`.
- **Branch naming**: `admin/<slug-corto>` (ej. `admin/sk-refactor-pr0`, `admin/empresas-pr2`). Auto-mode N2 activo para todos los `admin/*` (memoria `feedback_no_commits`).
- **Dependencias**: PRs previos que deben estar mergeados.
- Al final de cada fase hay una nota de **paralelización**.
- **Reuso primero (regla del proyecto):** cada PR indica explícitamente qué pieza hereda. Sin reuso, justificar (memoria `feedback_reutilizacion_codigo`).

> Convención: PR mergeable = build verde + tests pasando + revisión de 1 dev + cobertura del slice (unit donde aplique, integration donde haya BD/HTTP). Las migraciones EF Core deben tener un `dotnet ef migrations script` revisado a mano y adjunto al PR.

---

## Fase 0 — Refactor ADR-0035 (M)

1 PR consolidado: re-localización SharedKernel → módulos dueños + 4 nuevos DbContexts.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F-Admin-PR0 | `admin/sk-refactor-pr0` | **Mover entidades** de `SharedKernel/Domain/` a sus módulos dueños según ADR-0035: Empresa/Sucursal/Departamento → `Administracion/Domain/`; Moneda/RegimenFiscal/CondicionesPago/Incoterm/Transportista/TipoPersonaProveedor/Naturaleza/UsoPrincipal/EstatusCatalogo → `Catalogos/Domain/`; Proveedor/Articulo → `DatosMaestros/Domain/`; Almacen → `Almacen/Domain/` (módulo nuevo MVP-light, A1=b). **Splittear `CompartidoDbContext`** en `AdministracionDbContext`, `CatalogosDbContext`, `DatosMaestrosDbContext`, `AlmacenDbContext`. Schema físico permanece `compartido` por `ToTable(..., schema: "compartido")`. **Mover Application commands** (`Crear*Command` de Proveedor/Articulo, `ReclasificarNaturalezaArticulosCommand`) a `DatosMaestros/Application/`. **Reagrupar endpoints**: `Api/Endpoints/Administracion/`, `Api/Endpoints/Catalogos/`, `Api/Endpoints/DatosMaestros/`, `Api/Endpoints/Almacen/`. URLs estables. **Cambiar navegación EF** `Identidad.Usuario → Departamento` a FK por id sin navegación (`DepartamentoId : Guid?`). **Program.cs**: registrar 4 `AddDbContext<>` + `MigrationsHealthCheckOptions` para cada uno (memoria `feedback_dbcontext_nuevo_checklist`). **deploy-app-dev.yml**: agregar 4 DbContexts a la matriz de migraciones. **Tests existentes deben pasar al 100% sin cambios funcionales.** | `backend/src/{Administracion,Catalogos,DatosMaestros,Almacen}/` (estructura nueva), `backend/src/SharedKernel/Domain/` (limpieza), `backend/src/SharedKernel/Application/Catalogos/` (movidos), `backend/src/Api/Endpoints/Catalogos/` (reagrupado), `backend/src/Api/Program.cs`, `.github/workflows/deploy-app-dev.yml` | (cierre PR doc-only #1, mergeado) | M | medio (~100 archivos con imports actualizados) | `dotnet build` verde; `dotnet test` 100% pasando; `dotnet ef database update` aplica en dev sin errores; `/health/ready` verde con 4 nuevos DbContexts. |

**Paralelización Fase 0**: 1 PR. Único.

---

## Fase 1 — Andamio + contrato SettingsSchema (M)

2 PRs: SettingsSchema infra + landing/registry/gear.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F-Admin-PR1.1 | `admin/settings-schema-contract` | **Contrato `SettingsSchema`**: `SharedKernel.Application.Settings.{ISettingsSchemaProvider, SettingItem, TipoSetting, DisplayMode, ValidacionSetting}`. **Endpoints genéricos**: `Api.Endpoints.Settings.SettingsEndpoints` con `MapGroup("/api/v1/{modulo}/settings")` que resuelve provider por `{modulo}` vía `IEnumerable<ISettingsSchemaProvider>`. `GET /schema` y `PATCH /{clave}` con Idempotency-Key. **Adaptar `ComprasSettings` como exemplar**: nuevo `Compras.Application.Settings.ComprasSettingsSchemaProvider` que expone `AutoGenerarOcAlAutorizar` con `Mostrar = Custom, RutaCustom = "/compras/configuracion"`. Registrar en `Compras` Module. **Tests**: provider de Compras retorna schema esperado; PATCH valida tipos; 403 sin permiso; 422 con tipo inválido; auditoría dispara en PATCH. | `backend/src/SharedKernel/Application/Settings/*`, `backend/src/Api/Endpoints/Settings/SettingsEndpoints.cs`, `backend/src/Compras/Application/Settings/ComprasSettingsSchemaProvider.cs`, `backend/tests/Compras.IntegrationTests/Settings/*`, `backend/tests/Api.IntegrationTests/Settings/*` | F-Admin-PR0 | S | bajo (puro extension; URLs nuevas; no toca ComprasSettings existente) | `GET /api/v1/compras/settings/schema` responde con `AutoGenerarOcAlAutorizar`. PATCH con tipo bool válido OK; tipo inválido → 422. |
| F-Admin-PR1.2 | `admin/andamio-landing-gear` | **Permisos canónicos del andamio**: agregar a `PermisosCanonicos.cs` los mínimos `admin.empresas.leer`, `identidad.usuarios.leer`, `identidad.roles.leer`, `admin.auditoria.leer`. Migración seed en `Identidad`. **Endpoint smoke** `GET /api/v1/admin/smoke` con `[RequirePermission("admin.empresas.leer")]` (403/200). | `backend/src/Identidad/Domain/PermisosCanonicos.cs`, `backend/src/Identidad/Infrastructure/Migrations/<ts>_AdminPermisosCanonicos.cs`, `backend/src/Api/Endpoints/Administracion/SmokeEndpoint.cs`, `backend/tests/Api.IntegrationTests/Administracion/*` | F-Admin-PR1.1 | XS | bajo | Permisos seedeados; smoke endpoint 403/200. (Frontend del andamio vive en `07-frontend-pr-breakdown.md` UF-Admin-PR1.) |

**Paralelización Fase 1**: F-Admin-PR1.1 → F-Admin-PR1.2 secuencial.

---

## Fase 2 — Empresas + Sucursales + Departamentos (M)

3 PRs: agregado Empresa + Sucursal; agregado Departamento; CRUD endpoints + eventos.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F-Admin-PR2.1 | `admin/empresas-aggregate` | **Reusa** `Administracion.Domain.{Empresa, Sucursal}` (movidos en PR0). Enriquece `Empresa` con métodos de invariantes (`Desactivar()` valida no haya sucursales activas). Agrega `Sucursal.{Activar(), Desactivar()}`. Aggregate root pasa a controlar el ciclo de vida de Sucursal vía métodos `AgregarSucursal()`, `DesactivarSucursal()`. **Migración** aditiva: índice `ux_empresas_rfc`, índice `ux_sucursales_empresa_clave`, columna `empresas.deleted_at` si no existe. Tests unitarios. | `backend/src/Administracion/Domain/{Empresa,Sucursal}.cs`, `backend/src/Administracion/Infrastructure/Migrations/<ts>_EmpresasInvariantes.cs`, `backend/tests/Administracion.UnitTests/*` | F-Admin-PR0 | S | bajo (aditivo) | Tests pasan; índices únicos rechazan duplicados (RFC, sucursal+empresa). |
| F-Admin-PR2.2 | `admin/departamentos-aggregate` | Reusa `Administracion.Domain.Departamento` (movido en PR0). Agrega invariantes (`clave` único por empresa, no eliminar si tiene usuarios asignados — query `Identidad.Usuario` cross-módulo vía consulta separada). **Migración** índice `ux_departamentos_empresa_clave`. Tests. | `backend/src/Administracion/Domain/Departamento.cs`, `backend/src/Administracion/Infrastructure/Migrations/<ts>_DepartamentosInvariantes.cs`, `backend/tests/Administracion.UnitTests/*` | F-Admin-PR2.1 | S | bajo | Tests pasan; índice único OK; query cross-módulo a `Identidad.Usuario` funciona. |
| F-Admin-PR2.3 | `admin/empresas-departamentos-crud-endpoints` | **Commands/queries**: `CrearEmpresaCommand`, `ActualizarEmpresaCommand`, `DesactivarEmpresaCommand`, `CrearSucursalCommand`, `ActualizarSucursalCommand`, `DesactivarSucursalCommand`, `CrearDepartamentoCommand`, `ActualizarDepartamentoCommand`, `ListarEmpresasQuery` (paginado, filtro por estatus), `ObtenerEmpresaQuery` (incluye sucursales + departamentos). Validators FluentValidation. Handlers con `IIntegrationEventPublisher` publicando `EmpresaCreadaEvent` y `SucursalCreadaEvent`. **Endpoints REST** bajo `/api/v1/admin/empresas/*` + `/api/v1/admin/departamentos/*`. **Permisos canónicos**: `admin.empresas.{crear, editar, desactivar}`, `admin.empresas.sucursales.gestionar`, `admin.departamentos.gestionar`. Migración seed permisos. Tests integration. | `backend/src/Administracion/Application/Empresas/*`, `backend/src/Administracion/Application/Departamentos/*`, `backend/src/Api/Endpoints/Administracion/{EmpresasEndpoints, DepartamentosEndpoints}.cs`, `backend/src/Identidad/Infrastructure/Migrations/<ts>_AdminEmpresasPermisos.cs`, `backend/tests/Api.IntegrationTests/Administracion/*` | F-Admin-PR2.2, F-Admin-PR1.2 | M | medio (varios endpoints + eventos) | Tests integration cubren happy path + invariantes; eventos publicados verificables en Outbox. |

**Paralelización Fase 2**: PR2.1 ‖ PR2.2 paralelizables; ambos preceden a PR2.3.

---

## Fase 3 — Roles + matriz de permisos + grupos Entra ID (M)

3 PRs: extensión Rol + GrupoEntraId; CRUD endpoints + matriz permisos; seed 7 roles MVP.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F-Admin-PR3.1 | `admin/rol-grupo-entra-id` | **Extensión `Identidad.Rol`** con métodos `AsociarGrupoEntraId(string objectId)`, `DesasociarGrupoEntraId(...)`. **Nueva entidad hija `RolGrupoEntraId`** (`RolId`, `ObjectId`, `Nombre`). Tabla `identidad.rol_grupo_entra_id` con UNIQUE (`RolId`, `ObjectId`). Migración. Tests unitarios. | `backend/src/Identidad/Domain/RolGrupoEntraId.cs`, `backend/src/Identidad/Domain/Rol.cs` (extensión), `backend/src/Identidad/Infrastructure/Migrations/<ts>_RolGrupoEntraId.cs`, `backend/tests/Identidad.UnitTests/*` | F-Admin-PR0 | S | bajo (aditivo) | Migración aplica; UNIQUE rechaza duplicados; tests del agregado OK. |
| F-Admin-PR3.2 | `admin/roles-crud-endpoints` | **Commands/queries**: `CrearRolCommand`, `ActualizarRolCommand`, `EliminarRolCommand` (invariante: sin asignaciones), `AsignarPermisosARolCommand` (batch atómico, un `AuditLogEntry` consolidado), `AsociarGrupoEntraIdARolCommand`, `DesasociarGrupoEntraIdDeRolCommand`, `ListarRolesQuery` (paginado), `ObtenerRolQuery` (incluye permisos + grupos), `ListarPermisosQuery` (agrupado por módulo). **Endpoints REST** bajo `/api/v1/identidad/roles/*` + `/api/v1/identidad/permisos`. **Permisos canónicos**: `identidad.roles.{crear, editar, eliminar, asignar_permisos}`, `identidad.permisos.leer`. Migración seed permisos. **Eventos**: `RolPermisosActualizadosEvent` (Outbox). Tests integration. | `backend/src/Identidad/Application/Roles/*`, `backend/src/Identidad/Application/Permisos/*`, `backend/src/Api/Endpoints/Identidad/{RolesEndpoints, PermisosEndpoints}.cs`, `backend/src/Identidad/Infrastructure/Migrations/<ts>_IdentidadRolesPermisos.cs`, `backend/tests/Api.IntegrationTests/Identidad/*` | F-Admin-PR3.1, F-Admin-PR1.2 | M | medio | CRUD completo de roles + permisos; batch atómico con `AuditLogEntry` único; eventos publicados. |
| F-Admin-PR3.3 | `admin/seed-roles-mvp` | **Extender `BootstrapSuperAdminHostedService`** para seedear los 7 roles base (A2): Super-administrador, Admin identidad, Admin organizacional, Admin catálogos, Admin datos maestros, Auditor, Admin Compras. Cada rol con su set de permisos asociados desde `PermisosCanonicos.cs`. Idempotente (`ON CONFLICT DO NOTHING`). Tests integration. | `backend/src/Identidad/Infrastructure/BootstrapSuperAdminHostedService.cs` (extender), `backend/src/Identidad/Infrastructure/Migrations/<ts>_RolesBaseMvp.cs` (seed), `backend/tests/Identidad.IntegrationTests/Bootstrap/*` | F-Admin-PR3.2 | S | bajo | Hosted service idempotente; los 7 roles existen tras migración; auditor tiene `*.leer` cross-admin. |

**Paralelización Fase 3**: PR3.1 → PR3.2 → PR3.3 secuencial (cada uno requiere el anterior).

---

## Fase 4 — Usuarios + asignación rol×empresa (M)

2 PRs: extensión Usuario; CRUD endpoints.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F-Admin-PR4.1 | `admin/usuario-aggregate-extensions` | **Extensión `Identidad.Usuario`** con métodos `AsignarRolEnEmpresa()`, `RevocarRolEnEmpresa()`, `Desactivar()` (invariante: no puede ser el último super-admin activo). **Reusa** `UsuarioEmpresaRol` existente. **Sincronización con Entra ID en alta manual**: stub `IEntraIdResolverPort` (interfaz) + `LocalEntraIdResolverNoOp` (NoOp) — `PLATFORM-TODO(<EntraIdResolver>)` apuntando a wireup real post-MVP. Tests unitarios. | `backend/src/Identidad/Domain/Usuario.cs` (extensión), `backend/src/Identidad/Domain/Ports/IEntraIdResolverPort.cs`, `backend/src/Identidad/Infrastructure/Stubs/LocalEntraIdResolverNoOp.cs`, `backend/tests/Identidad.UnitTests/*` | F-Admin-PR3.3 | S | medio (invariante sobre super-admin) | Tests cubren happy path + invariante de no-eliminar-último-super-admin. |
| F-Admin-PR4.2 | `admin/usuarios-crud-endpoints` | **Commands/queries**: `CrearUsuarioCommand` (parámetros `Email, EntraIdObjectId?, NombreCompleto, DepartamentoId?`), `ActualizarUsuarioCommand`, `DesactivarUsuarioCommand`, `AsignarRolAUsuarioCommand`, `RevocarRolDeUsuarioCommand`, `ListarUsuariosQuery` (paginado, filtro por estatus/empresa/rol), `ObtenerUsuarioQuery` (incluye asignaciones). Validators (Email único, `EntraIdObjectId` único cuando no es null). **Endpoints REST** bajo `/api/v1/identidad/usuarios/*`. **Permisos canónicos**: `identidad.usuarios.{crear, editar, desactivar}`. Migración seed. **Eventos**: `UsuarioRolAsignadoEvent`, `UsuarioRolRevocadoEvent`. Tests integration. | `backend/src/Identidad/Application/Usuarios/*`, `backend/src/Api/Endpoints/Identidad/UsuariosEndpoints.cs`, `backend/src/Identidad/Infrastructure/Migrations/<ts>_UsuariosPermisos.cs`, `backend/tests/Api.IntegrationTests/Identidad/Usuarios/*` | F-Admin-PR4.1 | M | medio | CRUD completo; tests integration cubren asignación rol+empresa, desactivación, eventos. |

**Paralelización Fase 4**: PR4.1 → PR4.2 secuencial.

---

## Fase 4.5 — Datos Maestros UI (queries enriquecidas) (S)

1 PR: queries con filtros + endpoints admin.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F-Admin-PR4_5 | `admin/datos-maestros-queries` | **Reusa** commands existentes de Proveedor/Articulo (movidos a `DatosMaestros/Application/` en PR0). **Enriquece queries**: `ListarProveedoresQuery` con filtros (RFC, razón social, tipo persona, régimen fiscal, estatus); `ListarArticulosQuery` con filtros (código, descripción, naturaleza, unidad de medida, estatus). **Endpoints** `GET /api/v1/datos-maestros/proveedores?<filters>` y `GET /api/v1/datos-maestros/articulos?<filters>`. **Permisos canónicos**: `datos_maestros.proveedores.gestionar`, `datos_maestros.articulos.gestionar`. Migración seed permisos. Tests integration. | `backend/src/DatosMaestros/Application/Proveedores/*`, `backend/src/DatosMaestros/Application/Articulos/*`, `backend/src/Api/Endpoints/DatosMaestros/{ProveedoresEndpoints, ArticulosEndpoints}.cs`, `backend/src/Identidad/Infrastructure/Migrations/<ts>_DatosMaestrosPermisos.cs`, `backend/tests/Api.IntegrationTests/DatosMaestros/*` | F-Admin-PR0 | S | bajo (extension) | Filtros funcionan; permisos enforced; commands existentes siguen funcionando. |

**Paralelización Fase 4.5**: 1 PR. Paralelizable con PR4.2.

---

## Fase 5 — Catálogos SAT (M)

3 PRs: Moneda + tipos de cambio; catálogos editables (CondicionesPago, Incoterm, Transportista, UnidadMedida); seeds SAT inmutables (FormasPago, UsosCfdi, RegimenFiscal).

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F-Admin-PR5.1 | `admin/monedas-tipos-cambio` | **Reusa** `Catalogos.Domain.Moneda` (movida en PR0). **Nueva entidad hija `TipoCambio`** (`MonedaId, Fecha, ValorEnMxn, Origen`). Tabla `compartido.tipos_cambio` con UNIQUE (`MonedaId, Fecha`). Aggregate root `Moneda` controla ciclo de vida de `TipoCambio` via `RegistrarTipoCambio()`. **Commands/queries**: `CrearMonedaCommand`, `ActualizarMonedaCommand`, `RegistrarTipoCambioCommand`, `ListarMonedasQuery`, `ListarTiposCambioPorMonedaQuery` (paginado por fecha desc). **Endpoints REST** bajo `/api/v1/catalogos/monedas/*` + `/api/v1/catalogos/monedas/{id}/tipos-cambio`. **Permisos**: `catalogos.monedas.gestionar`, `catalogos.tipos_cambio.gestionar`. Migración + seeds (MXN, USD, EUR base). Tests. | `backend/src/Catalogos/Domain/{Moneda, TipoCambio}.cs`, `backend/src/Catalogos/Application/Monedas/*`, `backend/src/Catalogos/Infrastructure/Migrations/<ts>_MonedasYTiposCambio.cs`, `backend/src/Api/Endpoints/Catalogos/MonedasEndpoints.cs`, `backend/src/Identidad/Infrastructure/Migrations/<ts>_CatalogosMonedasPermisos.cs`, `backend/tests/Api.IntegrationTests/Catalogos/*` | F-Admin-PR0 | M | medio (modelo + entity hija) | CRUD de Moneda OK; registrar tipo de cambio inline OK; paginado por fecha funciona. |
| F-Admin-PR5.2 | `admin/catalogos-editables` | **Reusa** entidades `CondicionesPago, Incoterm, Transportista, UnidadMedida` (movidas en PR0). **Commands/queries** para cada uno: `Crear*Command`, `Actualizar*Command`, `Desactivar*Command`, `Listar*Query`. **Endpoints REST** bajo `/api/v1/catalogos/{condiciones-pago, incoterms, transportistas, unidades-medida}/*`. **Permisos**: `catalogos.{condiciones_pago, incoterms, transportistas, unidades_medida}.gestionar`. Migración seed permisos. Tests integration. | `backend/src/Catalogos/Application/{CondicionesPago, Incoterms, Transportistas, UnidadesMedida}/*`, `backend/src/Api/Endpoints/Catalogos/*`, `backend/src/Identidad/Infrastructure/Migrations/<ts>_CatalogosEditablesPermisos.cs`, `backend/tests/Api.IntegrationTests/Catalogos/*` | F-Admin-PR0 | M | bajo | CRUD funciona para los 4 catálogos; desactivar valida no haya referencias. |
| F-Admin-PR5.3 | `admin/catalogos-sat-seeds-completos` | **Reusa** entidades SAT `FormaPago, UsoCfdi, RegimenFiscal` (movidas en PR0). **Seeds completos** desde catálogos oficiales SAT: FormasPago (DOF), UsosCfdi (Anexo 20), RegimenFiscal (Anexo 24). **Endpoints REST read-only** `GET /api/v1/catalogos/{formas-pago, usos-cfdi, regimenes-fiscales}` (sin POST/PATCH). Migración aditiva con `ON CONFLICT DO NOTHING`. Tests integration. **No hay permiso CRUD**: estos catálogos son inmutables vía UI; cualquier cambio requiere nueva migración. | `backend/src/Catalogos/Infrastructure/Migrations/<ts>_CatalogosSatSeedsCompletos.cs`, `backend/src/Catalogos/Application/{FormasPago, UsosCfdi, RegimenesFiscales}/Listar*Query.cs`, `backend/src/Api/Endpoints/Catalogos/SatEndpoints.cs`, `backend/tests/Api.IntegrationTests/Catalogos/SatSeedsTests.cs` | F-Admin-PR0 | S | bajo (aditivo + idempotente) | Listar SAT retorna catálogo completo; intentar POST → 405; migración idempotente verificada. |

**Paralelización Fase 5**: PR5.1 ‖ PR5.2 ‖ PR5.3 son paralelizables (dominios independientes).

---

## Fase 6 — Series y folios (S)

2 PRs: agregado Serie + SecuenciaFolio + ReservarFolio; migración Compras OC a usar el nuevo modelo.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F-Admin-PR6.1 | `admin/series-folios-aggregate` | **Nueva agregado `Administracion.Serie`** con campos `EmpresaId, SucursalId?, TipoDocumento (enum), Prefijo, Sufijo?, ReinicioPeriodo (None | Anual | Mensual), Activa`. **Entidad hija `SecuenciaFolio`** (`SerieId, PeriodoClave, UltimoNumero`). UNIQUE (`SerieId, PeriodoClave`). **Commands/queries**: `CrearSerieCommand`, `ActualizarSerieCommand`, `DesactivarSerieCommand`, `ReservarFolioCommand` (idempotente con `Idempotency-Key`, SELECT FOR UPDATE atómico, retorna `Folio`), `ListarSeriesQuery`, `ObtenerSerieQuery` (incluye preview del próximo folio). **Endpoints REST** bajo `/api/v1/admin/series/*` + `POST /api/v1/admin/series/{id}/reservar` (consumo interno). **Permisos**: `admin.series.gestionar`. Migración. Tests unitarios + integration **con N reservas en paralelo** verificando atomicidad. | `backend/src/Administracion/Domain/{Serie, SecuenciaFolio, ReinicioPeriodo, TipoDocumento}.cs`, `backend/src/Administracion/Application/Series/*`, `backend/src/Administracion/Infrastructure/Migrations/<ts>_SeriesYFolios.cs`, `backend/src/Api/Endpoints/Administracion/SeriesEndpoints.cs`, `backend/tests/Administracion.UnitTests/*`, `backend/tests/Administracion.IntegrationTests/Concurrency/ReservarFolioTests.cs` | F-Admin-PR2.3 | S | medio (concurrencia) | CRUD de Serie OK; ReservarFolio con N reservas en paralelo no duplica folios; preview del próximo folio correcto en cada modo (None/Anual/Mensual). |
| F-Admin-PR6.2 | `admin/migrar-compras-oc-a-series` | **Migración de Compras OC** a usar `Administracion.Serie` + `ReservarFolioCommand` en lugar de su `Compras.FolioSecuencia` actual. `CrearOrdenCompraVaciaHandler` reemplaza la llamada a su secuencia local por `ReservarFolioCommand`. **Migración seed**: crear una `Serie` por cada `FolioSecuencia` actual de Compras (mapeo aditivo). **Compatibility shim**: `Compras.FolioSecuencia` queda marcada con `PLATFORM-TODO(<FolioSecuenciaDeprecate>)` pero sigue activa hasta cierre del ticket. Tests integration que verifican OC sigue generando folios correctamente con el nuevo modelo. | `backend/src/Compras/Application/Oc/CrearOrdenCompraVacia/CrearOrdenCompraVaciaHandler.cs` (refactor), `backend/src/Administracion/Infrastructure/Migrations/<ts>_MigrarComprasSeriesSeed.cs`, `backend/src/Compras/Domain/FolioSecuencia.cs` (TODO marker), `backend/tests/Api.IntegrationTests/Compras/Oc/FolioConSeriesTests.cs` | F-Admin-PR6.1 | S | medio (toca handler de Compras OC en flight) | OC sigue generando folios; OC creadas antes y después del PR coexisten correctamente. |

**Paralelización Fase 6**: PR6.1 → PR6.2 secuencial.

---

## Fase 7 — Auditoría + parámetros globales (S)

2 PRs: parámetros globales; auditoría UI consolidada.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F-Admin-PR7.1 | `admin/parametros-globales` | **Nueva entidad `ParametroGlobal`** (`Clave (PK), Valor, Tipo, Modulo?, Descripcion, UltimaActualizacion`). Tabla `compartido.parametros_globales`. **Commands/queries**: `ActualizarParametroGlobalCommand`, `ListarParametrosGlobalesQuery`. **Endpoints REST** `GET /api/v1/admin/parametros`, `PATCH /api/v1/admin/parametros/{clave}`. **Permisos**: `admin.parametros.editar`. Migración + seed con parámetros base (TimezoneDefault=America/Mexico_City, FormatoFecha=dd/MM/yyyy, RedondeoMonetario=2). Tests integration. | `backend/src/Administracion/Domain/ParametroGlobal.cs`, `backend/src/Administracion/Application/Parametros/*`, `backend/src/Administracion/Infrastructure/Migrations/<ts>_ParametrosGlobales.cs`, `backend/src/Api/Endpoints/Administracion/ParametrosEndpoints.cs`, `backend/src/Identidad/Infrastructure/Migrations/<ts>_AdminParametrosPermisos.cs`, `backend/tests/Api.IntegrationTests/Administracion/Parametros/*` | F-Admin-PR2.3 | S | bajo (aditivo) | CRUD funciona; seeds aplicados; auditoría visible. |
| F-Admin-PR7.2 | `admin/auditoria-ui-consolidada` | **Reusa** `AuditLogEntry` (ADR-0008) y `AuditSaveChangesInterceptor` activos. **Read model** `RegistroBitacora` (vista o proyección sobre `AuditLogEntry`). **Query** `ConsultarBitacoraQuery` con filtros (modulo, recurso, accion, usuarioId, empresaId, desde, hasta) + paginación server-side. **Endpoint REST** `GET /api/v1/admin/auditoria?modulo=&recurso=&accion=&desde=&hasta=&empresaId=&usuarioId=&offset=&limit=`. **Permiso**: `admin.auditoria.leer`. Tests integration verificando filtros + paginación + rango de fechas obligatorio. **Cierra `PLATFORM-TODO(<AuditUI>)` documentado en `01-diseno §12`**. | `backend/src/Administracion/Application/Auditoria/*`, `backend/src/Api/Endpoints/Administracion/AuditoriaEndpoints.cs`, `backend/tests/Api.IntegrationTests/Administracion/Auditoria/*` | F-Admin-PR7.1 | S | bajo | Bandeja paginada funciona; filtros aplican correctamente; rango de fechas obligatorio enforced; sin rango → 400 ProblemDetails. |

**Paralelización Fase 7**: PR7.1 → PR7.2 secuencial.

---

## Resumen tabular total

| Fase | PR | Sizing | Branch |
|---|---|---|---|
| 0 | F-Admin-PR0 | M | `admin/sk-refactor-pr0` |
| 1 | F-Admin-PR1.1 | S | `admin/settings-schema-contract` |
| 1 | F-Admin-PR1.2 | XS | `admin/andamio-landing-gear` |
| 2 | F-Admin-PR2.1 | S | `admin/empresas-aggregate` |
| 2 | F-Admin-PR2.2 | S | `admin/departamentos-aggregate` |
| 2 | F-Admin-PR2.3 | M | `admin/empresas-departamentos-crud-endpoints` |
| 3 | F-Admin-PR3.1 | S | `admin/rol-grupo-entra-id` |
| 3 | F-Admin-PR3.2 | M | `admin/roles-crud-endpoints` |
| 3 | F-Admin-PR3.3 | S | `admin/seed-roles-mvp` |
| 4 | F-Admin-PR4.1 | S | `admin/usuario-aggregate-extensions` |
| 4 | F-Admin-PR4.2 | M | `admin/usuarios-crud-endpoints` |
| 4.5 | F-Admin-PR4_5 | S | `admin/datos-maestros-queries` |
| 5 | F-Admin-PR5.1 | M | `admin/monedas-tipos-cambio` |
| 5 | F-Admin-PR5.2 | M | `admin/catalogos-editables` |
| 5 | F-Admin-PR5.3 | S | `admin/catalogos-sat-seeds-completos` |
| 6 | F-Admin-PR6.1 | S | `admin/series-folios-aggregate` |
| 6 | F-Admin-PR6.2 | S | `admin/migrar-compras-oc-a-series` |
| 7 | F-Admin-PR7.1 | S | `admin/parametros-globales` |
| 7 | F-Admin-PR7.2 | S | `admin/auditoria-ui-consolidada` |

**Total: 19 PRs** (5 M, 11 S, 3 XS+S compuestos). Coincide con la regla `feedback_pr_granularidad` — agrupados en S/M, sin microscópicos.

## Rev.

- **2026-05-13** — Rev. 1. PR breakdown inicial. Autor: Claude.
