# ERP - Current State

> **Documento histórico.** Este snapshot corresponde al 15/05/2026 y ya no representa el estado operativo completo del repositorio. Para continuar desarrollo, usar [docs/handoff/18-diagnostico-y-plan-reanudacion-2026-09-20.md](docs/handoff/18-diagnostico-y-plan-reanudacion-2026-09-20.md) y [docs/handoff/00-EMPIEZA-AQUI.md](docs/handoff/00-EMPIEZA-AQUI.md).

> Snapshot del estado del repositorio `Project_Millet_ERP` al 2026-05-15.
> Documento base para diseñar la integración con el **Glass Agent (PHP)** y
> con **A+W on-prem**. No prescribe diseño; sólo describe lo que existe hoy.

---

## 1. Estructura de archivos

### 1.1 Árbol del repositorio (alto nivel)

Excluye `bin/`, `obj/`, `.git/`, `node_modules/`, `dist/`, `coverage/`.

```
Project_Millet_ERP/
├── CLAUDE.md                       ← contexto para Claude Code
├── CONTRIBUTING.md
├── README.md
├── docker-compose.dev.yml          ← Postgres local para dev
├── claude-project/                 ← bundle para Claude Project
│   ├── README.md
│   ├── bundle.ps1
│   ├── instructions.md
│   └── knowledge-base.md
├── docs/
│   ├── README.md
│   ├── arquitectura.md
│   ├── onboarding-dev-seed-users.md
│   ├── api/
│   │   ├── README.md
│   │   └── openapi-v1.json         ← snapshot OpenAPI (desactualizado vs código)
│   ├── decisiones/                 ← 35 ADRs (0001..0035) + template
│   ├── integraciones/
│   │   ├── compras-eventos.md
│   │   └── compras-oc-plantillas-notif.md
│   ├── modulos/
│   │   ├── README.md
│   │   ├── administracion/         ← 00..09 (levantamiento, diseño, plan, PRs, etc.)
│   │   ├── compras-ordenes-compra/ ← 00..07
│   │   ├── compras-requisiciones/  ← 00..07
│   │   └── datos-maestros/
│   └── operacion/                  ← runbooks, planes UAT, dashboards
├── infra/                          ← IaC (Bicep)
│   ├── README.md
│   ├── main.bicep
│   ├── modules/
│   │   ├── appservice.bicep
│   │   ├── budget.bicep
│   │   ├── database.bicep
│   │   ├── keyvault.bicep
│   │   ├── keyvault-secrets.bicep
│   │   ├── monitoring.bicep
│   │   ├── monitoring-alerts.bicep
│   │   ├── network.bicep
│   │   ├── rbac.bicep
│   │   ├── registry.bicep
│   │   ├── servicebus.bicep
│   │   ├── signalr.bicep
│   │   ├── staticwebapp.bicep
│   │   ├── staticwebapp.json
│   │   └── storage.bicep
│   └── parameters/
│       ├── dev.bicepparam
│       └── prod.bicepparam
├── backend/                        ← .NET 9, monolito modular (CQRS hexagonal)
│   ├── Millet.sln
│   ├── Directory.Build.props
│   ├── Directory.Packages.props    ← Central Package Management
│   ├── BannedSymbols.txt
│   ├── nuget.config
│   ├── README.md
│   ├── src/
│   │   ├── Api/                    ← host ASP.NET, minimal API
│   │   │   ├── Millet.Api.csproj
│   │   │   ├── Program.cs
│   │   │   ├── appsettings.json
│   │   │   ├── appsettings.Development.json
│   │   │   ├── Auth/               ← AuthEndpoints, DevAuth, JwtTokenService,
│   │   │   │                         EntraTokenValidator, LoginOrchestrator,
│   │   │   │                         PermissionPolicyProvider, RequirePermission
│   │   │   ├── Endpoints/
│   │   │   │   ├── Administracion/
│   │   │   │   ├── Catalogos/
│   │   │   │   ├── Compras/
│   │   │   │   ├── DatosMaestros/
│   │   │   │   ├── Identidad/
│   │   │   │   └── Settings/
│   │   │   ├── Hubs/               ← SignalR ComprasHub + SoftLockManager
│   │   │   ├── Properties/
│   │   │   └── Web/                ← GlobalExceptionHandler, Idempotency, etc.
│   │   ├── SharedKernel/           ← BaseEntity, Money, IClock, interceptors,
│   │   │                             CoreDbContext, Outbox, Idempotency, HealthChecks
│   │   ├── Compartido/             ← CompartidoDbContext + seed catálogos
│   │   ├── Identidad/              ← Domain + IdentidadDbContext + BootstrapSuperAdmin
│   │   ├── Administracion/         ← Domain (Empresa, Sucursal, Departamento, Serie, ParametroGlobal)
│   │   ├── Catalogos/              ← Domain (Moneda, TipoCambio, Incoterm, CondicionesPago, …)
│   │   ├── DatosMaestros/          ← Domain (Proveedor, Articulo)
│   │   ├── Almacen/                ← Domain (Almacen)  — esqueleto
│   │   └── Compras/                ← Domain + Application (CQRS) + Infrastructure
│   │                                (ComprasDbContext, Migrations, Oc, Stubs, Trazabilidad)
│   └── tests/
│       ├── Administracion.UnitTests/
│       ├── Api.IntegrationTests/
│       ├── Compras.IntegrationTests/
│       ├── Compras.UnitTests/
│       ├── Identidad.UnitTests/
│       └── SharedKernel.UnitTests/
├── frontend/                       ← React 19 + Vite 8 + TanStack Router + shadcn/ui
│   ├── package.json
│   ├── vite.config.ts
│   ├── tsconfig*.json
│   ├── eslint.config.js
│   ├── vitest.config.ts
│   ├── components.json
│   ├── index.html
│   ├── public/
│   ├── docs/                       ← patrones-compras.md (exemplar UI cross-módulo)
│   └── src/                        ← rutas, componentes, hooks, stores, MSW mocks
├── tools/
│   ├── README.md
│   ├── openapi/
│   ├── perf/
│   └── setup-dev.ps1
├── .github/
│   ├── pull_request_template.md
│   └── workflows/
│       ├── deploy-app-dev.yml
│       ├── deploy-infra-dev.yml
│       ├── validate-app.yml
│       └── validate-infra.yml
├── .claude/
├── .vscode/
├── .editorconfig
└── .gitignore
```

### 1.2 Proyectos en la solution (`backend/Millet.sln`)

**Producción** (8 proyectos `Millet.*.csproj`, todos `net9.0`):

