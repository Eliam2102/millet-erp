#Requires -Version 5.1
<#
.SYNOPSIS
    Setup del ambiente local de Millet ERP.

.DESCRIPTION
    Verifica prerrequisitos, restaura herramientas, aplica migraciones EF y
    instala dependencias del frontend. Idempotente — re-correr es seguro.

.EXAMPLE
    .\tools\setup-dev.ps1

.EXAMPLE
    .\tools\setup-dev.ps1 -SkipMigrations
#>

[CmdletBinding()]
param(
    [switch]$SkipMigrations,
    [switch]$SkipNpmInstall,
    [ValidateRange(1, 65535)]
    [int]$PostgresPort = 5432
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$Backend = Join-Path $RepoRoot 'backend'
$Frontend = Join-Path $RepoRoot 'frontend'

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Ok {
    param([string]$Message)
    Write-Host "    OK  $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "    !!  $Message" -ForegroundColor Yellow
}

function Fail {
    param([string]$Message)
    Write-Host ""
    Write-Host "    XX  $Message" -ForegroundColor Red
    Write-Host ""
    exit 1
}

# === 1. Repo root ===
Write-Step "Validando que estamos en la raíz del monorepo"
if (-not (Test-Path (Join-Path $RepoRoot '.git'))) {
    Fail "No encuentro .git en $RepoRoot — ¿clonaste el repo bien?"
}
if (-not (Test-Path (Join-Path $RepoRoot 'CLAUDE.md'))) {
    Fail "No encuentro CLAUDE.md en $RepoRoot — el script está mal ubicado."
}
Write-Ok "Repo root: $RepoRoot"

# === 1b. Backend no debe estar corriendo ===
Write-Step "Verificando que el backend no esté corriendo"
$apiProcs = Get-Process -Name 'Millet.Api' -ErrorAction SilentlyContinue
if ($apiProcs) {
    $pids = ($apiProcs | ForEach-Object { $_.Id }) -join ', '
    Fail "Millet.Api está corriendo (PID $pids). Cierra 'dotnet watch run' antes de correr este script — bloquea los DLLs y rompe el build."
}
Write-Ok "Backend detenido"

# === 2. .NET 9 SDK ===
Write-Step "Verificando .NET 9 SDK"
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Fail "dotnet no está en PATH. Instala con: winget install Microsoft.DotNet.SDK.9"
}
$sdks = & dotnet --list-sdks
$has9 = $sdks | Where-Object { $_ -match '^9\.' }
if (-not $has9) {
    Fail "No hay SDK 9.x instalado. SDKs encontrados: $($sdks -join ', '). Instala con: winget install Microsoft.DotNet.SDK.9"
}
Write-Ok ".NET SDK 9.x detectado"

# === 3. Node 22 ===
Write-Step "Verificando Node.js 22"
$node = Get-Command node -ErrorAction SilentlyContinue
if (-not $node) {
    Fail "node no está en PATH. Instala con: winget install OpenJS.NodeJS.LTS"
}
$nodeVersion = & node -v
if ($nodeVersion -notmatch '^v22\.') {
    Write-Warn "Versión actual: $nodeVersion. El proyecto pinea v22 (frontend/.nvmrc). Puede compilar pero recomendamos v22."
} else {
    Write-Ok "Node $nodeVersion"
}

# === 4. Postgres reachable ===
Write-Step "Verificando Postgres en localhost:$PostgresPort"
$pgTest = Test-NetConnection -ComputerName localhost -Port $PostgresPort -InformationLevel Quiet -WarningAction SilentlyContinue
if (-not $pgTest) {
    Write-Host ""
    Write-Host "    Postgres no responde en localhost:$PostgresPort." -ForegroundColor Red
    Write-Host "    Opciones:" -ForegroundColor Red
    Write-Host "      - Docker:  `$env:POSTGRES_PORT='$PostgresPort'; docker compose -f docker-compose.dev.yml up -d" -ForegroundColor Red
    Write-Host "      - Nativo:  Inicia el servicio postgresql-x64-17 (Get-Service postgresql*)" -ForegroundColor Red
    Fail "Levanta Postgres y vuelve a correr el script."
}
Write-Ok "Postgres responde en :$PostgresPort"

# Sobrescribe únicamente la conexión del proceso local. Evita que build,
# migraciones y pruebas se conecten por accidente a otra instancia en 5432.
$env:ConnectionStrings__Postgres = "Host=localhost;Port=$PostgresPort;Database=millet_dev;Username=pgadmin;Password=pgadmin;Include Error Detail=true"

