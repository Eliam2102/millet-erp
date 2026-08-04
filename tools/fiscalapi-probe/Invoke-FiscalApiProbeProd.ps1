#requires -Version 7.0
<#
.SYNOPSIS
    Probe end-to-end de FiscalAPI PRODUCCIÓN: aprovisiona Person Receptor
    + FIEL, crea DownloadRule, lanza DownloadRequest, pollea y cosecha
    meta-items del SAT real.

.DESCRIPTION
    A diferencia del probe sandbox (Invoke-FiscalApiProbeE2E.ps1), este
    script apunta a https://live.fiscalapi.com y trabaja con datos
    fiscales REALES. NO emite facturas — la descarga masiva consume
    los CFDIs reales que el SAT tiene asociados al RFC receptor.

    Pasos (cada uno con checkpoint):

      1. Crear/encontrar Person Receptor con el RFC real de Millet.
      2. Subir FIEL .cer al Person (POST /api/v4/tax-files fileType=0).
      3. Subir FIEL .key al Person (POST /api/v4/tax-files fileType=1).
      4. Crear DownloadRule (Recibidos × Metadata × Vigente).
      5. Crear DownloadRequest para los últimos N días.
      6. Pollear hasta Completada.
      7. Listar /meta-items si quedó cosechable.

    SEGURIDAD:
    - La FIEL nunca se loggea ni se guarda en el reporte JSON.
    - Solo IDs (Person, TaxFile, Rule, Request) van al state.
    - Credenciales se leen de env vars distintas a sandbox.
    - La contraseña de la FIEL se pasa como SecureString o env var.

.PARAMETER TenantKey
    X-TENANT-KEY de FiscalAPI PRODUCCIÓN (env $FISCALAPI_PROD_TENANT_KEY).
    OJO: NO usar el del sandbox.

.PARAMETER ApiKey
    X-API-KEY de FiscalAPI PRODUCCIÓN (env $FISCALAPI_PROD_API_KEY).

.PARAMETER ReceptorRfc
    RFC real de Millet (razón social que va a recibir CFDIs).

.PARAMETER ReceptorLegalName
    Razón social EXACTA como aparece en SAT (sin régimen societario,
    en mayúsculas, sin acentos si SAT no los tiene).

.PARAMETER ReceptorZipCode
    Código postal fiscal del receptor en SAT.

.PARAMETER ReceptorEmail
    Email del receptor (puede ser uno administrativo de Millet).

.PARAMETER ReceptorTaxRegimeCode
    Régimen fiscal del receptor (default '601' — General de Ley Personas Morales).

.PARAMETER FielCerPath
    Path al archivo .cer de la FIEL del RFC receptor.

.PARAMETER FielKeyPath
    Path al archivo .key de la FIEL del RFC receptor.

.PARAMETER FielPassword
    Contraseña de la FIEL. Si se omite, lee $env:FISCALAPI_FIEL_PASSWORD.

.PARAMETER WindowDays
    Cuántos días hacia atrás cubrir la DownloadRequest (default 7).
    Máximo 31 días (límite FiscalAPI para CFDI/Metadata).

.PARAMETER PollAttempts
    Cuántas veces pollear (default 5).

.PARAMETER PollIntervalSeconds
    Segundos entre polls (default 30).

.PARAMETER Reset
    Borra el state file y empieza de cero.

.PARAMETER StateFile
    Path del state file (default .\fiscalapi-probe-prod-state.json).

.PARAMETER OutputJson
    Path del reporte final (default .\fiscalapi-probe-prod-report.json).
    NO contiene base64 de FIEL ni credenciales.

.EXAMPLE
    # Set credentials + password as env vars (más seguro que en línea):
    $env:FISCALAPI_PROD_TENANT_KEY = 'tu-tenant-prod'
    $env:FISCALAPI_PROD_API_KEY    = 'tu-api-prod'
    $env:FISCALAPI_FIEL_PASSWORD   = 'tu-password-fiel'

    .\Invoke-FiscalApiProbeProd.ps1 `
        -ReceptorRfc 'XXX111111XXX' `
        -ReceptorLegalName 'NOMBRE EXACTO EN SAT' `
        -ReceptorZipCode '01234' `
        -ReceptorEmail 'admin@millet.com' `
        -FielCerPath 'C:\path\to\fiel.cer' `
        -FielKeyPath 'C:\path\to\fiel.key'