| Proyecto | Carpeta | Propósito |
|---|---|---|
| `Millet.Api` | `src/Api/` | Host ASP.NET Core minimal API. Único proceso desplegado. Registra DI, endpoints, hubs, middleware. |
| `Millet.SharedKernel` | `src/SharedKernel/` | Primitivas transversales (DDD shared kernel): `BaseEntity`, `Money`, `IClock`, excepciones de dominio, `CoreDbContext`, interceptors EF, infra de Outbox, Idempotency HTTP, HealthChecks, Logging. |
| `Millet.Compartido` | `src/Compartido/` | `CompartidoDbContext` + seed de catálogos compartidos cross-módulo (proveedores y artículos de prueba). |
| `Millet.Identidad` | `src/Identidad/` | Módulo de identidad y acceso: `Usuario`, `Rol`, `Permiso`, `UsuarioEmpresaRol`, `RolGrupoEntraId`, `IdentidadDbContext`, `BootstrapSuperAdminHostedService`, stub `IEntraIdResolverPort`. |
| `Millet.Administracion` | `src/Administracion/` | `Empresa`, `Sucursal`, `Departamento`, `Serie`, `SecuenciaFolio`, `ParametroGlobal`, `TipoDocumentoSerie`. |
| `Millet.Catalogos` | `src/Catalogos/` | Catálogos SAT y editables: `Moneda`, `TipoCambio`, `Incoterm`, `RegimenFiscal`, `CondicionesPago`, `FormaPago`, `Transportista`, `UsoCfdi`, `UsoPrincipal`, `Naturaleza`. |
| `Millet.DatosMaestros` | `src/DatosMaestros/` | `Proveedor` y `Articulo` (entidades enriquecidas con relaciones a Catálogos). |
| `Millet.Almacen` | `src/Almacen/` | Esqueleto — sólo entidad `Almacen` por ahora. |
| `Millet.Compras` | `src/Compras/` | Único módulo de negocio "completo". Submódulos **Requisiciones** y **Órdenes de Compra**. CQRS con MediatR, FluentValidation, Mapster. Contiene `ComprasDbContext`, ~24 migraciones, Outbox, stubs cross-module (almacén/OC), generador PDF QuestPDF, Azure Blob adapter, evaluator de matriz de autorización multidimensional, trazabilidad cross-doc. |

**Tests** (6 proyectos `Millet.*.{UnitTests,IntegrationTests}.csproj`):

- `Millet.SharedKernel.UnitTests`
- `Millet.Identidad.UnitTests`
- `Millet.Administracion.UnitTests`
- `Millet.Compras.UnitTests`
- `Millet.Compras.IntegrationTests`
- `Millet.Api.IntegrationTests` (usa `WebApplicationFactory<Program>`)

> La modularidad es **por carpetas/namespaces** dentro de cada módulo; un
> sólo `Api.csproj` referencia a todos. Cada módulo es dueño de su esquema
> Postgres (ADR-0005).

---

## 2. Stack y versiones

### 2.1 Runtime y plataforma

- **.NET 9** (TFM `net9.0`, definido en cada `.csproj`).
- **C#** `LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`,
  `TreatWarningsAsErrors=true`.
- **Tipo de proyecto API**: ASP.NET Core **Minimal API** sobre `Microsoft.NET.Sdk.Web`.
  No hay controllers MVC ni Razor Pages. Todos los endpoints se registran con
  `app.MapGroup` / `MapGet` / `MapPost` desde `Program.cs` y extensiones en
  `Api/Endpoints/**`.
- **ORM**: **Entity Framework Core 9.0.4** con provider **Npgsql** y
  `EFCore.NamingConventions` (snake_case). 4 DbContexts (ver §5).
- **Mediator/CQRS**: MediatR 12.4.1.
- **Validación**: FluentValidation 11.11.0 (NO Data Annotations).
- **Mapping**: Mapster 7.4.0 (NO AutoMapper).
- **Logging**: Serilog 4.2.0 (`Serilog.AspNetCore` 9.0.0) + enrichers
  (Environment, Process, Thread) + `MaskSensitivePropertiesEnricher`.
  Sink consola en local; **Azure Monitor OpenTelemetry Distro** 1.3.0 exporta
  logs/traces/métricas a Application Insights cuando hay connection string.
- **Realtime**: ASP.NET SignalR (shared framework) + `Microsoft.Azure.SignalR`
  1.27.0 como backplane gestionado en QA/Prod.
- **PDF**: QuestPDF 2024.12.3 (`QuestPdfOrdenCompraGenerator`).
- **Service Bus**: `Azure.Messaging.ServiceBus` 7.18.4 (publisher de Outbox).
- **Blob Storage**: `Azure.Storage.Blobs` 12.22.2.
- **OpenAPI**: `Microsoft.AspNetCore.OpenApi` 9.0.0 + `Scalar.AspNetCore` 2.0.16
  (UI). Sólo se montan en `Development`/`Staging`.
- **Health Checks**: `Microsoft.Extensions.Diagnostics.HealthChecks` 9.0.0
  + `AspNetCore.HealthChecks.NpgSql` 9.0.0.
- **Auth**: `Microsoft.AspNetCore.Authentication.JwtBearer` 9.0.0 +
  `Microsoft.IdentityModel.JsonWebTokens` 8.2.0 +
  `Microsoft.IdentityModel.Protocols.OpenIdConnect` 8.2.0. **Sin**
  `Microsoft.Identity.Web` (decisión explícita, ver `Directory.Packages.props`).

### 2.2 Frontend (SPA)

- **React 19.2** + **react-dom 19.2** + **TypeScript ~6.0** (devDep) + **Vite 8**.
- **Router**: `@tanstack/react-router` 1.169.
- **Data**: `@tanstack/react-query` 5.100.
- **State**: `zustand` 5.0.
- **Forms**: `react-hook-form` 7.75 + `@hookform/resolvers` + `zod` 4.4.
- **UI**: shadcn/ui sobre Radix (`@radix-ui/react-*`), `lucide-react`,
  `tailwind-merge`, `class-variance-authority`, `cmdk`, `sonner`,
  `react-day-picker`, `date-fns` 4.1 + `date-fns-tz`, `tw-animate-css`,
  Tailwind 4.2 (`@tailwindcss/vite`).
- **Auth**: `@azure/msal-browser` 5.9 + `@azure/msal-react` 5.3.
- **Realtime**: `@microsoft/signalr` 10.0.
- **Testing**: `vitest` 4.1, `@testing-library/react` 16.3, `msw` 2.14,
  `axe-core` 4.11, `jsdom` 29.

### 2.3 Top-15 paquetes NuGet (producción)