# === 5. dotnet tool restore ===
Write-Step "Restaurando herramientas locales (dotnet-ef)"
Push-Location $Backend
try {
    & dotnet tool restore | Out-Host
    if ($LASTEXITCODE -ne 0) { Fail "dotnet tool restore falló (exit $LASTEXITCODE)" }
    Write-Ok "Tools restaurados"
} finally {
    Pop-Location
}

# === 6. Build sanity check ===
Write-Step "Compilando backend (sanity check)"
Push-Location $Backend
try {
    & dotnet build Millet.sln --nologo --verbosity quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { Fail "dotnet build falló (exit $LASTEXITCODE)" }
    Write-Ok "Backend compila"
} finally {
    Pop-Location
}

# === 7. Migraciones EF ===
if ($SkipMigrations) {
    Write-Step "Saltando migraciones (--SkipMigrations)"
} else {
    Write-Step "Aplicando migraciones EF (12 contextos)"
    $contexts = @(
        @{ Name = 'CompartidoDbContext'; Project = 'src/Compartido/Millet.Compartido.csproj' },
        @{ Name = 'CoreDbContext';       Project = 'src/SharedKernel/Millet.SharedKernel.csproj' },
        @{ Name = 'IdentidadDbContext';  Project = 'src/Identidad/Millet.Identidad.csproj' },
        @{ Name = 'ComprasDbContext'; Project = 'src/Compras/Millet.Compras.csproj' },
        @{ Name = 'IntegracionesAwDbContext'; Project = 'src/Integraciones.Aw/Millet.Integraciones.Aw.csproj' },
        @{ Name = 'IntegracionesFiscalDbContext'; Project = 'src/Integraciones.Fiscal/Millet.Integraciones.Fiscal.csproj' },
        @{ Name = 'AlmacenDbContext'; Project = 'src/Almacen/Millet.Almacen.csproj' },
        @{ Name = 'CuentasPorPagarDbContext'; Project = 'src/CuentasPorPagar/Millet.CuentasPorPagar.csproj' },
        @{ Name = 'FacturacionDbContext'; Project = 'src/Facturacion/Millet.Facturacion.csproj' },
        @{ Name = 'CuentasPorCobrarDbContext'; Project = 'src/CuentasPorCobrar/Millet.CuentasPorCobrar.csproj' },
        @{ Name = 'TesoreriaDbContext'; Project = 'src/Tesoreria/Millet.Tesoreria.csproj' },
        @{ Name = 'CentrosCostoDbContext'; Project = 'src/CentrosCosto/Millet.CentrosCosto.csproj' }
    )
    Push-Location $Backend
    try {
        foreach ($ctx in $contexts) {
            Write-Host "    -> $($ctx.Name)" -ForegroundColor DarkGray
            & dotnet ef database update --context $ctx.Name `
                --project $ctx.Project `
                --startup-project 'src/Api/Millet.Api.csproj' `
                --no-build | Out-Host
            if ($LASTEXITCODE -ne 0) { Fail "Migración $($ctx.Name) falló (exit $LASTEXITCODE)" }
        }
        Write-Ok "12 contextos de migración aplicados"
    } finally {
        Pop-Location
    }
}

# === 8. npm install ===
if ($SkipNpmInstall) {
    Write-Step "Saltando npm install (--SkipNpmInstall)"
} else {
    Write-Step "Instalando dependencias del frontend"
    Push-Location $Frontend
    try {
        & npm install | Out-Host
        if ($LASTEXITCODE -ne 0) { Fail "npm install falló (exit $LASTEXITCODE)" }
        Write-Ok "Frontend listo"
    } finally {
        Pop-Location
    }
}

# === Done ===
Write-Host ""
Write-Host "==> Setup completo." -ForegroundColor Green
Write-Host ""
Write-Host "Para arrancar la app:" -ForegroundColor Cyan
Write-Host "  Opción A (VS Code):  Tasks: Run Task -> 'dev: start all'"
Write-Host "  Opción B (terminal):"
Write-Host "    Terminal 1:  cd backend/src/Api && dotnet watch run"
Write-Host "    Terminal 2:  cd frontend && npm run dev"
Write-Host ""
Write-Host "Frontend en http://localhost:5173" -ForegroundColor Cyan
Write-Host ""
