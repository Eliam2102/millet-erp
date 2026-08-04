# ============================================================================
# install-service.ps1
# Instala "Millet A+W Drop Service" como Windows Service en SER-DATA.
#
# Pre-requisitos:
#   - Ejecutar en PowerShell elevado (Run as administrator).
#   - Binarios publicados con `dotnet publish -c Release -r win-x64
#     --self-contained -o publish\` en una máquina con .NET 8 SDK.
#   - Carpeta `publish\` copiada a $InstallDir (default C:\Apps\MilletAwDropService).
#   - `appsettings.json` editado con la ApiKey real (ver
#     `appsettings.example.json`).
#
# Uso:
#   .\install-service.ps1                                    # defaults
#   .\install-service.ps1 -InstallDir 'D:\Apps\MilletDrop'    # path custom
# ============================================================================

[CmdletBinding()]
param(
    [string]$ServiceName     = 'MilletAwDropService',
    [string]$DisplayName     = 'Millet A+W Drop Service',
    [string]$Description     = 'Recibe archivos EDI via Hybrid Connection desde Millet ERP y los escribe a la carpeta de import de A+W.',
    [string]$InstallDir      = 'C:\Apps\MilletAwDropService',
    [string]$ExeName         = 'MilletAwDropService.exe',
    [string]$EventLogSource  = 'MilletAwDropService',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Require-Admin {
    # `New-Object` en lugar de `[Type]::new(...)` para compatibilidad con
    # Windows PowerShell 4.0 (versión base de Windows Server 2012 R2).
    # `::new()` requiere PS 5.0+, que NO está garantizado en SER-DATA salvo
    # que se haya instalado Windows Management Framework 5.x.
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $currentUser = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $currentUser.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Este script debe ejecutarse como Administrator. Abre PowerShell con 'Run as administrator' y vuelve a intentar."
    }
}

Require-Admin

$binPath = Join-Path $InstallDir $ExeName

if (-not (Test-Path $binPath)) {
    throw "No se encontró el ejecutable en '$binPath'. Antes de instalar, copia el contenido de 'publish\' a '$InstallDir'."
}

$appSettings = Join-Path $InstallDir 'appsettings.json'
if (-not (Test-Path $appSettings)) {
    throw "Falta '$appSettings'. Copia 'appsettings.example.json' a 'appsettings.json' y edítalo (sobre todo DropService.ApiKey)."
}

# Validación rápida: ApiKey no vacía. NO la imprimimos.
$json = Get-Content $appSettings -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($json.DropService.ApiKey)) {
    throw "DropService.ApiKey está vacía en '$appSettings'. Configurala antes de continuar (ver appsettings.example.json)."
}

# Crear EventLog source si no existe. Requiere admin (lo somos por
# Require-Admin arriba). Si ya existe lanza warning silenciado.
if (-not [System.Diagnostics.EventLog]::SourceExists($EventLogSource)) {
    Write-Host "Creando Event Log source '$EventLogSource' en 'Application'..."
    New-EventLog -LogName 'Application' -Source $EventLogSource
} else {
    Write-Host "Event Log source '$EventLogSource' ya existe — skip."
}

# Si el servicio ya existe, opcional reinstalar con -Force.
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if (-not $Force) {
        throw "El servicio '$ServiceName' ya existe. Usa -Force para detenerlo, borrarlo y reinstalarlo."
    }
    Write-Host "Servicio existente: detener y borrar (--Force)..."
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

# Crear servicio con sc.exe (PS-native New-Service no expone fácilmente
# todos los flags que queremos).
Write-Host "Creando servicio '$ServiceName' apuntando a '$binPath'..."
& sc.exe create $ServiceName binPath= "`"$binPath`"" start= auto displayName= "$DisplayName" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe create falló con exit code $LASTEXITCODE." }

& sc.exe description $ServiceName "$Description" | Out-Null

# Reinicio automático del servicio si crashea: reset el contador cada 24h,
# reintentar 10s, 30s, 60s.
& sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null

# Arrancar.
Write-Host "Iniciando servicio..."
Start-Service -Name $ServiceName
Start-Sleep -Seconds 2

$svc = Get-Service -Name $ServiceName
Write-Host ""
Write-Host "✅ Servicio instalado:"
Write-Host "    Nombre:    $($svc.Name)"
Write-Host "    Display:   $($svc.DisplayName)"
Write-Host "    Status:    $($svc.Status)"
Write-Host "    StartType: $($svc.StartType)"
Write-Host ""
Write-Host "Próximos pasos:"
Write-Host "   1. Verificar con: .\verify-install.ps1"
Write-Host "   2. Revisar Event Log: Get-EventLog -LogName Application -Source $EventLogSource -Newest 10"
Write-Host "   3. Configurar Hybrid Connection en HCM (ver README.md sección 'Conectividad Hybrid Connection')"
