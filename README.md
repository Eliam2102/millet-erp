# Millet ERP

Sistema ERP back-office para **Millet**, empresa mexicana de vidrio de valor
agregado. Este repositorio es el monorepo que aloja el código del producto:
infraestructura, backend, frontend, herramientas y documentación.

---

## Qué es este proyecto

Millet ERP es un sistema interno que reemplaza progresivamente las funciones
de SAP en las áreas de back-office de la empresa: facturación CFDI, cuentas
por cobrar, compras de no-producción, almacén de no-producción, cuentas por
pagar, activos fijos, contabilidad y reportes/BI.

El reemplazo se hace siguiendo el patrón **Strangler Fig**: módulo por módulo,
sin un corte total con SAP, hasta que el ERP cubra todas las funciones de
back-office del alcance acordado.

El sistema **no** maneja la operación comercial ni de producción de la
fábrica; eso lo seguirá haciendo el sistema externo **A+W**, que se integra
con este ERP para alimentar facturación, almacén y otros módulos. También
existe un sistema externo de **requisiciones** que se integra para alimentar
el módulo de compras.

---

## Estructura del repositorio

| Carpeta | Contenido |
|---|---|
| [`infra/`](./infra/) | Infrastructure as Code en Bicep para los ambientes de Azure (dev, qa, prod). |
| [`backend/`](./backend/) | API en .NET 9 con arquitectura hexagonal y CQRS por módulo. |
| [`frontend/`](./frontend/) | SPA en React 18 + TypeScript + Vite + shadcn/ui. |
| [`docs/`](./docs/) | Documentación del proyecto, decisiones arquitectónicas (ADRs) y levantamientos por módulo. |
| [`tools/`](./tools/) | Scripts y utilidades del proyecto. |

Cada subcarpeta tiene su propio `README.md` con detalles específicos.

---

## Stack tecnológico

- **Backend:** .NET 9, C# con nullable reference types, MediatR (CQRS),
  FluentValidation, Mapster, Serilog, Entity Framework Core.
- **Frontend:** React 18, TypeScript, Vite, shadcn/ui.
- **Base de datos:** PostgreSQL 16 (Flexible Server en Azure), un solo motor
  con esquemas separados por módulo.
- **Mensajería e integración asíncrona:** Azure Service Bus.
- **Identidad:** Microsoft Entra ID (sin Active Directory local).
- **Cloud:** Azure, región principal **México Central**.
- **Infraestructura como código:** Bicep, desplegado con Azure CLI a nivel de
  suscripción.
- **Control de fuentes:** Git + GitHub.

---

## Cómo empezar

Antes de tocar el código, abre VS Code (o tu IDE preferido) **en la raíz del
monorepo**, no dentro de una subcarpeta. Esto permite que herramientas como
ESLint, dotnet, y los analizadores de Bicep encuentren correctamente sus
archivos de configuración.

Según en qué parte vayas a trabajar:

- Antes de tocar código nuevo, lee la **vista de arquitectura completa** en
  [`docs/arquitectura.md`](./docs/arquitectura.md) (15-20 min). Es el
  documento más completo del repo y el contexto necesario para que el resto
  haga sentido.
- Para desplegar o modificar la infraestructura en Azure, lee
  [`infra/README.md`](./infra/README.md). Incluye prerequisitos (Azure CLI,
  Bicep CLI, permisos en Entra ID), pasos del primer despliegue, comandos de
  validación y `what-if`, convenciones de nombres y costos estimados.
- Para trabajar en el backend, lee [`backend/README.md`](./backend/README.md).
- Para trabajar en el frontend, lee [`frontend/README.md`](./frontend/README.md).
- Para entender decisiones arquitectónicas pasadas o documentar nuevas, ve a
  [`docs/decisiones/`](./docs/decisiones/).
- Para documentación por módulo (levantamientos, diseño, plan de
  implementación), ve a [`docs/modulos/`](./docs/modulos/).
- Para replicar el Claude Project del equipo en tu cuenta individual de
  `claude.ai/projects`, ve a [`claude-project/`](./claude-project/).