1. `Microsoft.EntityFrameworkCore` 9.0.4
2. `Microsoft.EntityFrameworkCore.Design` 9.0.4
3. `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4
4. `EFCore.NamingConventions` 9.0.0
5. `MediatR` 12.4.1
6. `FluentValidation` 11.11.0 (+ `FluentValidation.DependencyInjectionExtensions`)
7. `Mapster` 7.4.0 (+ `Mapster.DependencyInjection`)
8. `Serilog.AspNetCore` 9.0.0
9. `Azure.Monitor.OpenTelemetry.AspNetCore` 1.3.0
10. `Microsoft.AspNetCore.OpenApi` 9.0.0 + `Scalar.AspNetCore` 2.0.16
11. `Microsoft.AspNetCore.Authentication.JwtBearer` 9.0.0
12. `Microsoft.IdentityModel.JsonWebTokens` 8.2.0
13. `Microsoft.Azure.SignalR` 1.27.0
14. `Azure.Messaging.ServiceBus` 7.18.4
15. `Azure.Storage.Blobs` 12.22.2 + `QuestPDF` 2024.12.3 +
    `AspNetCore.HealthChecks.NpgSql` 9.0.0

---

## 3. Endpoints / APIs ya existentes

### 3.1 Convenciones globales

- Versionado **por path**: prefijo `/api/v1/...` para endpoints de negocio
  (ADR-0021). Excepción: `/api/auth/...` y `/api/dev/...` no versionados.
- Tags OpenAPI por área (`Auth`, `Compras`, `Catalogos`, `Identidad`, etc.).
- **Idempotency-Key** obligatoria en mutaciones marcadas con
  `[RequireIdempotencyKey]` (ADR-0020). Middleware en `Api/Web/IdempotencyMiddleware.cs`.
- **ETag/If-Match** para concurrencia optimista (ADR-0012).
- **Problem Details** RFC 7807 vía `IExceptionHandler` (ADR-0010).
- **Autorización RBAC granular**: cada endpoint declara
  `.RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.<X>)`
  (ADR-0007). El catálogo canónico vive en
  [Identidad.Domain.PermisosCanonicos](backend/src/Identidad/Domain/PermisosCanonicos.cs).

### 3.2 Swagger / OpenAPI

- **OpenAPI 3.x generado por código**: `Microsoft.AspNetCore.OpenApi` lo
  emite en `GET /openapi/v1.json`.
- **UI**: Scalar en `GET /scalar/v1`.
- **Sólo activo en Development y Staging**; en Production las rutas devuelven
  404 (ADR-0017 §"Seguridad de la spec en producción").
- Hay un snapshot **probablemente desactualizado** en
  [docs/api/openapi-v1.json](docs/api/openapi-v1.json) (sólo ~28 paths;
  el código actual expone bastantes más).

### 3.3 Lista de endpoints (extraída del código)

#### Auth (`/api/auth`, anonymous + RequireAuthorization)

| Método | Ruta | Propósito |
|---|---|---|
| POST | `/api/auth/sesion` | Intercambia un token de Entra ID por un JWT propio del API. Anonymous. |
| POST | `/api/auth/cambiar-empresa` | Re-emite el JWT con otra `current_empresa_id` (requiere asignación). |
| GET  | `/api/auth/me` | Devuelve usuario activo + empresa actual + permisos efectivos + `ComprasSettings`. |

#### Dev-only (`/api/dev/...`, sólo compilados con `#if DEBUG`)

- `MapDevAuthEndpoints()` — `fake-login` para selector dev (ADR-0015).
- `MapDevIdempotencyEndpoints()` — helpers para tests/E2E.

#### Compras — Requisiciones (`/api/v1/compras/...`)

| Método | Ruta | Propósito |
|---|---|---|
| POST   | `/api/v1/compras/requisiciones` | Crear RQ en Borrador (Idempotency-Key). |
| GET    | `/api/v1/compras/requisiciones` | Bandeja (paginado, filtros, ETag). |
| GET    | `/api/v1/compras/requisiciones/{id}` | Detalle con ETag. |
| PATCH  | `/api/v1/compras/requisiciones/{id}` | Update cabecera (If-Match). |
| POST   | `/api/v1/compras/requisiciones/{id}/transmitir` | Enviar a autorización. |
| POST   | `/api/v1/compras/requisiciones/{id}/autorizaciones` | Autorizar nivel (matriz multidimensional). |
| POST   | `/api/v1/compras/requisiciones/{id}/rechazar` | Rechazar con motivo. |
| POST   | `/api/v1/compras/requisiciones/{id}/eliminar` | Eliminar (soft) con motivo. |
| POST   | `/api/v1/compras/requisiciones/{id}/cancelar` | Cancelar (libera reservas + aborta OC borrador). |
| GET    | `/api/v1/compras/requisiciones/{id}/historico` | Timeline de eventos. |
| POST   | `/api/v1/compras/requisiciones/{id}/lineas` | Agregar línea (inline). |
| PATCH  | `/api/v1/compras/requisiciones/{id}/lineas/{lineaId}` | Editar línea. |
| PATCH  | `/api/v1/compras/requisiciones/{id}/lineas/{lineaId}/notas` | Editar notas de línea. |
| DELETE | `/api/v1/compras/requisiciones/{id}/lineas/{lineaId}` | Eliminar línea. |
| GET    | `/api/v1/compras/motivos-rechazo` | Catálogo de motivos (Rechazo/Eliminación/Cancelación). |
| GET    | `/api/v1/compras/pendientes-autorizacion` | Bandeja de RQs pendientes para el aprobador actual. |

#### Compras — Aprobadores (`/api/v1/compras/aprobadores`)

| Método | Ruta | Propósito |
|---|---|---|
| GET    | `/api/v1/compras/aprobadores` | Listar aprobadores activos. |
| POST   | `/api/v1/compras/aprobadores` | Alta. |
| DELETE | `/api/v1/compras/aprobadores/{id}` | Baja. |
| GET    | `/api/v1/compras/aprobadores/historico` | Histórico de cambios. |

#### Compras — Settings (`/api/v1/compras/configuracion`)

| Método | Ruta | Propósito |
|---|---|---|
| GET   | `/api/v1/compras/configuracion` | Config por empresa (p.ej. `AutoGenerarOcAlAutorizar`). |
| PATCH | `/api/v1/compras/configuracion` | Upsert parcial. |

#### Compras — Órdenes de Compra (`/api/v1/compras/ordenes`)

OC tiene CRUD pleno + acciones de workflow + bandejas especializadas.
Endpoints (≈30): `POST /`, `POST /{id}/duplicar`, `GET /{id}/origen`,
`GET /{id}`, `GET /{id}/pdf`, `PATCH /{id}`, `POST /{id}/lineas`,
`PATCH /{id}/lineas/{lineaId}`, `DELETE /{id}/lineas/{lineaId}`,
`PATCH /{id}/lineas/{lineaId}/texto-adicional`,
`PATCH /{id}/referencia-proveedor`, `PATCH /{id}/contacto-proveedor`,
`PATCH /{id}/informacion-logistica`, `PATCH /{id}/informacion-importacion`,
`POST /{id}/transmitir`, `POST /{id}/autorizaciones`,
`POST /desde-requisicion`, `POST /{id}/lineas/desde-requisicion`,
`GET /` (bandeja), `GET /pendientes-autorizacion`,
`GET /partidas-abiertas`, `GET /partidas-abiertas/kpis`,
`GET /{id}/historico`, `GET /{id}/duplicadas`,
`GET /requisiciones-disponibles`,
`POST /{id}/cancelar`, `POST /{id}/cancelar-con-recepciones`,
`POST /{id}/rechazar`, `POST /{id}/adjuntos`,
`DELETE /{id}/adjuntos/{adjuntoId}`, `PATCH /{id}/numero-pedimento`.

#### Compras — Artículos / Trazabilidad / catálogos OC

| Método | Ruta | Propósito |
|---|---|---|
| GET | `/api/v1/compras/articulos/{id}/historial-compras` | Historial de compras por material. |
| GET | `/api/v1/compras/trazabilidad/arbol-documentos` | Árbol cross-doc (RQ → OC → Recepción → CFDI). |
| GET | `/api/v1/compras/catalogos/tipos-documento-oc` | Catálogo de tipos de doc para OC. |

#### Catálogos compartidos (`/api/v1/catalogos/...`)

