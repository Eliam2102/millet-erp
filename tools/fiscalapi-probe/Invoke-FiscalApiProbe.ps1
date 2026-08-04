#requires -Version 7.0
<#
.SYNOPSIS
    Probe contra FiscalAPI sandbox para validar las 10 variables abiertas
    del doc docs/modulos/integraciones-fiscal/02-flujo-asincrono.md §12.

.DESCRIPTION
    Este script pega a https://test.fiscalapi.com con las credenciales
    provistas y valida sistemáticamente:

      1. Headers exactos (X-TENANT-KEY + X-API-KEY + X-TIME-ZONE).
      2. Catálogo SatQueryTypes.
      3. Catálogo SatRequestStatuses (códigos de error SAT).
      4. Si requiere FIEL subido (tax-files).
      5. Si "Consultar estado de factura" existe y su forma.
      6. Rate limit y header Retry-After en 429.
      7. Tiempo típico de Pendiente -> Terminada (con --create).
      8. TTL de solicitudes (si las viejas siguen accesibles).
      9. Formato response paginado de /meta-items.
     10. Si personId pre-existe o se crea on-the-fly.

    Por default es READ-ONLY (no muta nada). Pase -CreateData para crear
    un Person + DownloadRule + DownloadRequest de prueba (recursos
    persisten en su cuenta sandbox).

.PARAMETER TenantKey
    El X-TENANT-KEY. Si se omite, lee $env:FISCALAPI_SANDBOX_TENANT_KEY.

.PARAMETER ApiKey
    El X-API-KEY. Si se omite, lee $env:FISCALAPI_SANDBOX_API_KEY.

.PARAMETER BaseUrl
    Default: https://test.fiscalapi.com. Cambie a https://live.fiscalapi.com
    si quiere validar prod (NO RECOMENDADO desde este script).

.PARAMETER CreateData
    Si presente, intenta crear un Person + DownloadRule + DownloadRequest
    para validar el flujo end-to-end. Por default solo lee.

.PARAMETER OutputJson
    Path donde escribir el reporte JSON con todos los hallazgos. Default:
    .\fiscalapi-probe-report.json en el dir del script.

.EXAMPLE
    # Read-only — valida auth + catalogos + listados:
    .\Invoke-FiscalApiProbe.ps1 -TenantKey 'xxx' -ApiKey 'yyy'

