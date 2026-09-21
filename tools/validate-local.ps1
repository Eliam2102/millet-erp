#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$Backend = Join-Path $RepoRoot 'backend'
$Frontend = Join-Path $RepoRoot 'frontend'

function Invoke-Checked {
    param([string]$Command, [string[]]$Arguments)
    & $Command @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Falló: $Command $($Arguments -join ' ') (exit $LASTEXITCODE)"
    }
}

Push-Location $Backend
try {
    Invoke-Checked 'dotnet' @('restore', 'Millet.sln')
    Invoke-Checked 'dotnet' @('build', 'Millet.sln', '--configuration', 'Release', '--no-restore', '--nologo')

    Get-ChildItem (Join-Path $Backend 'tests') -Directory -Filter '*UnitTests' |
        ForEach-Object {
            $project = Get-ChildItem $_.FullName -Filter '*.csproj' | Select-Object -First 1
            if ($project) {
                Invoke-Checked 'dotnet' @(
                    'test', $project.FullName,
                    '--configuration', 'Release',
                    '--no-build', '--no-restore', '--nologo'
                )
            }
        }
} finally {
    Pop-Location
}

Push-Location $Frontend
try {
    Invoke-Checked 'npm' @('install', '--no-audit', '--no-fund')
    Invoke-Checked 'npm' @('run', 'lint')
    Invoke-Checked 'npm' @('run', 'build')
    Invoke-Checked 'npm' @('test')
} finally {
    Pop-Location
}

Write-Host ''
Write-Host 'Gate local aprobado: backend unitario + frontend lint/build/tests.' -ForegroundColor Green
Write-Host 'Para integración aislada use WSL/Git Bash con ./tools/validate-integration-isolated.sh.'