- Datos maestros legacy: `proveedores`, `articulos` (+ `articulos/reclasificar-naturaleza`).
- Organización: `sucursales`, `departamentos`, `almacenes`.
- Editables: `condiciones-pago`, `incoterms`, `transportistas`, `usos-principales`
  (cada uno: GET, POST, PATCH `/{id}`, POST `/{id}/desactivar`).
- Multimoneda: `monedas` (+ `/{id}/tipos-cambio`).
- OC: `regimenes-fiscales`, `condiciones-pago`, `usos-principales`, etc.
- SAT read-only: `formas-pago`, `usos-cfdi`.

#### Datos Maestros (`/api/v1/datos-maestros/...`)

- `GET /api/v1/datos-maestros/proveedores`, `/{id}`
- `GET /api/v1/datos-maestros/articulos`, `/{id}`

> Vista enriquecida para UI; `catalogos/...` es el set base.

#### Administración (`/api/v1/admin/...`)

| Método | Ruta | Propósito |
|---|---|---|
| GET / POST / PATCH / POST `/desactivar` | `/api/v1/admin/empresas` (+ `/{id}`, `/sucursales`, `/sucursales/{id}`) | CRUD empresas y sucursales. |
| POST / PATCH | `/api/v1/admin/departamentos` (+ `/{id}`) | Alta / patch de departamentos. |
| GET / POST / GET `/{id}` / PATCH / POST `/desactivar` / POST `/reservar` | `/api/v1/admin/series` | Series y folios (reserva atómica). |
| GET / PATCH `/{clave}` | `/api/v1/admin/parametros` | Parámetros globales del ERP. |
| GET | `/api/v1/admin/auditoria` | Consulta de auditoría. |
| GET | `/api/v1/admin/smoke` | Smoke del andamio (anonymous). |

#### Identidad (`/api/v1/identidad/...`)

| Método | Ruta | Propósito |
|---|---|---|
| GET / GET `/admin` / POST / GET `/{id}` / PATCH / POST `/desactivar` / POST `/reactivar` / POST `/asignaciones` / DELETE `/asignaciones/{x}` | `/api/v1/identidad/usuarios` | CRUD usuarios + asignación rol×empresa. |
| GET / POST / GET `/{id}` / PATCH / DELETE / PUT `/{id}/permisos` / POST `/{id}/grupos-entra-id` / DELETE `/grupos-entra-id/{x}` | `/api/v1/identidad/roles` | CRUD roles + matriz de permisos + grupos Entra. |
| GET | `/api/v1/identidad/permisos` | Catálogo canónico de permisos. |

#### Settings genérico por módulo

| Método | Ruta | Propósito |
|---|---|---|
| GET   | `/api/v1/{modulo}/settings/schema` | Lista de settings declarados por el `ISettingsSchemaProvider` del módulo. |
| PATCH | `/api/v1/{modulo}/settings/{clave}` | Update parcial. Hoy registrado: `Compras`. |

#### Realtime (SignalR)

| Hub | Ruta | Propósito |
|---|---|---|
| `ComprasHub` | `/hubs/compras` | Presencia + soft locks + eventos del módulo Compras. JWT en query param `access_token`. |

#### Operación / Diagnóstico

| Método | Ruta | Propósito |
|---|---|---|
| GET | `/` | "Hello World!" placeholder. |
| GET | `/health/live` | Liveness (anonymous, sólo proceso vivo). |
| GET | `/health/ready` | Readiness: Postgres + migraciones aplicadas + SignalR (anonymous). |
| GET | `/health` | Detalle JSON por dependencia (RBAC: `infra.health.leer`). |
| GET | `/openapi/v1.json` | Spec OpenAPI (sólo Dev/Staging). |
| GET | `/scalar/v1` | UI Scalar (sólo Dev/Staging). |

---

## 4. Autenticación y autorización

### 4.1 Esquema

- **JWT propio del API**, emitido por `JwtTokenService` (HMAC SHA-256 con
  `Auth:Jwt:SigningKey`).
- En **EntraId** (default y único modo en QA/Prod): el frontend obtiene un
  token de Entra ID con MSAL y lo intercambia en `POST /api/auth/sesion`.
  `LoginOrchestrator` valida el token Entra con `EntraTokenValidator`
  (`JsonWebTokenHandler` + `ConfigurationManager<OpenIdConnectConfiguration>`
  para JWKS), resuelve el usuario, sus permisos y emite el JWT del API.
- En **FakeForLocalDev** (sólo Development; `AuthModeValidator` falla fast en
  arranque si se intenta en Staging/Prod): endpoint `POST /api/dev/fake-login`
  emite el mismo JWT pero a partir de un selector de usuarios seed (ADR-0015).
- El **JWT del API** lleva `current_empresa_id`. `POST /api/auth/cambiar-empresa`
  re-emite el JWT con otra empresa si el usuario tiene asignación.
- Permisos efectivos se cachean en memoria (`InMemoryPermissionCache`, TTL 5 min)
  por `(userId, empresaId)`.

### 4.2 Autorización

- **RBAC granular** por endpoint, vía
  `RequireAuthorization(PermissionPolicyProvider.Prefix + <permiso canónico>)`.
- Permisos canónicos centralizados en
  [`Millet.Identidad.Domain.PermisosCanonicos`](backend/src/Identidad/Domain/PermisosCanonicos.cs).
- Modelo en BD: `Rol` ←→ `RolPermiso` →`Permiso`; `Usuario` se asigna a un
  `(Empresa, Rol)` vía `UsuarioEmpresaRol`. Opcionalmente `Rol` puede mapearse
  a uno o más grupos de Entra ID (`RolGrupoEntraId`).
- **Multi-empresa** (ADR-0011): toda entidad de negocio lleva `EmpresaId`;
  `EmpresaContextSaveChangesInterceptor` lo setea desde `ICurrentEmpresaContext`.

### 4.3 Dónde se configura