.EXAMPLE
    # Flujo completo — crea recursos en sandbox:
    .\Invoke-FiscalApiProbe.ps1 -CreateData -TestRfc 'MIL010101AAA' `
        -TestLegalName 'Millet Test SA'

.NOTES
    Owner: eduardo.paredes@tiglass.net
    Doc:   docs/modulos/integraciones-fiscal/02-flujo-asincrono.md §12
#>
[CmdletBinding()]
param(
    [string]$TenantKey = $env:FISCALAPI_SANDBOX_TENANT_KEY,
    [string]$ApiKey    = $env:FISCALAPI_SANDBOX_API_KEY,
    [string]$BaseUrl   = 'https://test.fiscalapi.com',
    [string]$TimeZone  = 'America/Mexico_City',
    [switch]$CreateData,
    [string]$TestRfc      = 'XAXX010101000',
    [string]$TestLegalName = 'Millet Probe SA',
    [string]$OutputJson = (Join-Path $PSScriptRoot 'fiscalapi-probe-report.json')
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Validación de parámetros
# ---------------------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($TenantKey)) {
    throw "TenantKey requerido. Pasa -TenantKey o set FISCALAPI_SANDBOX_TENANT_KEY env var."
}
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "ApiKey requerido. Pasa -ApiKey o set FISCALAPI_SANDBOX_API_KEY env var."
}
if ($CreateData -and ([string]::IsNullOrWhiteSpace($TestRfc) -or [string]::IsNullOrWhiteSpace($TestLegalName))) {
    throw "Con -CreateData también requieres -TestRfc y -TestLegalName."
}

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Get-FiscalHeaders {
    @{
        'X-TENANT-KEY' = $TenantKey
        'X-API-KEY'    = $ApiKey
        'X-TIME-ZONE'  = $TimeZone
        'Content-Type' = 'application/json'
        'Accept'       = 'application/json'
    }
}

function Invoke-FiscalApi {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [object]$Body,
        [hashtable]$ExtraHeaders
    )
    $url = $BaseUrl.TrimEnd('/') + $Path
    $headers = Get-FiscalHeaders
    if ($ExtraHeaders) {
        foreach ($k in $ExtraHeaders.Keys) { $headers[$k] = $ExtraHeaders[$k] }
    }
    $iwrParams = @{
        Method  = $Method
        Uri     = $url
        Headers = $headers
        SkipHttpErrorCheck  = $true
        MaximumRedirection  = 0
        TimeoutSec          = 30
    }
    if ($null -ne $Body) {
        $iwrParams.Body = ($Body | ConvertTo-Json -Depth 12 -Compress)
    }
    $startedAt = [DateTimeOffset]::UtcNow
    try {
        $resp = Invoke-WebRequest @iwrParams
    } catch {
        return [pscustomobject]@{
            Url          = $url
            Method       = $Method
            StatusCode   = -1
            ElapsedMs    = ([DateTimeOffset]::UtcNow - $startedAt).TotalMilliseconds
            Headers      = @{}
            Body         = $null
            RawBody      = $_.Exception.Message
            NetworkError = $true
        }
    }
    $elapsedMs = ([DateTimeOffset]::UtcNow - $startedAt).TotalMilliseconds
    $rawBody = $resp.Content
    $parsedBody = $null
    if (-not [string]::IsNullOrWhiteSpace($rawBody)) {
        try { $parsedBody = $rawBody | ConvertFrom-Json -Depth 25 } catch { }
    }
    [pscustomobject]@{
        Url        = $url
        Method     = $Method
        StatusCode = [int]$resp.StatusCode
        ElapsedMs  = $elapsedMs
        Headers    = @{} + $resp.Headers
        Body       = $parsedBody
        RawBody    = $rawBody
        NetworkError = $false
    }
}

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host $Title -ForegroundColor Cyan
    Write-Host ("=" * 70) -ForegroundColor Cyan
}

function Write-Finding {
    param(
        [string]$Variable,
        [string]$Status,    # PASS / FAIL / WARN / INFO
        [string]$Detail
    )
    $color = switch ($Status) {
        'PASS' { 'Green' }
        'FAIL' { 'Red' }
        'WARN' { 'Yellow' }
        default { 'Gray' }
    }
    Write-Host ("[{0,-4}] " -f $Status) -ForegroundColor $color -NoNewline
    Write-Host ("§{0,-30} " -f $Variable) -NoNewline
    Write-Host $Detail
}

# ---------------------------------------------------------------------------
# Estado acumulado del reporte
# ---------------------------------------------------------------------------

$report = [ordered]@{
    ranAt        = (Get-Date).ToString('o')
    baseUrl      = $BaseUrl
    timeZone     = $TimeZone
    createData   = [bool]$CreateData
    psVersion    = $PSVersionTable.PSVersion.ToString()
    findings     = [System.Collections.Generic.List[object]]::new()
    rawRequests  = [System.Collections.Generic.List[object]]::new()
}

function Add-Finding {
    param([string]$Variable, [string]$Status, [string]$Detail, $Evidence)
    $report.findings.Add([ordered]@{
        variable = $Variable
        status   = $Status
        detail   = $Detail
        evidence = $Evidence
    }) | Out-Null
    Write-Finding -Variable $Variable -Status $Status -Detail $Detail
}

function Add-RawRequest {
    param($Response, [string]$Label)
    $report.rawRequests.Add([ordered]@{
        label      = $Label
        method     = $Response.Method
        url        = $Response.Url
        statusCode = $Response.StatusCode
        elapsedMs  = $Response.ElapsedMs
        headers    = $Response.Headers
        body       = $Response.Body
    }) | Out-Null
}

# ---------------------------------------------------------------------------
# §V1 — Headers + auth + envelope (lectura barata)
# ---------------------------------------------------------------------------

Write-Section "V1: Headers + Auth + Response envelope"

# Probamos un GET autenticado al endpoint más barato. Empezamos por
# /api/v4/download-catalogs porque la doc lo describe como "no paginado"
# y no muta nada.
$r1 = Invoke-FiscalApi -Method GET -Path '/api/v4/download-catalogs'
Add-RawRequest -Response $r1 -Label 'list-catalogs'

if ($r1.NetworkError) {
    Add-Finding 'V1.auth-reach' 'FAIL' "No se pudo conectar a $($r1.Url): $($r1.RawBody)" $null
} elseif ($r1.StatusCode -in 401, 403) {
    Add-Finding 'V1.auth-headers' 'FAIL' "Auth falló (HTTP $($r1.StatusCode)) con headers X-TENANT-KEY + X-API-KEY. Verifica las credenciales." $r1.Body
} elseif ($r1.StatusCode -ge 200 -and $r1.StatusCode -lt 300) {
    Add-Finding 'V1.auth-headers' 'PASS' "X-TENANT-KEY + X-API-KEY autenticaron (HTTP $($r1.StatusCode), $([math]::Round($r1.ElapsedMs)) ms)." $null
    # Verificamos el envelope documentado en /api-response.
    $b = $r1.Body
    $hasEnvelope = $b -ne $null -and `
        ($b.PSObject.Properties.Name -contains 'succeeded') -and `
        ($b.PSObject.Properties.Name -contains 'data') -and `
        ($b.PSObject.Properties.Name -contains 'httpStatusCode')
    if ($hasEnvelope) {
        Add-Finding 'V1.envelope' 'PASS' "Response wrap envelope confirmado: {data, succeeded, message, httpStatusCode, traceIdentifier}." $null
    } else {
        Add-Finding 'V1.envelope' 'WARN' "Response NO tiene el envelope esperado. Revisa rawRequests[0].body en el JSON output." $b
    }
} else {
    Add-Finding 'V1.auth-headers' 'WARN' "HTTP inesperado: $($r1.StatusCode). Cuerpo: $($r1.RawBody.Substring(0, [Math]::Min(200, $r1.RawBody.Length)))" $r1.Body
}

