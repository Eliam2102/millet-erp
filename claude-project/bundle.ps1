#Requires -Version 5.1
<#
.SYNOPSIS
    Genera un zip con los archivos del knowledge base para subir al Claude
    Project (claude.ai/projects).

.DESCRIPTION
    Empaqueta los archivos listados en knowledge-base.md (núcleo + operación
    + ADRs por glob + levantamientos por glob). El zip resultante se sube al
    uploader del Project, reemplazando los archivos previos.

.EXAMPLE
    .\claude-project\bundle.ps1

.EXAMPLE
    .\claude-project\bundle.ps1 -OutputPath .\out\kb.zip
#>
[CmdletBinding()]
param(
    [string]$OutputPath = "$PSScriptRoot/millet-erp-kb.zip"
)
$ErrorActionPreference = 'Stop'

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

# === Núcleo + operación: rutas explícitas ===
$Explicitos = @(
    'README.md',
    'CLAUDE.md',
    'CONTRIBUTING.md',
    'docs/README.md',
    'docs/arquitectura.md',
    'docs/onboarding-dev-seed-users.md',
    'docs/decisiones/README.md',
    'docs/decisiones/template.md',
    'docs/levantamientos/README.md',
    'infra/README.md',
    'backend/README.md',
    'frontend/README.md',
    'tools/README.md'
)

# === ADRs: descubrir por glob para no tener que actualizar el script cada vez ===
$AdrsDir = Join-Path $RepoRoot 'docs/decisiones'
$Adrs = Get-ChildItem -Path $AdrsDir -Filter '0*.md' -File |
        ForEach-Object { "docs/decisiones/$($_.Name)" }

# === Levantamientos: descubrir todos los .md (excluye README ya incluido) ===
$LevantamientosDir = Join-Path $RepoRoot 'docs/levantamientos'
$Levantamientos = @()
if (Test-Path $LevantamientosDir) {
    $Levantamientos = Get-ChildItem -Path $LevantamientosDir -Filter '*.md' -File |
                      Where-Object { $_.Name -ne 'README.md' } |
                      ForEach-Object { "docs/levantamientos/$($_.Name)" }
}

$Files = @($Explicitos + $Adrs + $Levantamientos | Sort-Object -Unique)

# Validar que todos existan (falla rápido si la lista quedó desincronizada)
$Faltantes = @()
foreach ($f in $Files) {
    $abs = Join-Path $RepoRoot $f
    if (-not (Test-Path $abs)) { $Faltantes += $f }
}
if ($Faltantes.Count -gt 0) {
    Write-Host "Archivos faltantes (corregir knowledge-base.md o bundle.ps1):" -ForegroundColor Red
    $Faltantes | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

if (Test-Path $OutputPath) { Remove-Item $OutputPath -Force }

$absolutos = $Files | ForEach-Object { Join-Path $RepoRoot $_ }
Compress-Archive -Path $absolutos -DestinationPath $OutputPath -Force

$sizeKb = [Math]::Round((Get-Item $OutputPath).Length / 1KB, 1)
Write-Host ""
Write-Host "Bundle generado:" -ForegroundColor Green
Write-Host "  $OutputPath ($($Files.Count) archivos, $sizeKb KB)"
Write-Host ""
Write-Host "Sube este zip al Project en claude.ai/projects -> Knowledge base." -ForegroundColor Cyan