.NOTES
    Owner: eduardo.paredes@tiglass.net
    Doc:   docs/modulos/integraciones-fiscal/02-flujo-asincrono.md §12

    Razón de existir: el endpoint POST /api/v4/download-rules está
    bloqueado en sandbox (test.fiscalapi.com) — solo dashboard. En
    producción (live.fiscalapi.com) sí funciona via API. Por eso
    Millet usa live como única configuración del módulo
    Integraciones.Fiscal.
#>
[CmdletBinding()]
param(
    [string]$TenantKey             = $env:FISCALAPI_PROD_TENANT_KEY,
    [string]$ApiKey                = $env:FISCALAPI_PROD_API_KEY,
    [string]$BaseUrl               = 'https://live.fiscalapi.com',
    [string]$TimeZone              = 'America/Mexico_City',

    [Parameter(Mandatory)][string]$ReceptorRfc,
    [Parameter(Mandatory)][string]$ReceptorLegalName,
    [Parameter(Mandatory)][string]$ReceptorZipCode,
    [string]$ReceptorEmail         = 'admin@millet.test',
    [string]$ReceptorTaxRegimeCode = '601',

    [Parameter(Mandatory)][string]$FielCerPath,
    [Parameter(Mandatory)][string]$FielKeyPath,
    [string]$FielPassword          = $env:FISCALAPI_FIEL_PASSWORD,

    [int]$WindowDays               = 7,
    [int]$PollAttempts             = 5,
    [int]$PollIntervalSeconds      = 30,
    [switch]$Reset,
    [string]$StateFile             = (Join-Path $PSScriptRoot 'fiscalapi-probe-prod-state.json'),
    [string]$OutputJson            = (Join-Path $PSScriptRoot 'fiscalapi-probe-prod-report.json')
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Validación de parámetros
# ---------------------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($TenantKey)) {
    throw "TenantKey requerido. Pasa -TenantKey o set FISCALAPI_PROD_TENANT_KEY env var."
}
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "ApiKey requerido. Pasa -ApiKey o set FISCALAPI_PROD_API_KEY env var."
}
if ([string]::IsNullOrWhiteSpace($FielPassword)) {
    throw "FielPassword requerida. Pasa -FielPassword o set FISCALAPI_FIEL_PASSWORD env var."
}
if (-not (Test-Path $FielCerPath)) { throw "No existe $FielCerPath" }
if (-not (Test-Path $FielKeyPath)) { throw "No existe $FielKeyPath" }
if ($WindowDays -lt 1 -or $WindowDays -gt 31) {
    throw "WindowDays debe estar entre 1 y 31 (límite FiscalAPI para CFDI/Metadata)."
}

# Confirmar al user que estamos en producción.
if ($BaseUrl -like '*live.fiscalapi.com*') {
    Write-Host ""
    Write-Host "⚠️  PRODUCCIÓN: $BaseUrl" -ForegroundColor Yellow
    Write-Host "    RFC receptor: $ReceptorRfc"
    Write-Host "    Ventana descarga: $WindowDays días"
    Write-Host "    Esto consume cuota REAL de tu suscripción FiscalAPI."
    Write-Host ""
}

# ---------------------------------------------------------------------------
# State management
# ---------------------------------------------------------------------------

if ($Reset -and (Test-Path $StateFile)) {
    Remove-Item $StateFile -Force
    Write-Host "State file borrado. Empezando de cero." -ForegroundColor Yellow
}

$state = if (Test-Path $StateFile) {
    Get-Content $StateFile -Raw | ConvertFrom-Json -AsHashtable
} else { @{} }

function Save-State { $state | ConvertTo-Json -Depth 12 | Set-Content -Path $StateFile -Encoding UTF8 }

# ---------------------------------------------------------------------------
# HTTP helper (sin loggear body de upload de FIEL)
# ---------------------------------------------------------------------------