# ---------------------------------------------------------------------------
# §V2 — Catálogo SatQueryTypes (y demás)
# ---------------------------------------------------------------------------

Write-Section "V2-V3: Catálogos de descarga"

# Listamos catálogos disponibles (ya cargados en $r1) e inspeccionamos.
$catalogList = $r1.Body
if ($catalogList -and $catalogList.data) {
    $itemsRoot = $catalogList.data.items ?? $catalogList.data
    Add-Finding 'V2.catalog-list' 'INFO' ("Catálogos disponibles: " + (($itemsRoot | ForEach-Object { $_.name ?? $_.id ?? $_ }) -join ', ')) $itemsRoot
} else {
    Add-Finding 'V2.catalog-list' 'WARN' "No se pudo enumerar catálogos." $catalogList
}

# Detalles de los catálogos clave.
$catalogsToInspect = @(
    @{ Name = 'SatQueryTypes';        Var = 'V2.sat-query-types' },
    @{ Name = 'DownloadTypes';        Var = 'V2.download-types' },
    @{ Name = 'SatInvoiceStatuses';   Var = 'V2.sat-invoice-statuses' },
    @{ Name = 'SatInvoiceTypes';      Var = 'V2.sat-invoice-types' },
    @{ Name = 'SatRequestStatuses';   Var = 'V3.sat-request-statuses' },
    @{ Name = 'DownloadRequestStatuses'; Var = 'V3.download-request-statuses' },
    @{ Name = 'DownloadRequestTypes'; Var = 'V3.download-request-types' }
)

foreach ($cat in $catalogsToInspect) {
    $r = Invoke-FiscalApi -Method GET -Path "/api/v4/download-catalogs/$($cat.Name)"
    Add-RawRequest -Response $r -Label "catalog-$($cat.Name)"
    if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 300) {
        $items = $r.Body.data
        Add-Finding $cat.Var 'PASS' ("$($cat.Name): " + (($items | ForEach-Object { "$($_.id)=$($_.description ?? $_.name)" }) -join ' | ')) $items
    } else {
        Add-Finding $cat.Var 'WARN' ("$($cat.Name): HTTP $($r.StatusCode).") $r.RawBody
    }
}

# ---------------------------------------------------------------------------
# §V4 — ¿Tax-files (FIEL/CSD) requeridos? Listamos lo existente.
# ---------------------------------------------------------------------------

Write-Section "V4: Tax-files (CSD/FIEL)"

$r = Invoke-FiscalApi -Method GET -Path '/api/v4/tax-files?pageNumber=1&pageSize=10'
Add-RawRequest -Response $r -Label 'list-tax-files'
if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 300) {
    $count = ($r.Body.data.items ?? @()).Count
    Add-Finding 'V4.tax-files-list' 'INFO' "Tax-files actuales en la cuenta: $count." $r.Body.data
} else {
    Add-Finding 'V4.tax-files-list' 'WARN' "Listado tax-files: HTTP $($r.StatusCode)." $r.RawBody
}

