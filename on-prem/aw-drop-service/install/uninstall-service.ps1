# ============================================================================
# uninstall-service.ps1
# Detiene y desinstala el Millet A+W Drop Service.
# NO borra binarios ni appsettings.json — solo el registro del servicio.
# Para limpiar todo, borrar manualmente $InstallDir después.
# ============================================================================

[CmdletBinding()]
param(
    [string]$ServiceName    = 'MilletAwDropService',
    [string]$EventLogSource = 'MilletAwDropService',
    [switch]$RemoveEventLogSource
)

$ErrorActionPreference = 'Stop'

function Require-Admin {
    # `New-Object` en lugar de `[Type]::new(...)` — compatible con
    # Windows PowerShell 4.0 (base de Windows Server 2012 R2).
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $currentUser = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $currentUser.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Este script debe ejecutarse como Administrator."
    }
}

Require-Admin

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    Write-Host "El servicio '$ServiceName' no está instalado. Nada que hacer."
} else {
    if ($svc.Status -ne 'Stopped') {
        Write-Host "Deteniendo servicio '$ServiceName'..."
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }
    Write-Host "Borrando servicio '$ServiceName'..."
    & sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc.exe delete falló con exit code $LASTEXITCODE." }
    Write-Host "✅ Servicio borrado."
}

if ($RemoveEventLogSource) {
    if ([System.Diagnostics.EventLog]::SourceExists($EventLogSource)) {
        Write-Host "Eliminando Event Log source '$EventLogSource'..."
        Remove-EventLog -Source $EventLogSource
        Write-Host "✅ Event Log source eliminado."
    } else {
        Write-Host "Event Log source '$EventLogSource' no existe — skip."
    }
} else {
    Write-Host ""
    Write-Host "Nota: el Event Log source '$EventLogSource' NO se eliminó. Si quieres también borrarlo,"
    Write-Host "      vuelve a ejecutar con: .\uninstall-service.ps1 -RemoveEventLogSource"
}