function Invoke-FiscalApi {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [object]$Body,
        [switch]$SuppressBodyLog
    )
    $url = $BaseUrl.TrimEnd('/') + $Path
    $headers = @{
        'X-TENANT-KEY' = $TenantKey
        'X-API-KEY'    = $ApiKey
        'X-TIME-ZONE'  = $TimeZone
        'Content-Type' = 'application/json'
        'Accept'       = 'application/json'
    }
    $iwrParams = @{
        Method             = $Method
        Uri                = $url
        Headers            = $headers
        SkipHttpErrorCheck = $true
        MaximumRedirection = 0
        TimeoutSec         = 60
    }
    if ($null -ne $Body) {
        $iwrParams.Body = ($Body | ConvertTo-Json -Depth 12 -Compress)
    }
    $startedAt = [DateTimeOffset]::UtcNow
    try {
        $resp = Invoke-WebRequest @iwrParams
    } catch {
        return [pscustomobject]@{
            Url       = $url; Method = $Method; StatusCode = -1
            ElapsedMs = ([DateTimeOffset]::UtcNow - $startedAt).TotalMilliseconds
            Body      = $null; RawBody = $_.Exception.Message
            Error     = $true
        }
    }
    $parsedBody = $null
    if (-not [string]::IsNullOrWhiteSpace($resp.Content)) {
        try { $parsedBody = $resp.Content | ConvertFrom-Json -Depth 25 } catch { }
    }
    [pscustomobject]@{
        Url       = $url; Method = $Method; StatusCode = [int]$resp.StatusCode
        ElapsedMs = ([DateTimeOffset]::UtcNow - $startedAt).TotalMilliseconds
        Body      = $parsedBody; RawBody = $resp.Content
        Error     = $false
    }
}

function Assert-Success {
    param($Response, [string]$StepName, [switch]$RedactBody)
    if ($Response.Error -or $Response.StatusCode -lt 200 -or $Response.StatusCode -ge 300) {
        Write-Host ""
        Write-Host "FALLO en $StepName" -ForegroundColor Red
        Write-Host "  URL: $($Response.Url)" -ForegroundColor Red
        Write-Host "  Method: $($Response.Method)" -ForegroundColor Red
        Write-Host "  StatusCode: $($Response.StatusCode)" -ForegroundColor Red
        if (-not $RedactBody) {
            Write-Host "  Body:" -ForegroundColor Red
            Write-Host ($Response.RawBody ?? '(vacío)') -ForegroundColor Red
        } else {
            Write-Host "  Body: (redacted — error en upload sensible)" -ForegroundColor Red
        }
        Write-Host ""
        Write-Host "State file $StateFile conserva los IDs ya creados. Re-corre para reintentar desde acá." -ForegroundColor Yellow
        throw "Paso '$StepName' falló."
    }
}

function Write-Step { param([string]$Num, [string]$Title)
    Write-Host ""
    Write-Host ("─" * 70) -ForegroundColor Cyan
    Write-Host "PASO $Num : $Title" -ForegroundColor Cyan
    Write-Host ("─" * 70) -ForegroundColor Cyan
}
function Write-Skip { param([string]$Num, [string]$Detail)
    Write-Host "[skip] Paso $Num ya completado: $Detail" -ForegroundColor Gray
}

# ===========================================================================
# Paso 1 — Crear/encontrar Person Receptor con el RFC real de Millet
# ===========================================================================

Write-Step "1" "Crear/encontrar Person Receptor ($ReceptorRfc)"

