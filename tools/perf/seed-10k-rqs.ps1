#requires -Version 5.1
<#
.SYNOPSIS
    Aplica tools/perf/seed-10k-rqs.sql contra la BD de dev local.

.DESCRIPTION
    Wrapper sobre `psql` para sembrar 10,000 requisiciones de prueba en
    compras.requisiciones (F8-PR3, benchmark de bandejas). El SQL es
    idempotente vía `ON CONFLICT DO NOTHING` y deja `ANALYZE` corrido.

.PARAMETER ConnectionString
    Cadena de conexión PostgreSQL completa. Si se omite, se intenta leer
    de la variable de entorno `MILLET_DEV_DB` o se usa el default
    `postgresql://millet:millet@localhost:5432/millet_dev`.

.PARAMETER Cleanup
    Si se especifica, BORRA las filas con folio `PERF-%` en lugar de
    insertar. Útil entre corridas.

.EXAMPLE
    .\tools\perf\seed-10k-rqs.ps1
    .\tools\perf\seed-10k-rqs.ps1 -ConnectionString "postgresql://..."
    .\tools\perf\seed-10k-rqs.ps1 -Cleanup
#>
[CmdletBinding()]
param(
    [string]$ConnectionString,
    [switch]$Cleanup
)

$ErrorActionPreference = 'Stop'

if (-not $ConnectionString) {
    $ConnectionString = $env:MILLET_DEV_DB
    if (-not $ConnectionString) {
        $ConnectionString = 'postgresql://millet:millet@localhost:5432/millet_dev'
        Write-Host "MILLET_DEV_DB no seteado; usando default $ConnectionString" -ForegroundColor Yellow
    }
}

# Verificar que psql está en PATH.
$psql = Get-Command psql -ErrorAction SilentlyContinue
if (-not $psql) {
    throw 'psql no encontrado en PATH. Instala el cliente de PostgreSQL.'
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if ($Cleanup) {
    Write-Host 'Borrando filas PERF-% de compras.requisiciones...' -ForegroundColor Cyan
    $sql = @"
DELETE FROM compras.requisiciones
WHERE empresa_id = '00000003-0000-0000-0000-000000000001'
  AND folio LIKE 'PERF-%';
ANALYZE compras.requisiciones;
"@
    & psql $ConnectionString -c $sql
    if ($LASTEXITCODE -ne 0) { throw "psql falló (exit $LASTEXITCODE)" }
    Write-Host 'Cleanup completado.' -ForegroundColor Green
    return
}

$seedFile = Join-Path $scriptDir 'seed-10k-rqs.sql'
if (-not (Test-Path $seedFile)) {
    throw "No se encontró $seedFile"
}

Write-Host 'Sembrando 10,000 requisiciones (puede tardar 5-15s)...' -ForegroundColor Cyan
$sw = [System.Diagnostics.Stopwatch]::StartNew()
& psql $ConnectionString -f $seedFile
$sw.Stop()
if ($LASTEXITCODE -ne 0) { throw "psql falló (exit $LASTEXITCODE)" }

Write-Host ("Seed completo en {0:N1}s." -f $sw.Elapsed.TotalSeconds) -ForegroundColor Green
Write-Host 'Próximo paso: leer tools/perf/bench-bandejas.md y ejecutar el benchmark.' -ForegroundColor Yellow
