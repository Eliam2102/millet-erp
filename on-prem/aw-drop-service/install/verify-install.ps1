# ============================================================================
# verify-install.ps1
# Smoke test del Millet A+W Drop Service post-instalación.
# Ejecuta:
#   1. Service en estado Running
#   2. GET / → 200 + texto con versión
#   3. GET /healthz → 200 con import_folder_writable=true
#   4. POST /drop-edi sin X-API-Key → 401
#   5. POST /drop-edi con API key correcta + payload válido → 200 + archivo creado
#   6. Cleanup del archivo de prueba
#
# Uso (en SER-DATA, sin necesidad de admin):
#   .\verify-install.ps1
# ============================================================================

[CmdletBinding()]
param(
    [string]$BaseUrl     = 'http://localhost:5000',
    [string]$ServiceName = 'MilletAwDropService',
    [string]$ConfigPath  = 'C:\Apps\MilletAwDropService\appsettings.json'
)

$ErrorActionPreference = 'Stop'

function Step($n, $msg) { Write-Host ""; Write-Host "[$n] $msg" -ForegroundColor Cyan }
function OK($msg)       { Write-Host "    ✅ $msg" -ForegroundColor Green }
function Fail($msg)     { Write-Host "    ❌ $msg" -ForegroundColor Red; exit 1 }

# 1. Servicio Running.
Step 1 "Verificar estado del servicio"
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc)                  { Fail "Servicio '$ServiceName' no instalado. Corre install-service.ps1 primero." }
if ($svc.Status -ne 'Running')  { Fail "Servicio '$ServiceName' en estado $($svc.Status), esperado Running." }
OK "Servicio Running"

# 2. GET / (smoke).
Step 2 "GET $BaseUrl/"
try {
    $resp = Invoke-WebRequest -Uri "$BaseUrl/" -UseBasicParsing -TimeoutSec 5
    if ($resp.StatusCode -ne 200)                          { Fail "Esperado 200, recibido $($resp.StatusCode)" }
    if ($resp.Content -notlike 'Millet A+W Drop Service*') { Fail "Respuesta inesperada: $($resp.Content)" }
    OK "Respuesta: $($resp.Content)"
} catch {
    Fail "No se pudo alcanzar $BaseUrl/. Detalle: $($_.Exception.Message)"
}

# 3. GET /healthz.
Step 3 "GET $BaseUrl/healthz"
$health = Invoke-RestMethod -Uri "$BaseUrl/healthz" -TimeoutSec 5
Write-Host "    Respuesta: $($health | ConvertTo-Json -Compress)"
if ($health.status -ne 'alive')        { Fail "status esperado 'alive', recibido '$($health.status)'" }
if (-not $health.import_folder_writable) { Fail "import_folder_writable=false — la carpeta no es escribible para el service account." }
OK "Alive, uptime=$($health.uptime_seconds)s, writable=$($health.import_folder_writable)"

# 4. POST sin X-API-Key.
Step 4 "POST /drop-edi sin X-API-Key (esperado 401)"
$tmpEdi = Join-Path $env:TEMP "verify-$([guid]::NewGuid().ToString('N').Substring(0,8)).edi"
"REC1 dummy edi body for verify`r`n#END#" | Set-Content -Path $tmpEdi -Encoding UTF8
$padding = 'X' * 200
Add-Content -Path $tmpEdi -Value $padding
try {
    Invoke-WebRequest -Uri "$BaseUrl/drop-edi" -Method POST `
        -Headers @{ 'X-Filename' = 'verify.edi' } `
        -InFile $tmpEdi -ContentType 'text/plain' -UseBasicParsing -TimeoutSec 5 | Out-Null
    Fail "Esperado 401, recibido 2xx — auth NO está activa!"
} catch {
    $code = $_.Exception.Response.StatusCode.value__
    if ($code -ne 401) { Fail "Esperado 401, recibido $code. Detalle: $($_.Exception.Message)" }
    OK "Auth bloquea requests sin API key (401)"
}

# 5. POST con API key correcta. Recupera la key de appsettings.json local.
Step 5 "POST /drop-edi con API key + payload válido (esperado 200)"
if (-not (Test-Path $ConfigPath)) { Fail "No se encontró $ConfigPath para leer la API key." }
$cfg = Get-Content $ConfigPath -Raw | ConvertFrom-Json
$apiKey = $cfg.DropService.ApiKey
if ([string]::IsNullOrWhiteSpace($apiKey)) { Fail "DropService.ApiKey vacía en $ConfigPath." }
$importFolder = $cfg.DropService.AwImportFolder

$testFilename = "verify-$([guid]::NewGuid().ToString('N').Substring(0,8)).edi"
try {
    $resp = Invoke-RestMethod -Uri "$BaseUrl/drop-edi" -Method POST `
        -Headers @{ 'X-API-Key' = $apiKey; 'X-Filename' = $testFilename } `
        -InFile $tmpEdi -ContentType 'text/plain' -TimeoutSec 10
    OK "Drop OK: bytes=$($resp.bytes_written), path=$($resp.path)"

    # Verifica que el archivo exista y borralo (cleanup).
    if (-not (Test-Path $resp.path)) { Fail "Drop reportó éxito pero el archivo no existe en $($resp.path)" }
    Remove-Item $resp.path -Force
    OK "Archivo de verificación eliminado de $importFolder"
} catch {
    Fail "POST falló: $($_.Exception.Message)"
} finally {
    Remove-Item $tmpEdi -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "🟢 Todos los checks pasaron. El servicio está operativo." -ForegroundColor Green
Write-Host ""
Write-Host "Siguiente paso: configurar la Hybrid Connection 'hc-aw-drop-service' en HCM"
Write-Host "                para que el módulo Millet.Integraciones.Aw del ERP pueda llegar aquí."