if ($state.receptorId) {
    Write-Skip "1" "receptorId=$($state.receptorId)"
} else {
    $list = Invoke-FiscalApi -Method GET -Path '/api/v4/people?pageNumber=1&pageSize=100'
    Assert-Success $list "list-people"
    $found = ($list.Body.data.items ?? @()) | Where-Object { $_.tin -eq $ReceptorRfc } | Select-Object -First 1
    if ($found) {
        $state.receptorId = $found.id
        Write-Host "Reusando Person existente: id=$($found.id), legalName='$($found.legalName)'" -ForegroundColor Yellow
        # Sincronizar legalName + zipCode con los valores que el user proveyó
        # (los que están en SAT). Crítico para que la descarga acepte el match.
        $needsUpdate = ($found.legalName -cne $ReceptorLegalName) -or ($found.zipCode -ne $ReceptorZipCode)
        if ($needsUpdate) {
            Write-Host "Actualizando legalName/zipCode con valores que diste..." -ForegroundColor Yellow
            $upd = Invoke-FiscalApi -Method PUT -Path "/api/v4/people/$($found.id)" -Body @{
                id             = $found.id
                legalName      = $ReceptorLegalName
                email          = $found.email
                tin            = $ReceptorRfc
                satTaxRegimeId = $found.satTaxRegimeId ?? $ReceptorTaxRegimeCode
                zipCode        = $ReceptorZipCode
            }
            Assert-Success $upd "update-receptor"
        }
    } else {
        $body = @{
            legalName      = $ReceptorLegalName
            email          = $ReceptorEmail
            password       = 'Probe.Mil123!Prod'
            tin            = $ReceptorRfc
            satTaxRegimeId = $ReceptorTaxRegimeCode
            zipCode        = $ReceptorZipCode
        }
        $resp = Invoke-FiscalApi -Method POST -Path '/api/v4/people' -Body $body
        Assert-Success $resp "create-receptor"
        $state.receptorId = $resp.Body.data.id
        Write-Host "Receptor creado: id=$($resp.Body.data.id)" -ForegroundColor Green
    }
    Save-State
}

# ===========================================================================
# Paso 2 — Subir FIEL .cer
# ===========================================================================

Write-Step "2" "Subir FIEL .cer (tax-file fileType=0)"

if ($state.fielCerFileId) {
    Write-Skip "2" "fielCerFileId=$($state.fielCerFileId)"
} else {
    $cerBytes  = [System.IO.File]::ReadAllBytes($FielCerPath)
    $cerBase64 = [Convert]::ToBase64String($cerBytes)
    $resp = Invoke-FiscalApi -Method POST -Path '/api/v4/tax-files' -Body @{
        personId   = $state.receptorId
        tin        = $ReceptorRfc
        base64File = $cerBase64
        fileType   = 0
        password   = $FielPassword
    } -SuppressBodyLog
    # No imprimir el body en caso de éxito ni de error (contiene la FIEL).
    if ($resp.Error -or $resp.StatusCode -lt 200 -or $resp.StatusCode -ge 300) {
        Write-Host "FALLO subiendo FIEL .cer — HTTP $($resp.StatusCode)" -ForegroundColor Red
        # Solo loggeamos message/details, no body completo (puede contener fragmentos).
        if ($resp.Body) {
            Write-Host "  message: $($resp.Body.message)" -ForegroundColor Red
            Write-Host "  details: $($resp.Body.details)" -ForegroundColor Red
        }
        throw "upload-fiel-cer failed"
    }
    $state.fielCerFileId = $resp.Body.data.id
    Save-State
    Write-Host "FIEL .cer subido: id=$($resp.Body.data.id), validFrom=$($resp.Body.data.validFrom), validTo=$($resp.Body.data.validTo)" -ForegroundColor Green
    # NO loggear base64File ni password.
}

# ===========================================================================
# Paso 3 — Subir FIEL .key
# ===========================================================================

Write-Step "3" "Subir FIEL .key (tax-file fileType=1)"

if ($state.fielKeyFileId) {
    Write-Skip "3" "fielKeyFileId=$($state.fielKeyFileId)"
} else {
    $keyBytes  = [System.IO.File]::ReadAllBytes($FielKeyPath)
    $keyBase64 = [Convert]::ToBase64String($keyBytes)
    $resp = Invoke-FiscalApi -Method POST -Path '/api/v4/tax-files' -Body @{
        personId   = $state.receptorId
        tin        = $ReceptorRfc
        base64File = $keyBase64
        fileType   = 1
        password   = $FielPassword
    } -SuppressBodyLog
    if ($resp.Error -or $resp.StatusCode -lt 200 -or $resp.StatusCode -ge 300) {
        Write-Host "FALLO subiendo FIEL .key — HTTP $($resp.StatusCode)" -ForegroundColor Red
        if ($resp.Body) {
            Write-Host "  message: $($resp.Body.message)" -ForegroundColor Red
            Write-Host "  details: $($resp.Body.details)" -ForegroundColor Red
        }
        throw "upload-fiel-key failed"
    }
    $state.fielKeyFileId = $resp.Body.data.id
    Save-State
    Write-Host "FIEL .key subida: id=$($resp.Body.data.id)" -ForegroundColor Green
}