# ---------------------------------------------------------------------------
# §V5 — ¿"Consultar estado de factura" existe? Lo probamos vacío para ver
#        la forma del request validator.
# ---------------------------------------------------------------------------

Write-Section "V5: Consultar estado de factura (refresh por UUID)"

$r = Invoke-FiscalApi -Method POST -Path '/api/v4/invoices/sat-status' -Body @{}
Add-RawRequest -Response $r -Label 'sat-status-empty'
switch ($r.StatusCode) {
    400 {
        Add-Finding 'V5.sat-status-endpoint' 'PASS' "Endpoint existe (HTTP 400 con body vacío). Inspecciona los validators." $r.Body
    }
    404 {
        # Probamos rutas alternativas.
        $alt = Invoke-FiscalApi -Method POST -Path '/api/v4/invoices-by-reference/sat-status' -Body @{}
        Add-RawRequest -Response $alt -Label 'sat-status-alt-empty'
        if ($alt.StatusCode -eq 400) {
            Add-Finding 'V5.sat-status-endpoint' 'PASS' "Endpoint en /invoices-by-reference/sat-status (HTTP 400 con body vacío)." $alt.Body
        } else {
            Add-Finding 'V5.sat-status-endpoint' 'WARN' "Ningún path conocido funcionó. Revisa Postman collection." @{ Tried = @($r.Url, $alt.Url); Status = @($r.StatusCode, $alt.StatusCode) }
        }
    }
    default {
        Add-Finding 'V5.sat-status-endpoint' 'WARN' "HTTP inesperado $($r.StatusCode)." $r.Body
    }
}

# ---------------------------------------------------------------------------
# §V6 — Rate limit y Retry-After
# ---------------------------------------------------------------------------

Write-Section "V6: Rate limit (best-effort)"

# Mandamos 30 requests baratas back-to-back y vemos si alguna devuelve 429.
$rateResults = for ($i = 0; $i -lt 30; $i++) {
    $rr = Invoke-FiscalApi -Method GET -Path '/api/v4/download-catalogs'
    [pscustomobject]@{ i = $i; statusCode = $rr.StatusCode; retryAfter = $rr.Headers['Retry-After']; elapsedMs = $rr.ElapsedMs }
}
$rateLimited = $rateResults | Where-Object { $_.statusCode -eq 429 }
if ($rateLimited) {
    $first = $rateLimited[0]
    Add-Finding 'V6.rate-limit' 'INFO' "Hit 429 en la request #$($first.i)/30. Retry-After header: $($first.retryAfter ?? '(no presente)')." $rateLimited
} else {
    Add-Finding 'V6.rate-limit' 'INFO' "30 requests back-to-back NO dispararon 429. El rate limit, si existe, es > 30/burst en /download-catalogs. Promedio: $([math]::Round((($rateResults.elapsedMs | Measure-Object -Average).Average), 1)) ms." $rateResults
}

# ---------------------------------------------------------------------------
# §V7-V8 — People / Download-rules / Download-requests existentes
# ---------------------------------------------------------------------------

Write-Section "V7-V8: People / Rules / Requests existentes"

$people = Invoke-FiscalApi -Method GET -Path '/api/v4/people?pageNumber=1&pageSize=10'
Add-RawRequest -Response $people -Label 'list-people'
if ($people.StatusCode -ge 200 -and $people.StatusCode -lt 300) {
    $count = ($people.Body.data.items ?? @()).Count
    Add-Finding 'V7.people-list' 'INFO' "People (recipients) actuales: $count. Total reportado: $($people.Body.data.totalCount)." $people.Body.data
} else {
    Add-Finding 'V7.people-list' 'WARN' "HTTP $($people.StatusCode)." $people.RawBody
}

$rules = Invoke-FiscalApi -Method GET -Path '/api/v4/download-rules?pageNumber=1&pageSize=10'
Add-RawRequest -Response $rules -Label 'list-rules'
if ($rules.StatusCode -ge 200 -and $rules.StatusCode -lt 300) {
    $count = ($rules.Body.data.items ?? @()).Count
    Add-Finding 'V8.rules-list' 'INFO' "Download-rules actuales: $count. Total: $($rules.Body.data.totalCount)." $rules.Body.data
} else {
    Add-Finding 'V8.rules-list' 'WARN' "HTTP $($rules.StatusCode)." $rules.RawBody
}