---

## Desarrollo local

Para iterar sin necesidad de Azure ni Entra ID. Ver
[ADR-0015](./docs/decisiones/0015-local-dev-auth.md) para detalles del modo
`FakeForLocalDev`.

### Pre-requisitos

- **.NET 9 SDK** — backend
- **Node.js 22** — frontend (versión pinneada en `frontend/.nvmrc`)
- **Docker** — para PostgreSQL local. Si prefieres Postgres nativo, instálalo
  con usuario `pgadmin`/password `pgadmin` y crea el database `millet_dev`.

### Setup primer arranque

```powershell
# 1. Levantar PostgreSQL (en el root del repo)
docker compose -f docker-compose.dev.yml up -d

# 2. Aplicar migraciones (TODOS los DbContexts, en orden)
#    El backend NO auto-migra: el health check /health/ready da 503 si falta
#    alguna. La lista debe coincidir con MigrationsHealthCheckOptions en
#    Program.cs y con el job run-migrations de deploy-app-dev.yml. Al entrar un
#    módulo nuevo, agrégalo aquí también.
cd backend
$contexts = @(
  @{ Ctx = "CompartidoDbContext";         Proj = "src/Compartido/Millet.Compartido.csproj" },
  @{ Ctx = "CoreDbContext";               Proj = "src/SharedKernel/Millet.SharedKernel.csproj" },
  @{ Ctx = "IdentidadDbContext";          Proj = "src/Identidad/Millet.Identidad.csproj" },
  @{ Ctx = "ComprasDbContext";            Proj = "src/Compras/Millet.Compras.csproj" },
  @{ Ctx = "IntegracionesAwDbContext";    Proj = "src/Integraciones.Aw/Millet.Integraciones.Aw.csproj" },
  @{ Ctx = "IntegracionesFiscalDbContext";Proj = "src/Integraciones.Fiscal/Millet.Integraciones.Fiscal.csproj" },
  @{ Ctx = "AlmacenDbContext";            Proj = "src/Almacen/Millet.Almacen.csproj" },
  @{ Ctx = "CuentasPorPagarDbContext";    Proj = "src/CuentasPorPagar/Millet.CuentasPorPagar.csproj" },
  @{ Ctx = "FacturacionDbContext";        Proj = "src/Facturacion/Millet.Facturacion.csproj" },
  @{ Ctx = "CuentasPorCobrarDbContext";   Proj = "src/CuentasPorCobrar/Millet.CuentasPorCobrar.csproj" },
  @{ Ctx = "TesoreriaDbContext";          Proj = "src/Tesoreria/Millet.Tesoreria.csproj" },
  @{ Ctx = "CentrosCostoDbContext";       Proj = "src/CentrosCosto/Millet.CentrosCosto.csproj" }
)
foreach ($c in $contexts) {
  dotnet ef database update --context $c.Ctx --project $c.Proj `
    --startup-project src/Api/Millet.Api.csproj
}

# 3. Instalar deps del frontend
cd ../frontend
npm install
```

### Cada sesión de desarrollo

```powershell
# Terminal 1: backend con hot-reload
cd backend/src/Api
dotnet watch run

# Terminal 2: frontend
cd frontend
npm run dev
```

Abre <http://localhost:5173>. Verás el `<DevUserSelector />` (en lugar del botón
"Iniciar sesión con Microsoft") — selecciona **"Super Admin (Dev)"** para
loguearte con todos los permisos. El `BootstrapSuperAdminHostedService` crea
automáticamente el usuario, el rol `super-admin` y la empresa inicial dev en
el primer arranque del backend.

### Parar el ambiente

```powershell
# Detiene Postgres pero PRESERVA la data
docker compose -f docker-compose.dev.yml down

# Detiene Y borra el volumen — usar si quieres re-bootstrap desde cero
docker compose -f docker-compose.dev.yml down -v
```

---

## Owner del proyecto

Eduardo Paredes — `eduardo.paredes@tiglass.net`