# ===========================================================================
# Paso 4 — Crear DownloadRule
# ===========================================================================

Write-Step "4" "Crear DownloadRule (Recibidos × Metadata × Vigente)"

if ($state.ruleId) {
    Write-Skip "4" "ruleId=$($state.ruleId)"
} else {
    $resp = Invoke-FiscalApi -Method POST -Path '/api/v4/download-rules' -Body @{
        description        = "Probe Millet PROD — Recibidos Vigente $ReceptorRfc"
        personId           = $state.receptorId
        satQueryTypeId     = 'Metadata'
        downloadTypeId     = 'Recibidos'
        satInvoiceStatusId = 'Vigente'
    }
    Assert-Success $resp "create-rule"
    $state.ruleId = $resp.Body.data.id
    Save-State
    Write-Host "Rule creada: id=$($resp.Body.data.id), tin=$($resp.Body.data.tin)" -ForegroundColor Green
}

# ===========================================================================
# Paso 5 — Crear DownloadRequest
# ===========================================================================

Write-Step "5" "Crear DownloadRequest (últimos $WindowDays días)"

if ($state.requestId) {
    Write-Skip "5" "requestId=$($state.requestId) — para uno nuevo pasa -Reset"
} else {
    $endDate   = (Get-Date).ToString('yyyy-MM-ddT00:00:00')
    $startDate = (Get-Date).AddDays(-$WindowDays).ToString('yyyy-MM-ddT00:00:00')
    $resp = Invoke-FiscalApi -Method POST -Path '/api/v4/download-requests' -Body @{
        downloadRuleId        = $state.ruleId
        downloadRequestTypeId = 'Manual'
        startDate             = $startDate
        endDate               = $endDate
    }
    Assert-Success $resp "create-request"
    $state.requestId        = $resp.Body.data.id
    $state.requestStartDate = $startDate
    $state.requestEndDate   = $endDate
    $state.requestCreatedAt = $resp.Body.data.createdAt
    Save-State
    Write-Host "Request creada: id=$($resp.Body.data.id) — ventana $startDate -> $endDate" -ForegroundColor Green
    Write-Host "Estado inicial: satStatus=$($resp.Body.data.satRequestStatusId), reqStatus=$($resp.Body.data.downloadRequestStatusId)" -ForegroundColor Gray
}

# ===========================================================================
# Paso 6 — Pollear
# ===========================================================================

Write-Step "6" "Pollear DownloadRequest ($PollAttempts × ${PollIntervalSeconds}s)"

$polls = @()
$completedRun = $false
for ($i = 1; $i -le $PollAttempts; $i++) {
    if ($i -gt 1) { Start-Sleep -Seconds $PollIntervalSeconds }
    $resp = Invoke-FiscalApi -Method GET -Path "/api/v4/download-requests/$($state.requestId)"
    Assert-Success $resp "poll-${i}"
    $satStatus = $resp.Body.data.satRequestStatusId
    $reqStatus = $resp.Body.data.downloadRequestStatusId
    $invCount  = $resp.Body.data.invoiceCount
    $polls += [pscustomobject]@{
        attempt = $i
        satRequestStatusId      = $satStatus
        downloadRequestStatusId = $reqStatus
        invoiceCount            = $invCount
        lastAttemptDate         = $resp.Body.data.lastAttemptDate
        nextAttemptDate         = $resp.Body.data.nextAttemptDate
    }
    Write-Host ("  Poll #${i}: satStatus={0} | reqStatus={1} | invoiceCount={2}" -f $satStatus, $reqStatus, $invCount) -ForegroundColor Gray
    if ($reqStatus -eq '3') {
        Write-Host "  -> Completada! Pasamos a cosecha." -ForegroundColor Green
        $completedRun = $true
        break
    }
    if ($reqStatus -eq '-1' -or $satStatus -in '4','5','6','-1') {
        Write-Host "  -> Estado terminal de error/abandono. Stop." -ForegroundColor Red
        break
    }
}
$state.polls = $polls
Save-State

