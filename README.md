# Millet ERP

> **Nuevo integrante:** empieza por [docs/handoff/00-EMPIEZA-AQUI.md](docs/handoff/00-EMPIEZA-AQUI.md). Ese paquete separa el estado real del código, el arranque local, la forma de trabajo y las dependencias que debe entregar Millet.

> **Repositorio:** <https://github.com/Eliam2102/millet-erp>. Al corte del
> 20/09/2026 GitHub lo reportó público y `main` sin protección. La visibilidad
> objetivo debe confirmarse con el propietario; mientras tanto, la regla
> operativa es trabajar por rama y pull request, nunca directo a `main`.

> **Gate de arranque:** tener acceso no habilita desarrollo funcional. Geovany y Uzziel deben completar primero la [Ola 0 en ClickUp](https://app.clickup.com/9017291387/v/l/li/901717118871). La [Ola 1A](https://app.clickup.com/9017291387/v/l/li/901717118872) sólo comienza cuando se complete y valide `O0-07 · Gate de salida · habilitación de Ola 1A`.

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
| [`frontend/`](./frontend/) | SPA en React 19 + TypeScript + Vite + shadcn/ui. |
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

### Secuencia obligatoria del primer arranque

No comenzar una funcionalidad directamente después de clonar. El orden de
trabajo es:

1. Leer el handoff, la planeación de Fase 1 y la ficha asignada en Notion/ClickUp.
2. Confirmar acceso, MFA, identidad Git y herramientas.
3. Clonar el repositorio y preparar una base local exclusiva por desarrollador.
4. Aplicar los 12 contextos de migración.
5. Levantar backend y frontend, iniciar sesión y ejecutar el smoke test.
6. Registrar evidencia del entorno en la tarea individual de Ola 0.
7. Recorrer los módulos existentes para no reconstruir trabajo ya realizado.
8. Probar el flujo de rama, pull request y revisión cruzada.
9. Esperar la validación de `O0-07`; sólo entonces iniciar Ola 1A.

El checklist verificable está en
[`docs/handoff/06-checklist-primer-dia.md`](./docs/handoff/06-checklist-primer-dia.md).

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

La banda de SDK se fija en [`global.json`](./global.json); no se depende del
SDK más reciente instalado en la máquina.

### Setup primer arranque

macOS, Linux, WSL o Git Bash:

```bash
./tools/setup-dev.sh
```

Windows PowerShell:

```powershell
.\tools\setup-dev.ps1
```

Los dos scripts consumen `tools/migration-contexts.txt`, que es la lista
canónica de los 12 `DbContext`. Levantan PostgreSQL con Docker Compose,
restauran y compilan el backend, aplican migraciones e instalan el frontend.
Las opciones y la ruta manual están documentadas en
[`docs/handoff/01-arranque-local.md`](./docs/handoff/01-arranque-local.md).

### Cada sesión de desarrollo

```bash
# Terminal 1: backend con hot-reload
cd backend
dotnet watch run --project src/Api/Millet.Api.csproj

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

```bash
# Detiene Postgres pero PRESERVA la data
docker compose -f docker-compose.dev.yml down

# Detiene Y borra el volumen — usar si quieres re-bootstrap desde cero
docker compose -f docker-compose.dev.yml down -v
```

---

## Responsables del handoff

- Planeación y alcance: Eliam Cauich y Ángel Sánchez.
- Desarrollo: Geovany y Uzziel; reparto detallado en Notion/ClickUp.
- Coordinación Millet, TI y A+W: Jorge Toache.
- Administración del repositorio: Eliam Cauich (`Eliam2102`).
- Usuarios exactos de GitHub de Geovany y Uzziel: deben verificarse antes de emitir las invitaciones; no se infieren a partir del correo.
- Owner histórico indicado por el repositorio original: Eduardo Paredes; vigencia y participación actual `Por confirmar`.
