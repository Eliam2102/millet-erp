# Tools

Scripts y utilidades del proyecto.

## `setup-dev.ps1`

Setup automatizado del ambiente local. Verifica prerrequisitos, restaura
herramientas, aplica las 3 migraciones EF e instala dependencias del
frontend. Idempotente.

```powershell
# Desde la raíz del repo
.\tools\setup-dev.ps1

# Saltar pasos
.\tools\setup-dev.ps1 -SkipMigrations
.\tools\setup-dev.ps1 -SkipNpmInstall
```

Pre-requisitos que el script **verifica pero no instala**: .NET 9 SDK,
Node.js 22, Postgres en `:5432`. Ver
[CONTRIBUTING.md](../CONTRIBUTING.md) para los comandos de instalación.