- Wiring DI: `AddMilletAuth(builder.Configuration)` en
  [Program.cs:247](backend/src/Api/Program.cs#L247) + `Auth/AuthExtensions.cs`.
- Endpoints: `Auth/AuthEndpoints.cs` + `Auth/DevAuthEndpoints.cs`.
- Options: `Auth/Options/*` (bindeadas a sección `Auth:`).
- Validación de modo: `Auth/AuthModeValidator` (fail-fast en arranque).
- Bootstrap del primer SuperAdmin en cada ambiente:
  `Identidad/Infrastructure/BootstrapSuperAdminHostedService.cs` (ADR-0007).
- Stub temporal del resolver Entra→Usuario:
  `Identidad/Infrastructure/Stubs/LocalEntraIdResolverNoOp.cs`
  (`PLATFORM-TODO(<EntraIdResolver>)`).

---

## 5. Base de datos

### 5.1 Motor y conexión

- **PostgreSQL 16** (siempre; ni SQL Server, ni Mongo, ni otros).
- **Local dev**: contenedor Docker `postgres:16-alpine` (ver
  [docker-compose.dev.yml](docker-compose.dev.yml)); usuario `pgadmin`,
  password `pgadmin`, DB `millet_dev` en `localhost:5432`.
- **Azure**: **Azure Database for PostgreSQL Flexible Server**,
  `psql-millet-{env}-mxc-01` (definido en
  [infra/modules/database.bicep](infra/modules/database.bicep)).
- Cadena de conexión:
  - **Local**: `ConnectionStrings:Postgres` en
    [appsettings.Development.json](backend/src/Api/appsettings.Development.json).
  - **QA/Prod**: app setting `ConnectionStrings__Postgres` en App Service
    apunta a `@Microsoft.KeyVault(VaultName=...;SecretName=postgres-connection-string)`
    — el valor real lo setea el operador en Key Vault.
- Convención EF: snake_case (`UseSnakeCaseNamingConvention()`).
- Interceptors aplicados a todos los DbContexts:
  `MetadataSaveChangesInterceptor`, `EmpresaContextSaveChangesInterceptor`,
  `AuditSaveChangesInterceptor`. `ComprasDbContext` agrega además
  `OutboxSaveChangesInterceptor` (Outbox pattern, ADR-0009).

### 5.2 DbContexts (ADR-0030 — multi-DbContext por módulo)

| DbContext | Proyecto | Esquema(s) | Migraciones |
|---|---|---|---|
| `CompartidoDbContext` | `Millet.Compartido` | `compartido` | `src/Compartido/Infrastructure/Migrations/Compartido/` |
| `CoreDbContext` | `Millet.SharedKernel` | `core` (idempotency, etc.) | `src/SharedKernel/Infrastructure/Persistence/Migrations/` |
| `IdentidadDbContext` | `Millet.Identidad` | `identidad` | 17 migraciones (2026-05-03 → 2026-05-14) |
| `ComprasDbContext` | `Millet.Compras` | `compras` (+ `compras.integration_events_outbox`) | 24 migraciones (2026-05-07 → 2026-05-13) |

Migraciones **activas**: el pipeline `deploy-app-dev.yml` aplica las 4 con
`dotnet ef database update` antes del deploy de la app. `MigrationsAppliedHealthCheck`
hace que `/health/ready` falle si quedan pendientes.

### 5.3 Tablas / entidades (sin esquema detallado)

**SharedKernel / Compartido (transversal):**
- `Idempotency` keys (HTTP idempotency), Outbox infra, auditoría, presencia.

**Identidad (`identidad.*`):**
- `Usuario`, `Rol`, `Permiso`, `RolPermiso`, `UsuarioEmpresaRol`,
  `UsuarioPreferencia`, `RolGrupoEntraId`, `RestriccionRol`.

**Administración (`compartido.*` físico):**
- `Empresa`, `Sucursal`, `Departamento`, `Serie`, `TipoDocumentoSerie`,
  `SecuenciaFolio`, `ReinicioPeriodo`, `ParametroGlobal`, `TipoParametro`.

**Catálogos:**
- SAT (read-only): `FormaPago`, `UsoCfdi`, `RegimenFiscal`.
- Editables: `Moneda`, `TipoCambio`, `OrigenTipoCambio`, `Incoterm`,
  `CondicionesPago`, `Transportista`, `UsoPrincipal`, `Naturaleza`,
  `TipoPersonaProveedor`, `AplicaTipoPersona`, `EstatusCatalogo`.

**Datos Maestros (`compartido.*`):**
- `Proveedor`, `Articulo`.

**Almacén (esqueleto):**
- `Almacen`.

**Compras (`compras.*`):**
- Cabecera: `Requisicion`, `LineaRequisicion`, `Autorizacion`,
  `MotivoRechazo`, `MotivoRechazoAplicaA`, `RolAprobador`,
  `AprobadorDepartamento`, `UmbralAprobacionDepartamento`,
  `Clasificacion`, `EstadoRequisicion`, `NivelAutorizacion`, `Prioridad`,
  `Cubrimiento`, `CubrimientoLinea`, `Folio`, `FolioSecuencia`,
  `ComprasSettings`.
- OC: entidades en `Compras/Domain/Oc/*` (orden cabecera, líneas,
  adjuntos, autorizaciones OC, regímenes fiscales, sub-estados, PDF).
- Outbox: `integration_events_outbox` (publicado por
  `OutboxPublisherWorker<ComprasDbContext>` a Azure Service Bus).
- Trazabilidad: árbol cross-doc, historial de compras por material.

---

## 6. Infraestructura Azure

### 6.1 Provisioning

- **Bicep** (no Terraform, no ARM, no `az cli` imperativo).
- Punto de entrada: [infra/main.bicep](infra/main.bicep) (scope = suscripción).
- Parámetros por ambiente:
  [infra/parameters/dev.bicepparam](infra/parameters/dev.bicepparam),
  [infra/parameters/prod.bicepparam](infra/parameters/prod.bicepparam).
- Convención de nombres: `{tipo}-millet-{env}-mxc-01`. Región default:
  `mexicocentral` (con excepciones documentadas).

### 6.2 Recursos definidos

Todos viven en el resource group `rg-millet-{env}-mxc-01`:

| Módulo Bicep | Recurso | Nombre (dev) | Notas |
|---|---|---|---|
| `network.bicep` | VNet | `vnet-millet-dev-mxc-01` | — |
| `monitoring.bicep` | Log Analytics + Application Insights | `log-...-01`, `appi-...-01` | Daily cap 1GB en dev. |
| `keyvault.bicep` | Key Vault | `kv-millet-dev-mxc-01` | Purge protection sólo en prod. |
| `keyvault-secrets.bicep` | Secrets `service-bus-connection-string`, `signalr-connection-string` | — | Otros secretos (`postgres-connection-string`, `auth-jwt-signing-key`, `auth-initial-admin-oid`, `auth-entra-tenant-id`, `auth-entra-api-client-id`, `auth-entra-api-audience`, `auth-entra-spa-client-id`, `swa-deployment-token`) los setea el operador con `az keyvault secret set`. |
| `storage.bicep` | Storage Account (LRS dev / GRS prod) | `stmilletdevmxc01` | Sin guiones (constraint de Azure). |
| `registry.bicep` | Azure Container Registry (Basic dev / Premium prod) | `crmilletdevmxc01` | — |
| `database.bicep` | PostgreSQL Flexible Server | `psql-millet-dev-mxc-01` | `Standard_B1ms` (Burstable) en dev / `Standard_D2ds_v5` (GeneralPurpose) en prod. HA sólo en prod. Admin Entra group + admin local `pgadmin`. |
| `servicebus.bicep` | Service Bus Namespace (Standard) | `sb-millet-dev-mxc-01` | Connection string en KV. |
| `signalr.bicep` | Azure SignalR Service | `sigr-millet-dev-mxc-01` (en `southcentralus`) | mexicocentral no soporta SignalR (ADR-0001). Free F1 en dev, Standard S1 en prod. |
| `staticwebapp.bicep` | Azure Static Web App (Free) | `swa-millet-dev-mxc-01` (en `centralus`) | Aloja el SPA. |
| `appservice.bicep` | App Service Plan + Web App (Linux .NET 9) | plan `plan-millet-dev-mxc-01`, app `app-millet-dev-mxc-01` | B1 en dev / P1v3 en prod. `healthCheckPath=/health/ready`. Managed identity con `Key Vault Secrets User`. CORS poblado con hostname del SWA. |
| `rbac.bicep` | Role assignments | — | Admins → Owner, Developers → Contributor. |
| `budget.bicep` | Cost Management Budget | `budget-millet-dev` | Alerta a `eduardo.paredes@tiglass.net`. |
| `monitoring-alerts.bicep` | Action Group + alertas SignalR | — | Email al owner; métricas `SystemErrors` y `ServerLoad`. |

App Settings principales del App Service (definidos en
[infra/modules/appservice.bicep](infra/modules/appservice.bicep)):

- `WEBSITES_ENABLE_APP_SERVICE_STORAGE=false`
- `ASPNETCORE_ENVIRONMENT` (`Development` | `Staging` | `Production`)
- `APPLICATIONINSIGHTS_CONNECTION_STRING` (lee Azure Monitor OTel directamente)
- `ConnectionStrings__Postgres` → `@Microsoft.KeyVault(...postgres-connection-string)`
- `ServiceBus__ConnectionString` → KV ref
- `SignalR__ConnectionString` → KV ref
- `Auth__Jwt__SigningKey` → KV ref
- `Auth__InitialAdminEntraOid` → KV ref
- `Auth__EntraId__{TenantId,ClientId,Audience}` → KV refs
- `Auth__Bootstrap__EmpresaInicial__{Rfc,RazonSocial,RegimenFiscal,NombreComercial}` (valores directos)
- `Cors__AllowedOrigins__{N}` (poblado con hostname SWA)

---

## 7. Configuración

### 7.1 Settings en `backend/src/Api/appsettings.json`

- `Logging.LogLevel` (Default `Information`, AspNetCore `Warning`).
- `Serilog.MinimumLevel` (Default `Information`; overrides por namespace).
- `Auth.Mode` = `EntraId`.
- `Auth.Jwt.{Issuer,Audience,AccessTokenLifetimeMinutes}` (sin `SigningKey` en
  el archivo — viene de KV).
- `Auth.EntraId.{TenantId,ClientId,Audience}` (vacíos, vienen de KV).
- `Auth.InitialAdminEntraOid` (vacío, viene de KV).
- `Auth.Bootstrap.EmpresaInicial.{Rfc,RazonSocial,RegimenFiscal,NombreComercial}`
  (vacíos en este archivo; valores reales vía Bicep app settings).
- `Cors.AllowedOrigins` (array vacío por default).
- `AllowedHosts` = `*`.

### 7.2 Settings en `appsettings.Development.json` (público por diseño)

- `Auth.Mode` = `FakeForLocalDev`.
- `Auth.Jwt.SigningKey` = clave dev hardcodeada (NO se usa fuera de Dev).
- `Auth.InitialAdminEntraOid` = `dev-superadmin`.
- `Auth.Bootstrap.EmpresaInicial` = `MID010101AAA / Millet ERP - Empresa Inicial Dev / 601 / Millet Dev`.
- `ConnectionStrings.Postgres` = `Host=localhost;Port=5432;Database=millet_dev;Username=pgadmin;Password=pgadmin`.
- `Cors.AllowedOrigins` = `[ "http://localhost:5173" ]`.
- `Compras.UseStubs` = `true`; `Compras.Stubs.Stock.DefaultRatio` = `0.0`.

### 7.3 Variables de entorno esperadas en App Service

(Misma lista que §6.2 al final.) Adicionalmente, secciones `Compras:Oc:BlobStorage:ConnectionString`
y `Outbox.ServiceBusConnectionString` se leen condicionalmente: si la
connection string está presente se usa el adapter real (Azure Blob / Service Bus),
si está vacía se cae a stubs (`LocalFilesystemBlobStub`, `NoOpIntegrationEventBusSender`).

### 7.4 Frontend (`VITE_*`)

Inyectadas en build time por GitHub Actions (`deploy-app-dev.yml` los lee de KV):

- `VITE_AUTH_MODE` (`EntraId`)
- `VITE_API_BASE_URL` (`https://app-millet-dev-mxc-01.azurewebsites.net`)
- `VITE_ENTRA_TENANT_ID`
- `VITE_ENTRA_CLIENT_ID` (SPA client id)
- `VITE_API_AUDIENCE` (`api://<api-client-id>/access_as_user`)

### 7.5 Key Vault references (resumen)

Sintaxis: `@Microsoft.KeyVault(VaultName=<kv>;SecretName=<name>)`.

Secretos consumidos hoy: `postgres-connection-string`,
`service-bus-connection-string`, `signalr-connection-string`,
`auth-jwt-signing-key`, `auth-initial-admin-oid`, `auth-entra-tenant-id`,
`auth-entra-api-client-id`, `auth-entra-api-audience`,
`auth-entra-spa-client-id` (frontend, vía workflow), `swa-deployment-token`
(frontend deploy).

---

## 8. CLAUDE.md actual (literal)

```markdown
# CLAUDE.md — Contexto para Claude Code

Este archivo orienta a Claude Code en futuras sesiones sobre este monorepo.
Léelo completo antes de proponer cambios significativos.

---

## Qué es el proyecto

**Millet ERP** es un sistema ERP back-office interno para **Millet**, empresa
mexicana de vidrio de valor agregado. Reemplaza progresivamente las funciones
de SAP en las áreas de back-office: facturación CFDI, cuentas por cobrar,
compras de no-producción, almacén de no-producción, cuentas por pagar,
activos fijos, contabilidad y reportes/BI.

El sistema **no** maneja la operación comercial ni de producción; eso lo
sigue haciendo el sistema externo **A+W**, que se integra con este ERP. Un
sistema externo de **requisiciones** también se integra (módulo de compras).

Owner del proyecto: **Eduardo Paredes** — `eduardo.paredes@tiglass.net`.

---

## Decisiones arquitectónicas

- **Monolito modular** desplegado como una sola unidad, organizado en
  módulos independientes con fronteras claras.
- **Arquitectura hexagonal (puertos y adaptadores)** dentro de cada módulo,
  con **CQRS** para separar comandos y consultas.
- **MediatR** como mediador para casos de uso (commands/queries/handlers).
- **PostgreSQL único** (Flexible Server en Azure) con **esquemas separados
  por módulo**; cada módulo es dueño de su esquema y nadie más escribe en él.
- **Azure Service Bus** para integración asíncrona entre módulos y con
  sistemas externos (A+W, requisiciones).
- **Microsoft Entra ID** para identidad y autorización; **no hay Active
  Directory local** ni federación con AD on-premises.
- **Strangler Fig** como estrategia de reemplazo de SAP: módulo por módulo,
  sin un corte total. SAP sigue operando en paralelo hasta que cada módulo
  del ERP esté listo y validado.

---

## Módulos del back-office (10)

1. **Identidad y acceso** — transversal; gestión de usuarios, roles y
   permisos sobre Entra ID.
2. **Integración A+W** — consume facturación, inventario y datos comerciales
   del sistema externo A+W.
3. **Facturación** — emisión de CFDI 4.0, timbrado, cancelación, notas de
   crédito.
4. **Cuentas por Cobrar** — cartera de clientes, cobranza, aplicación de
   pagos, antigüedad de saldos.
5. **Compras** — compras de no-producción; integra con el sistema externo
   de requisiciones; órdenes de compra, recepciones.
6. **Almacén de no-producción** — inventario de consumibles e insumos
   indirectos (no incluye inventario productivo, eso lo lleva A+W).
7. **Cuentas por Pagar** — proveedores, programación de pagos, conciliación.
8. **Activos Fijos** — alta, depreciación, bajas, reporte fiscal.
9. **Contabilidad** — plan de cuentas, pólizas, cierre mensual, balanza,
   estados financieros.
10. **Reportes y BI** — reportes operativos y financieros, dashboards,
    integración con herramientas de BI.

---

## Convenciones de código C#

- **Nullable reference types** habilitados en todos los proyectos.
- **`async`/`await`** siempre que haya I/O (DB, HTTP, Service Bus, archivos).
  Nada de `.Result` ni `.Wait()`.
- **`record`** para DTOs y value objects (inmutables por defecto).
- **`sealed`** por defecto en clases concretas; abrir herencia solo cuando
  haya razón explícita.
- **FluentValidation** para validación de comandos y queries —
  **NO** Data Annotations.
- **Mapster** para mapeo entre DTOs/entidades — **NO** AutoMapper.
- **Serilog** para logging estructurado, con sinks a Application Insights
  en Azure y consola en local.

---

## Convenciones de Bicep / Azure

- **Patrón de nombres:** `{tipo}-{proyecto}-{ambiente}-{región}-{secuencia}`
  - Ejemplo: `rg-millet-dev-mxc-01`, `psql-millet-prod-mxc-01`.
  - `tipo`: prefijo del recurso (`rg`, `psql`, `kv`, `st`, `sb`, etc.).
  - `proyecto`: `millet`.
  - `ambiente`: `dev`, `qa`, `prod`.
  - `región`: `mxc` (México Central).
  - `secuencia`: `01`, `02`, ...
- **Storage Accounts:** solo letras minúsculas y dígitos (Azure no permite
  guiones ni mayúsculas en el nombre).
- **Tags consistentes** en todos los recursos:
  `project=millet`, `environment={dev|qa|prod}`, `owner=eduardo.paredes@tiglass.net`,
  `costCenter`, `managedBy=bicep`.
- **Parametrización por ambiente** en archivos `.bicepparam` separados
  (`infra/parameters/dev.bicepparam`, `prod.bicepparam`, etc.). Nunca
  valores hardcodeados específicos de ambiente en los módulos.
- **Secretos jamás** en código ni en parámetros — usar **Azure Key Vault**
  con referencias `getSecret()` desde Bicep.

---

## Comandos comunes de despliegue

Todos los comandos asumen que estás autenticado con `az login` y que la
suscripción correcta está seleccionada (`az account set --subscription <id>`).

**Validar plantilla (sintaxis y referencias):**
```bash
az deployment sub validate \
  --location mexicocentral \
  --template-file infra/main.bicep \
  --parameters infra/parameters/dev.bicepparam
```

**What-if (previsualizar cambios sin aplicarlos):**
```bash
az deployment sub what-if \
  --location mexicocentral \
  --template-file infra/main.bicep \
  --parameters infra/parameters/dev.bicepparam
```

**Deploy real:**
```bash
az deployment sub create \
  --location mexicocentral \
  --template-file infra/main.bicep \
  --parameters infra/parameters/dev.bicepparam
```

**Regla:** SIEMPRE correr `what-if` antes de `create` en cualquier ambiente
distinto a un sandbox personal. En `prod` es obligatorio revisar el output
con un segundo par de ojos.

---

## Deuda de plataforma y stubs `NoOp`

Cuando un módulo de negocio se construye antes de que cierta
infraestructura compartida exista (`CollaborationHub` SignalR, Outbox,
motor de notificaciones, etc.), usa una implementación temporal `NoOp`
o stub. Para que esa deuda no quede invisible:

1. **Sección "Dependencias de plataforma pendientes"** en el documento
   de diseño del módulo (`docs/modulos/<modulo>/01-diseno.md`),
   con tabla *Pieza · Ticket · NoOp en uso · Cómo se wirea*.
2. **Comentario `// PLATFORM-TODO(<identificador>): ...`** en cada
   `NoOp` o stub temporal del código. Buscable con
   `rg "PLATFORM-TODO" backend/src`. El `<identificador>` es el ID
   del sistema de tickets si existe (`MIL-1234`, `#42`); si no, un
   nombre descriptivo entre `<>` consistente con la tabla del módulo
   (`<CollaborationHub>`, `<Outbox>`).
3. **Checklist "Módulos consumidores"** en el ticket de plataforma. El
   ticket no se cierra hasta que todos los módulos del checklist están
   wired.

Detalle, plantillas y proceso de cierre en **ADR-0031**.

Esta convención aplica a **todos los módulos** del back-office desde el
primero (Compras / Requisiciones).

---

## Patrones de UI del frontend (cross-módulo)

El módulo Compras Requisiciones es el **exemplar** de los patrones de UI
del back-office. La estructura de ventanas, navegación y forms se
documenta en
[`frontend/docs/patrones-compras.md`](frontend/docs/patrones-compras.md)
y debe replicarse en cada módulo nuevo (CxC, OC, CxP, Activos Fijos,
Contabilidad, BI). Resumen:

- **Master-detail** con lista compacta 320px sticky a la izquierda +
  panel detalle a la derecha; mobile drill-down. Rutas
  `/<modulo>/<recurso>` (bandeja tabular) + `/<modulo>/<recurso>/$id`
  (master-detail).
- **Sheet (slide-from-right)** para forms de "Nueva ..."; provider a
  nivel shell con `useNuevaXxx().abrir()`; confirm al cerrar con
  `isDirty`; `Force: true` en success.
- **Inline forms** (sin modal) para editar/agregar items de un master:
  border dashed primary (agregar) vs amber (editar).
- **Topbar global**: search contextual por ruta con debounce 200ms,
  Quick Create popover por módulo, ayuda contextual.
- **Sub-topbar** del detalle sticky con `data-print="hidden"`; aside
  master también `data-print="hidden"` para impresión limpia.

Cuando llegue el siguiente módulo, abre primero esa doc y copia el
patrón del más cercano a tu caso (P1 = bandeja general, P2 = bandeja
filtrada server-side, P3 = detalle, P4 = nueva con sheet). Las
diferencias entre módulos viven en el query/schema/columnas, no en la
estructura de ventanas.

---

## Notas para Claude Code

- **Abre VS Code en la raíz del monorepo**, no en una subcarpeta. Si lo
  abres en `infra/` o `backend/`, los analizadores y configuraciones del
  monorepo no funcionan correctamente.
- **Antes de cambios grandes**, revisa el `README.md` de la subcarpeta
  correspondiente (`infra/README.md`, `backend/README.md`, etc.). Cada
  subcarpeta tiene su propio contexto y prerequisitos.
- **Para `infra/`:** SIEMPRE validar con `what-if` antes de aplicar
  cualquier cambio. Nunca hagas `az deployment sub create` directo sin
  haber visto el what-if primero.
- **Secretos JAMÁS en código.** Ni en archivos `.bicepparam`, ni en
  `appsettings.json`, ni en commits. Si necesitas un secreto, va a
  **Azure Key Vault** y se referencia desde ahí.
- **No commitear sin permiso explícito** del owner. Por defecto, después
  de hacer cambios, reporta qué hiciste y espera instrucción para commitear.
- **Idioma:** documentación, comentarios de negocio y nombres de módulos
  en **español**. Código, comandos y nombres técnicos en inglés.
- **Convenciones de Git:** ramas con prefijo `feature/`, `fix/`, `chore/`,
  `docs/`. Nunca trabajar directo en `main`.
```

---

## 9. CI/CD

### 9.1 Plataforma

**GitHub Actions** (no Azure DevOps, no Jenkins). Workflows en
[.github/workflows/](.github/workflows/):

| Workflow | Trigger | Propósito |
|---|---|---|
| `validate-app.yml` | PR (todos) | Job `changes` (paths-filter) + lint + build + tests (.NET y frontend); los jobs de cada lado se saltan (`skipped` = success para branch protection) si el PR no tocó ese lado. |
| `validate-infra.yml` | PR / push (cambios en `infra/`) | `bicep build` + `az deployment sub what-if` contra dev. |
| `deploy-infra-dev.yml` | manual (workflow_dispatch) | `az deployment sub create` con `dev.bicepparam`. |
| `deploy-app-dev.yml` | push a `main` con cambios en `backend/` o `frontend/` | Pipeline completo de despliegue a dev. |

### 9.2 `deploy-app-dev.yml` (resumen)

Targets (env vars del workflow):

- `APP_SERVICE_NAME=app-millet-dev-mxc-01`
- `STATIC_WEB_APP_NAME=swa-millet-dev-mxc-01`
- `KEY_VAULT_NAME=kv-millet-dev-mxc-01`

Jobs (en orden):

1. **`build-and-publish-backend`** — `dotnet publish` del API → artifact `backend-publish`.
2. **`build-frontend`** — `tsc -b && vite build` → artifact `frontend-dist` (sin VITE_* reales).
3. **`run-migrations`** (depende de backend) — `az login` OIDC, obtiene
   connection string Postgres de KV, restaura `dotnet-ef`, aplica las 4
   migraciones (`CompartidoDbContext`, `CoreDbContext`, `IdentidadDbContext`,
   `ComprasDbContext`).
4. **`deploy-backend`** (depende de migrations) — `azure/webapps-deploy@v3`.
5. **`deploy-frontend`** (depende sólo de build-frontend) — re-buildea con
   `VITE_*` leídos de KV, sube a SWA con `Azure/static-web-apps-deploy@v1`.
6. **`smoke-tests`** (depende de deploy-backend) — `curl` con retries a
   `/health/live`, `/health/ready`, `/`.

Autenticación a Azure: **OIDC federated credentials** (sin secretos).
`vars.AZURE_CLIENT_ID`, `vars.AZURE_TENANT_ID`, `vars.AZURE_SUBSCRIPTION_ID`.

**Entornos**: sólo `dev` por ahora. **QA y Prod aún no tienen workflows**
de deploy; los `prod.bicepparam` existen pero el pipeline está pendiente.

---

## 10. Pendientes documentados

### 10.1 Módulos de negocio (de `docs/modulos/README.md`)

De los 10 módulos del back-office, **3 tienen documentación 00→02 completa
y código**:

- ✅ **Compras — Requisiciones** (00→07, código en `backend/src/Compras`).
- ✅ **Compras — Órdenes de Compra** (00→07, código en `backend/src/Compras/.../Oc`).
- ✅ **Administración** (00→09, código en `backend/src/Administracion`,
  `Identidad`, `Catalogos`, `DatosMaestros`, `Compartido`).

**Pendientes (sin levantamiento, sin diseño, sin plan, sin código):**

1. Integración **A+W** — el nexo con el sistema productivo on-prem.
2. **Facturación** (CFDI 4.0).
3. **Cuentas por Cobrar**.
4. **Almacén de no-producción**.
5. **Cuentas por Pagar**.
6. **Activos Fijos**.
7. **Contabilidad**.
8. **Reportes y BI**.

Además, el submódulo de **Recepciones** dentro de Compras está pendiente
(hoy hay `cancelar-con-recepciones` que es defensivo, no un flujo completo).

### 10.2 Deuda de plataforma y stubs `PLATFORM-TODO`

Convención ADR-0031: cualquier stub `NoOp` o adapter incompleto se marca con
`// PLATFORM-TODO(<id>): ...` en el código, y se documenta en
"Dependencias de plataforma pendientes" del diseño del módulo. Casos
identificables hoy:

- **`<EntraIdResolver>`** — `LocalEntraIdResolverNoOp` devuelve `null` y deja
  al handler de `CrearUsuario` caer al placeholder `dev-{email}`. Wiring real
  a Microsoft Graph es post-MVP. Ver
  [LocalEntraIdResolverNoOp.cs](backend/src/Identidad/Infrastructure/Stubs/LocalEntraIdResolverNoOp.cs).
- **`<StubsTeardown>`** — bloque `Compras.UseStubs=true` en
  `appsettings.Development.json` activa 5 `InMemory*` stubs cross-module
  (almacén/OC) porque los adapters reales no existen aún. Cuando Almacén
  y otros submódulos lleguen, hay que retirar el bloque y borrar los stubs.
- **Outbox publisher** vs **Service Bus real** — si
  `Outbox.ServiceBusConnectionString` está vacío, se usa
  `NoOpIntegrationEventBusSender` (no se publica nada). En QA/Prod la cs
  viene de KV.
- **Blob Storage** — si `Compras:Oc:BlobStorage:ConnectionString` está vacío,
  se usa `LocalFilesystemBlobStub` (PDFs y adjuntos al filesystem del host).

### 10.3 README.md y otros docs

- **README.md raíz**: describe propósito, stack, estructura, setup local.
  No menciona pendientes concretos.
- **backend/README.md**: está **desactualizado** — describe estado de
  "Fase 1: scaffolding" y dice que no hay módulos de negocio implementados.
  La realidad actual incluye Compras Requisiciones + OC + Admin completos.
- **CONTRIBUTING.md**: convenciones de PR/ramas.
- **No existen** archivos `TODO.md`, `NOTES.md`, `CHANGELOG.md` o
  `ROADMAP.md` en la raíz.
- `docs/operacion/collaboration-hub-status.md`: estado del CollaborationHub
  SignalR (Sprints 1, 2, 3 — soft locks, presencia, métricas).
- ADRs activos: 35 documentos en `docs/decisiones/` (0001..0035). Los más
  relevantes para integración externa: **0001 SignalR**, **0003 Entra ID**,
  **0007 RBAC granular**, **0009 Outbox**, **0020 Idempotencia HTTP**,
  **0021 Versionado API**, **0027 Integración PAC OneFactura**, **0030
  multi-DbContext**, **0031 stubs NoOp**.

### 10.4 Lo que NO existe en el repo (relevante para Glass Agent / A+W)

- No hay módulo, endpoint o adapter para **A+W** todavía (sólo se menciona
  como sistema externo en CLAUDE.md/README.md).
- No hay módulo, endpoint o adapter para un **Glass Agent en PHP** —
  no se menciona en ningún ADR, doc, ni código hoy.
- No hay tablas de **mapping** Millet ↔ A+W, ni de **catálogo de eventos
  de integración** publicado al exterior. La integración por Service Bus
  (`compras.integration_events_outbox`) sólo emite eventos internos del
  módulo Compras documentados en
  [docs/integraciones/compras-eventos.md](docs/integraciones/compras-eventos.md).

---

_Documento generado por inspección estática del repo el 2026-05-15. No se
modificó código fuente._