# ===========================================================================
# Paso 7 — Cosechar /meta-items (si Completada)
# ===========================================================================

Write-Step "7" "Cosechar /meta-items (si reqStatus=3)"

if ($completedRun) {
    $meta = Invoke-FiscalApi -Method GET -Path "/api/v4/download-requests/$($state.requestId)/meta-items?pageNumber=1&pageSize=50"
    Assert-Success $meta "list-meta-items"
    $items = $meta.Body.data.items ?? @()
    $state.metaItems = @{
        count          = $items.Count
        totalCount     = $meta.Body.data.totalCount
        totalPages     = $meta.Body.data.totalPages
        # Solo guardamos los UUIDs (no PII), no metadata completa.
        sampleUuids    = ($items | Select-Object -First 10 -ExpandProperty uuid -ErrorAction SilentlyContinue)
        firstItemShape = if ($items.Count -gt 0) { @($items[0].PSObject.Properties.Name) } else { @() }
    }
    Save-State
    Write-Host "Cosechados $($items.Count) items (totalCount=$($meta.Body.data.totalCount), totalPages=$($meta.Body.data.totalPages))." -ForegroundColor Green
    if ($items.Count -gt 0) {
        Write-Host "Shape del primer item: $($state.metaItems.firstItemShape -join ', ')" -ForegroundColor Gray
        Write-Host "Primeros UUIDs:"
        $state.metaItems.sampleUuids | ForEach-Object { Write-Host "  $_" }
    } else {
        Write-Host "Sin CFDIs en la ventana — RFC sin actividad reciente o ventana sin recibidos." -ForegroundColor Yellow
    }
} else {
    Write-Host "Request aún no en estado 3 (Completada). Re-corre el script más tarde — skipea pasos 1-5 y solo continúa polleando." -ForegroundColor Yellow
}

# ===========================================================================
# Reporte final (sin secretos)
# ===========================================================================

Write-Host ""
Write-Host ("═" * 70) -ForegroundColor Cyan
Write-Host "RESUMEN" -ForegroundColor Cyan
Write-Host ("═" * 70) -ForegroundColor Cyan

$lastPoll = $polls | Select-Object -Last 1
$report = [ordered]@{
    ranAt              = (Get-Date).ToString('o')
    baseUrl            = $BaseUrl
    receptor           = @{ id = $state.receptorId; rfc = $ReceptorRfc; legalName = $ReceptorLegalName }
    fielCerFileId      = $state.fielCerFileId
    fielKeyFileId      = $state.fielKeyFileId
    ruleId             = $state.ruleId
    requestId          = $state.requestId
    requestStartDate   = $state.requestStartDate
    requestEndDate     = $state.requestEndDate
    polls              = $polls
    metaItems          = $state.metaItems
    # NO incluir: tenantKey, apiKey, fielPassword, ni base64 de la FIEL.
}
$report | ConvertTo-Json -Depth 25 | Set-Content -Path $OutputJson -Encoding UTF8

Write-Host ""
Write-Host "Receptor: $($state.receptorId) ($ReceptorRfc — $ReceptorLegalName)"
Write-Host "FIEL .cer: $($state.fielCerFileId)"
Write-Host "FIEL .key: $($state.fielKeyFileId)"
Write-Host "Rule: $($state.ruleId)"
Write-Host "Request: $($state.requestId) (ventana $($state.requestStartDate) -> $($state.requestEndDate))"
if ($lastPoll) {
    Write-Host "Estado: satStatus=$($lastPoll.satRequestStatusId), reqStatus=$($lastPoll.downloadRequestStatusId), invoiceCount=$($lastPoll.invoiceCount)"
}
if ($state.metaItems) {
    Write-Host "Meta-items cosechados: $($state.metaItems.count)"
}
Write-Host ""
Write-Host "Reporte (sin secretos): $OutputJson" -ForegroundColor Cyan
Write-Host "State: $StateFile" -ForegroundColor Cyan