$requests = Invoke-FiscalApi -Method GET -Path '/api/v4/download-requests?pageNumber=1&pageSize=10'
Add-RawRequest -Response $requests -Label 'list-requests'
if ($requests.StatusCode -ge 200 -and $requests.StatusCode -lt 300) {
    $items = $requests.Body.data.items ?? @()
    Add-Finding 'V8.requests-list' 'INFO' "Download-requests actuales: $($items.Count). Total: $($requests.Body.data.totalCount)." $requests.Body.data

    # Si hay requests existentes, miramos el más viejo para inferir TTL.
    if ($items.Count -gt 0) {
        $oldest = $items | Sort-Object { [DateTime]$_.createdAt } | Select-Object -First 1
        $ageDays = ((Get-Date) - [DateTime]$oldest.createdAt).TotalDays
        Add-Finding 'V8.requests-ttl' 'INFO' ("Request más vieja en la cuenta: createdAt=$($oldest.createdAt), edad ~$([math]::Round($ageDays, 1))d. Si FiscalAPI tuviera TTL, ya no aparecería.") $oldest
    }
} else {
    Add-Finding 'V8.requests-list' 'WARN' "HTTP $($requests.StatusCode)." $requests.RawBody
}

# ---------------------------------------------------------------------------
# §V9 — Flujo end-to-end (solo con -CreateData)
# ---------------------------------------------------------------------------

