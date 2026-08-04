# Cómo contribuir a Millet ERP

Guía de onboarding compactada. Si trabajas en una subcarpeta específica
(`infra/`, `backend/`, `frontend/`), su `README.md` profundiza en cada área.

---

## 1. Acceso

Antes de clonar necesitas:

- Estar invitado al repo `TiGlass/millet_erp` con rol `Write` mínimo
  (Eduardo Paredes — `eduardo.paredes@tiglass.net`).
- Una cuenta de GitHub con `gh` CLI o credenciales SSH/HTTPS configuradas.

Para desarrollar contra la app **no** necesitas Azure ni Entra ID: el modo
`FakeForLocalDev` funciona 100% local. Solo si vas a desplegar infra o tocar
secretos pides acceso adicional.

---

## 2. Pre-requisitos en tu máquina

| Herramienta | Versión | Cómo instalar (Windows) |
|---|---|---|
| .NET SDK | 9.x | `winget install Microsoft.DotNet.SDK.9` |
| Node.js | 22.x | `winget install OpenJS.NodeJS.LTS` (o `nvm use` con `frontend/.nvmrc`) |
| PostgreSQL | 16+ | Docker Desktop **o** `winget install PostgreSQL.PostgreSQL.17` |
| Git | reciente | `winget install Git.Git` |
| GitHub CLI | opcional | `winget install GitHub.cli` |
| VS Code | reciente | `winget install Microsoft.VisualStudioCode` |

Si usas Postgres nativo, crea el usuario y la base:
- Usuario: `pgadmin`, password: `pgadmin`
- Base de datos: `millet_dev`

Si usas Docker, no hay que crear nada — `docker-compose.dev.yml` lo deja listo.

---

## 3. Setup primer arranque

```powershell
git clone https://github.com/TiGlass/millet_erp.git
cd millet_erp

# Opción A: script automatizado (recomendado)
.\tools\setup-dev.ps1

# Opción B: manual — pasos en README.md sección "Setup primer arranque"
```

El script verifica prerrequisitos, levanta Postgres si usas Docker, aplica las
3 migraciones de EF y ejecuta `npm install`. Es idempotente — puedes correrlo
de nuevo si algo falla.

**Abre VS Code en la raíz del monorepo**, no en una subcarpeta. Cuando lo
abras te va a sugerir las extensiones recomendadas — instálalas todas.

---

## 4. Cada sesión de desarrollo

Dos formas:

**Vía VS Code (recomendado):**
- `Ctrl+Shift+P` → `Tasks: Run Task` → `dev: start all`
- Levanta backend (`:5000`) y frontend (`:5173`) en paralelo con hot-reload.

**Vía dos terminales:**
```powershell
# Terminal 1
cd backend/src/Api
dotnet watch run

# Terminal 2
cd frontend
npm run dev
```

Abre `http://localhost:5173`, selecciona uno de los 4 usuarios seed
(SuperAdmin, Facturador, Cobrador, Auditor) y entras. El bootstrap del
backend crea el SuperAdmin y la empresa "Millet Dev" la primera vez.

---

## 5. Convenciones de Git

- **Nunca trabajar directo en `main`.** Siempre rama nueva.
- Prefijo de rama según tipo de cambio:
  - `feature/<nombre>` — funcionalidad nueva
  - `fix/<nombre>` — bug fix
  - `chore/<nombre>` — tareas de mantenimiento, deps, tooling
  - `docs/<nombre>` — solo documentación
- Commits en español, formato convencional: `tipo(scope): descripción`.
  - Ejemplos: `feat(facturacion): emitir CFDI 4.0`, `fix(auth): handle Entra duplicate claims`
- PR a `main` con título descriptivo y body explicando el _por qué_ del cambio.
  Squash merge es el estándar.
- **Secretos jamás en commits.** Si necesitas un secreto, va a Azure Key Vault.

---

## 6. Antes de pushear

Corre el equivalente local del CI para no romper a otros:

- VS Code: `Tasks: Run Task` → `validate all (CI mirror)`
- Manual: `dotnet build`, `dotnet test`, `npm run build`, `npm run lint`

Esto reproduce el workflow de [pr-checks.yml](.github/workflows/pr-checks.yml).

---

## 7. Convenciones de código

Resumen — el detalle vive en [CLAUDE.md](CLAUDE.md):

**Backend (C#):**
- Nullable reference types siempre habilitados.
- `async`/`await` para todo I/O. Nada de `.Result` ni `.Wait()`.
- `record` para DTOs y value objects.
- `sealed` por defecto en clases concretas.
- **FluentValidation** (no Data Annotations), **Mapster** (no AutoMapper),
  **Serilog** para logging estructurado.

**Frontend (TS):**
- TanStack Router con file-based routing en `src/routes/`.
- TanStack Query para llamadas al backend.
- Zustand para estado global.
- shadcn/ui + Tailwind para componentes.

**Idioma:**
- Documentación, comentarios de negocio, nombres de módulos: **español**.
- Código, comandos, nombres técnicos: **inglés**.

---

## 8. Dónde leer más

| Tema | Archivo |
|---|---|
| Arquitectura general | [docs/arquitectura.md](docs/arquitectura.md) |
| Decisiones arquitectónicas (ADRs) | [docs/decisiones/](docs/decisiones/) |
| Documentación por módulo (levantamiento, diseño, plan de implementación) | [docs/modulos/](docs/modulos/) |
| Modo de auth en dev local | [docs/decisiones/0015-local-dev-auth.md](docs/decisiones/0015-local-dev-auth.md) |
| Despliegue y Bicep | [infra/README.md](infra/README.md) |

Cualquier duda que no esté en la docs, ping a Eduardo.
