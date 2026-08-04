#requires -Version 5.1
<#
.SYNOPSIS
    Regenera docs/api/openapi-v1.json a partir del API corriendo localmente.

.DESCRIPTION
    Levanta `dotnet run` del proyecto Api en background, espera readiness,
    descarga `/openapi/v1.json`, normaliza el JSON (strip servers + paths
    /api/dev/* + path raíz "/") y lo guarda en `docs/api/openapi-v1.json`
    pretty-printed para diffs útiles.

    Usar SIEMPRE después de modificar metadata de endpoints (`.WithSummary`,
    `.Produces<T>`, etc) y commitear el JSON resultante junto con los
    cambios. CI verifica que el snapshot esté en sync con `git diff`.

.PARAMETER Port
    Puerto local en el que levantar la app. Default: 5050 (evita choque con
    una app dev que ya esté corriendo en :5000).

.PARAMETER WaitSeconds
    Segundos para esperar startup. Default: 15.

.EXAMPLE
    .\tools\openapi\regenerate-snapshot.ps1
    .\tools\openapi\regenerate-snapshot.ps1 -Port 5051 -WaitSeconds 20
#>
[CmdletBinding()]
param(
    [int]$Port = 5050,
    [int]$WaitSeconds = 15
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$apiProj = Join-Path $repoRoot 'backend/src/Api/Millet.Api.csproj'
$outputFile = Join-Path $repoRoot 'docs/api/openapi-v1.json'
$tempFile = Join-Path ([System.IO.Path]::GetTempPath()) "millet-openapi-$([Guid]::NewGuid().ToString('N')).json"

if (-not (Test-Path $apiProj)) { throw "No se encontró $apiProj" }

Write-Host "Levantando API en http://localhost:$Port (env=Development)..." -ForegroundColor Cyan
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://localhost:$Port"

$proc = Start-Process -FilePath 'dotnet' `
    -ArgumentList @('run', '--project', $apiProj, '--no-launch-profile') `
    -PassThru -NoNewWindow -RedirectStandardOutput "$tempFile.api.log"

try {
    Start-Sleep -Seconds $WaitSeconds
    Write-Host "Descargando /openapi/v1.json..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri "http://localhost:$Port/openapi/v1.json" -OutFile $tempFile -UseBasicParsing | Out-Null
}
finally {
    if ($proc -and -not $proc.HasExited) {
        Write-Host "Deteniendo API (PID $($proc.Id))..." -ForegroundColor Cyan
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
}

# Normalizar: quitar 'servers' + paths /api/dev/* + path raíz "/".
$spec = Get-Content $tempFile -Raw | ConvertFrom-Json
$spec.PSObject.Properties.Remove('servers') | Out-Null

$pathsToRemove = @($spec.paths.PSObject.Properties.Name | Where-Object {
    $_.StartsWith('/api/dev/') -or $_ -eq '/'
})
foreach ($p in $pathsToRemove) {
    $spec.paths.PSObject.Properties.Remove($p) | Out-Null
}

# Pretty-print con indent 2 (alineado con la generación inicial).
$json = $spec | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($outputFile, $json + [System.Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

Remove-Item $tempFile -ErrorAction SilentlyContinue
Remove-Item "$tempFile.api.log" -ErrorAction SilentlyContinue

Write-Host "OK. Snapshot guardado en $outputFile" -ForegroundColor Green
Write-Host "Próximo paso: 'git diff docs/api/openapi-v1.json' y commitear." -ForegroundColor Yellow