if ($CreateData) {
    Write-Section "V9-V10: Flujo end-to-end (crea Person + Rule + Request)"

    # 1. ¿Existe ya el Person con ese RFC?
    $existing = Invoke-FiscalApi -Method GET -Path "/api/v4/people?pageNumber=1&pageSize=50"
    $personMatch = ($existing.Body.data.items ?? @()) | Where-Object { $_.tin -eq $TestRfc } | Select-Object -First 1
    if ($personMatch) {
        Add-Finding 'V10.person-precreate' 'INFO' "Person con RFC $TestRfc YA existe (id=$($personMatch.id))." $personMatch
        $personId = $personMatch.id
    } else {
        $createPerson = Invoke-FiscalApi -Method POST -Path '/api/v4/people' -Body @{
            legalName = $TestLegalName
            email     = "probe+$([guid]::NewGuid().ToString('N').Substring(0,8))@millet-erp.test"
            password  = ([guid]::NewGuid().ToString('N') + 'Aa1!').Substring(0, 16)
            tin       = $TestRfc
        }
        Add-RawRequest -Response $createPerson -Label 'create-person'
        if ($createPerson.StatusCode -ge 200 -and $createPerson.StatusCode -lt 300) {
            $personId = $createPerson.Body.data.id
            Add-Finding 'V10.person-precreate' 'PASS' "Person creado on-the-fly (id=$personId)." $createPerson.Body.data
        } else {
            Add-Finding 'V10.person-precreate' 'FAIL' "No se pudo crear Person. HTTP $($createPerson.StatusCode). Body: $($createPerson.RawBody)" $createPerson.Body
            $personId = $null
        }
    }

    if ($personId) {
        # 2. Crear la regla Recibidos × Metadata × Vigente.
        $rule = Invoke-FiscalApi -Method POST -Path '/api/v4/download-rules' -Body @{
            description        = 'Probe — Millet ERP V9 validation'
            personId           = $personId
            satQueryTypeId     = 'Metadata'
            downloadTypeId     = 'Recibidos'
            satInvoiceStatusId = 'Vigente'
        }
        Add-RawRequest -Response $rule -Label 'create-rule'
        if ($rule.StatusCode -ge 200 -and $rule.StatusCode -lt 300) {
            $ruleId = $rule.Body.data.id
            Add-Finding 'V9.create-rule' 'PASS' "Rule creada (id=$ruleId)." $rule.Body.data

            # 3. Crear request para últimos 7 días (sub-31d, dentro del límite).
            $endDate   = (Get-Date).ToString('yyyy-MM-ddT00:00:00')
            $startDate = (Get-Date).AddDays(-7).ToString('yyyy-MM-ddT00:00:00')
            $request = Invoke-FiscalApi -Method POST -Path '/api/v4/download-requests' -Body @{
                downloadRuleId        = $ruleId
                downloadRequestTypeId = 'Manual'
                startDate             = $startDate
                endDate               = $endDate
            }
            Add-RawRequest -Response $request -Label 'create-request'
            if ($request.StatusCode -ge 200 -and $request.StatusCode -lt 300) {
                $reqId = $request.Body.data.id
                Add-Finding 'V9.create-request' 'PASS' ("Request creada (id=$reqId). Estado inicial: " +
                    "satRequestStatusId=$($request.Body.data.satRequestStatusId), " +
                    "downloadRequestStatusId=$($request.Body.data.downloadRequestStatusId).") $request.Body.data

                # 4. Pollear 3× con espera de 30s entre cada uno.
                Write-Host ""
                Write-Host "Polleando estado cada 30s × 3..." -ForegroundColor Gray
                $polls = @()
                for ($i = 1; $i -le 3; $i++) {
                    Start-Sleep -Seconds 30
                    $poll = Invoke-FiscalApi -Method GET -Path "/api/v4/download-requests/$reqId"
                    $polls += [pscustomobject]@{
                        attempt      = $i
                        statusSat    = $poll.Body.data.satRequestStatusId
                        statusReq    = $poll.Body.data.downloadRequestStatusId
                        invoiceCount = $poll.Body.data.invoiceCount
                        nextAttempt  = $poll.Body.data.nextAttemptDate
                    }
                    Write-Host ("  Poll #${i}: satStatus=$($poll.Body.data.satRequestStatusId) reqStatus=$($poll.Body.data.downloadRequestStatusId) count=$($poll.Body.data.invoiceCount)") -ForegroundColor Gray
                }
                Add-Finding 'V9.poll-timing' 'INFO' "Polleo cada 30s × 3 reportado en evidence. Si tras 90s sigue en estado no-terminal, esperar minutos a horas." $polls

                # 5. Intentar listar meta-items (probablemente vacío todavía).
                $meta = Invoke-FiscalApi -Method GET -Path "/api/v4/download-requests/$reqId/meta-items?pageNumber=1&pageSize=10"
                Add-RawRequest -Response $meta -Label 'list-meta-items'
                if ($meta.StatusCode -ge 200 -and $meta.StatusCode -lt 300) {
                    $metaItems = $meta.Body.data.items ?? @()
                    Add-Finding 'V9.meta-items-shape' 'PASS' ("/meta-items respondió OK con $($metaItems.Count) items. Paginado={pageNumber=$($meta.Body.data.pageNumber), totalPages=$($meta.Body.data.totalPages), hasNextPage=$($meta.Body.data.hasNextPage)}. Si vacío, espera la cosecha SAT.") $meta.Body.data
                } else {
                    Add-Finding 'V9.meta-items-shape' 'WARN' "HTTP $($meta.StatusCode) en /meta-items — posiblemente solo disponible cuando estado=Terminada." $meta.RawBody
                }
            } else {
                Add-Finding 'V9.create-request' 'FAIL' "HTTP $($request.StatusCode). Body: $($request.RawBody)" $request.Body
            }
        } else {
            Add-Finding 'V9.create-rule' 'FAIL' "HTTP $($rule.StatusCode). Body: $($rule.RawBody)" $rule.Body
        }
    }
} else {
    Write-Section "V9-V10: SKIPPED (sin -CreateData)"
    Add-Finding 'V9.skipped' 'INFO' "Flujo end-to-end skipped — corre con -CreateData -TestRfc <RFC> -TestLegalName <Razón Social> para validarlo." $null
}

# ---------------------------------------------------------------------------
# Resumen + persistencia del reporte
# ---------------------------------------------------------------------------

Write-Section "RESUMEN"

$summary = $report.findings | Group-Object status | ForEach-Object {
    "{0}: {1}" -f $_.Name, $_.Count
}
Write-Host ($summary -join '  |  ') -ForegroundColor White

$report | ConvertTo-Json -Depth 25 | Set-Content -Path $OutputJson -Encoding UTF8
Write-Host ""
Write-Host "Reporte JSON: $OutputJson" -ForegroundColor Cyan
Write-Host ""
Write-Host "Siguientes pasos:" -ForegroundColor Yellow
Write-Host "  1. Revisa los hallazgos arriba. Cualquier FAIL bloquea PR-9." -ForegroundColor Yellow
Write-Host "  2. Si todo PASS/INFO, actualiza docs/modulos/integraciones-fiscal/02-flujo-asincrono.md §12" -ForegroundColor Yellow
Write-Host "     marcando los [Gap] como resueltos y citando este JSON como evidencia." -ForegroundColor Yellow
Write-Host "  3. Cierra D1-D10 con base en los hallazgos y arranca PR-9." -ForegroundColor Yellow
