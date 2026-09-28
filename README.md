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

El inicio de sesión de desarrollo usa Microsoft Entra ID. Cada integrante
necesita una cuenta autorizada, el registro de aplicación de desarrollo y sus
valores locales de configuración. El modo `FakeForLocalDev` sigue disponible
solo para pruebas locales explícitas; ver
[ADR-0015](./docs/decisiones/0015-local-dev-auth.md).

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

### Configuración personal antes de iniciar sesión

El repositorio entrega la **estructura**, no las credenciales ni los IDs de
Entra de cada entorno. Pide a TI los valores del tenant de desarrollo, el
registro de la aplicación SPA, el registro del API y la cuenta autorizada.
No copies los valores de otra persona ni edites los cuatro archivos
versionados para tu máquina:

| Archivo compartido | Qué contiene en Git | Dónde poner tus valores |
|---|---|---|
| `backend/src/Api/Properties/launchSettings.json` | Perfil y puerto local de la API. | Variables de entorno de tu terminal; no agregues aquí conexiones ni OID. |
| `backend/src/Api/appsettings.json` | Estructura y valores generales, sin OID personal. | Variables de entorno del backend. |
| `backend/src/Api/appsettings.Development.json` | Opciones de desarrollo; campos de Entra personales vacíos. | Copia [`backend/.env.local.example`](backend/.env.local.example) a `backend/.env.local` y complétala. |
| `frontend/.env.development` | Modo `EntraId`, URL local y campos de Entra vacíos. | Copia [`frontend/.env.local.example`](frontend/.env.local.example) a `frontend/.env.development.local` y complétala. |

Antes de completar las plantillas, confirma con TI estas correspondencias:
`VITE_ENTRA_CLIENT_ID` es el ID de la **SPA**;
`Auth__EntraId__ClientId` es el ID del **API**;
`Auth__EntraId__Audience` es la audiencia (`aud`) que acepta el API;
`VITE_API_AUDIENCE` es el **scope** que solicita la SPA (por ejemplo,
`api://<api-client-id>/access_as_user`). Confirma también tenant, URI de
retorno, cuenta autorizada y puerto real de PostgreSQL. No supongas que
audiencia y scope son el mismo texto.

Los dos archivos `.local` están ignorados por Git. **.NET no carga
`backend/.env.local` automáticamente:** en macOS/Linux/WSL, desde la raíz
del repositorio, cárgalo en la misma terminal que ejecutará la API:

```bash
cp backend/.env.local.example backend/.env.local
cp frontend/.env.local.example frontend/.env.development.local
# Edita ambos archivos con los valores autorizados antes de continuar.
set -a
. backend/.env.local
set +a
cd backend
dotnet watch run --project src/Api/Millet.Api.csproj
```

En PowerShell, configura las mismas claves en la terminal de la API; el
archivo `.env.local` no se importa solo. Sustituye estos marcadores por los
valores autorizados:

```powershell
Copy-Item frontend/.env.local.example frontend/.env.development.local
# Completa el archivo copiado con los valores de la SPA y el API.
$env:Auth__Mode = 'EntraId'
$env:Auth__EntraId__TenantId = '<tenant-id>'
$env:Auth__EntraId__ClientId = '<api-client-id>'
$env:Auth__EntraId__Audience = '<api-audience>'
# Sólo al inicializar una base nueva con un administrador autorizado:
# $env:Auth__InitialAdminEntraOid = '<oid-del-primer-admin>'
$env:ConnectionStrings__Postgres = '<conexion-a-tu-postgres-local>'
dotnet watch run --project backend/src/Api/Millet.Api.csproj
```

La conexión `ConnectionStrings__Postgres` debe apuntar a tu PostgreSQL local
y a su puerto real. El script de setup configura esa conexión **durante el
setup**, pero no la deja exportada en la
terminal desde la que luego inicias `dotnet watch`.

En otra terminal, ejecuta `cd frontend && npm run dev`. Confirma que
`VITE_API_BASE_URL` coincide con el puerto de la API, que la SPA de Entra
tiene registrada `http://localhost:5173/` como URI de retorno y que
`VITE_API_AUDIENCE` es el scope expuesto por el registro del API. Las
variables `VITE_` son visibles en el navegador: nunca incluyas secretos allí.
`VITE_ENTRA_DOMAIN` no se usa en el inicio de sesión actual.

Para comprobar tu preparación: los cuatro archivos compartidos de la tabla
deben permanecer sin cambios en `git status`; la API debe responder en
`/health/ready`; después, **Continuar con Microsoft** debe volver al ERP y
mostrar sólo las empresas, sucursales y acciones asignadas a tu cuenta.
Comprueba también **Cerrar sesión**. Si TI aún no entregó tenant, registros,
scope o cuenta autorizada, registra ese dato como **Por confirmar**: una
pantalla que compila no acredita el acceso real.

### Cada sesión de desarrollo

```bash
# Terminal 1: backend con hot-reload
cd backend
dotnet watch run --project src/Api/Millet.Api.csproj

# Terminal 2: frontend
cd frontend
npm run dev
```

Usa las plantillas de configuración personal anteriores. Si inicializas una
base nueva y necesitas el primer administrador, confirma con TI el OID de la
cuenta autorizada y ponlo en `Auth__InitialAdminEntraOid` **sólo en tu entorno
local**. Si queda vacío, el backend omite ese alta inicial.

Abre <http://localhost:5173> y usa **Continuar con Microsoft**. Si faltan los
IDs de Entra, el frontend indicará que no puede iniciar; si falta el OID de
administrador en una base nueva, el backend omitirá ese bootstrap. Tener una
cuenta de Microsoft no concede permisos ERP por sí solo: la cuenta debe estar
vinculada y autorizada en el sistema.

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
