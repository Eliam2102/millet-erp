#requires -Version 7.0
<#
.SYNOPSIS
    Probe end-to-end de FiscalAPI sandbox: provisiona Emisor + Receptor +
    CSD + Producto + Factura de prueba, luego crea DownloadRule +
    DownloadRequest y pollea hasta cosecha.

.DESCRIPTION
    A diferencia de Invoke-FiscalApiProbe.ps1 (read-only validation),
    este script ejecuta el flujo completo que la doc de FiscalAPI exige
    para que el sandbox simule la descarga masiva.

    Pasos automatizados (cada uno con checkpoint en state file):

      1. Descargar ZIP de CSDs de prueba (developers.sw.com.mx).
      2. Crear Person Emisor con RFC EKU9003173C9 (configurable).
      3. Subir .cer del emisor (POST /api/v4/tax-files).
      4. Subir .key del emisor (mismo endpoint, fileType=1).
      5. Crear Person Receptor con RFC IIA040805DZ4 (configurable).
      6. Crear Producto genérico.
      7. Emitir factura de prueba Emisor → Receptor (POST /api/v4/invoices/income).
      8. Crear DownloadRule (Recibidos × Metadata × Vigente para el Receptor).
      9. Crear DownloadRequest para últimos N días.
     10. Pollear hasta Completada (configurable; 5 intentos × 30s default).
     11. Listar /meta-items si quedó Terminada.

    Si el script falla en algún paso, los IDs de pasos previos quedan
    persistidos en el state file (default
    .\fiscalapi-probe-e2e-state.json). Al re-correr, skipea pasos ya
    completados.

.PARAMETER TenantKey
    X-TENANT-KEY de FiscalAPI sandbox (env $FISCALAPI_SANDBOX_TENANT_KEY).

.PARAMETER ApiKey
    X-API-KEY de FiscalAPI sandbox (env $FISCALAPI_SANDBOX_API_KEY).

.PARAMETER EmisorRfc
    RFC del emisor (default: EKU9003173C9 — Escuela Kemper Urgate).
    Debe ser uno de los RFCs morales del ZIP de prueba.

.PARAMETER EmisorLegalName
    Razón social del emisor.

.PARAMETER ReceptorRfc
    RFC del receptor (default: IIA040805DZ4 — Industria Iluminadora).

.PARAMETER ReceptorLegalName
    Razón social del receptor.

.PARAMETER ZipCode
    CP del emisor para emitir (default: 42501).

.PARAMETER PollAttempts
    Cuántas veces pollear el DownloadRequest (default: 5).

.PARAMETER PollIntervalSeconds
    Segundos entre polls (default: 30).

.PARAMETER WindowDays
    Tamaño de ventana de descarga (default: 7).

.PARAMETER Reset
    Borra el state file y empieza de cero.

.PARAMETER StateFile
    Path del state file (default: .\fiscalapi-probe-e2e-state.json).

.PARAMETER OutputJson
    Path del reporte final (default: .\fiscalapi-probe-e2e-report.json).

.EXAMPLE
    .\Invoke-FiscalApiProbeE2E.ps1

.EXAMPLE
    # Re-correr desde cero (descarta progreso previo en sandbox)
    .\Invoke-FiscalApiProbeE2E.ps1 -Reset

.NOTES
    Owner: eduardo.paredes@tiglass.net
    Doc:   docs/modulos/integraciones-fiscal/02-flujo-asincrono.md §12
    El SAT no tiene sandbox de descarga masiva; FiscalAPI simula con
    datos sintéticos basados en las facturas que tú emites.
#>
[CmdletBinding()]
param(
    [string]$TenantKey         = $env:FISCALAPI_SANDBOX_TENANT_KEY,
    [string]$ApiKey            = $env:FISCALAPI_SANDBOX_API_KEY,
    [string]$BaseUrl           = 'https://test.fiscalapi.com',
    [string]$TimeZone          = 'America/Mexico_City',
    [string]$EmisorRfc         = 'EKU9003173C9',
    [string]$EmisorLegalName   = 'ESCUELA KEMPER URGATE',
    [string]$ReceptorRfc       = 'IIA040805DZ4',
    [string]$ReceptorLegalName = 'INDUSTRIA ILUMINADORA DE ALMACENES',
    [string]$ZipCode           = '42501',
    [int]$PollAttempts         = 5,
    [int]$PollIntervalSeconds  = 30,
    [int]$WindowDays           = 7,
    [switch]$Reset,
    # Borra solo los IDs de factura/regla/request del state para forzar
    # re-emisión sin perder Person/TaxFile/Producto. Util cuando la
    # factura previa quedó "huérfana" (modo values) y la simulación SAT
    # no la detecta en /api/v4/download-rules/test.
    [switch]$ReissueInvoice,
    [string]$StateFile         = (Join-Path $PSScriptRoot 'fiscalapi-probe-e2e-state.json'),
    [string]$OutputJson        = (Join-Path $PSScriptRoot 'fiscalapi-probe-e2e-report.json')
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($TenantKey)) {
    throw "TenantKey requerido. Pasa -TenantKey o set FISCALAPI_SANDBOX_TENANT_KEY env var."
}
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "ApiKey requerido. Pasa -ApiKey o set FISCALAPI_SANDBOX_API_KEY env var."
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
} else {
    @{}
}

if ($ReissueInvoice) {
    foreach ($k in @('invoiceId','invoiceUuid','ruleId','requestId','requestCreatedAt','requestInitialState','polls','metaItems')) {
        if ($state.ContainsKey($k)) { $state.Remove($k) }
    }
    Write-Host "ReissueInvoice: limpiados invoice/rule/request del state." -ForegroundColor Yellow
}

function Save-State {
    $state | ConvertTo-Json -Depth 12 | Set-Content -Path $StateFile -Encoding UTF8
}

# ---------------------------------------------------------------------------
# HTTP helper
# ---------------------------------------------------------------------------

function Invoke-FiscalApi {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [object]$Body
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
    $elapsedMs = ([DateTimeOffset]::UtcNow - $startedAt).TotalMilliseconds
    $parsedBody = $null
    if (-not [string]::IsNullOrWhiteSpace($resp.Content)) {
        try { $parsedBody = $resp.Content | ConvertFrom-Json -Depth 25 } catch { }
    }
    [pscustomobject]@{
        Url       = $url; Method = $Method; StatusCode = [int]$resp.StatusCode
        ElapsedMs = $elapsedMs; Body = $parsedBody; RawBody = $resp.Content
        Error     = $false
    }
}

function Assert-Success {
    param($Response, [string]$StepName)
    if ($Response.Error -or $Response.StatusCode -lt 200 -or $Response.StatusCode -ge 300) {
        Write-Host ""
        Write-Host "FALLO en $StepName" -ForegroundColor Red
        Write-Host "  URL: $($Response.Url)" -ForegroundColor Red
        Write-Host "  Method: $($Response.Method)" -ForegroundColor Red
        Write-Host "  StatusCode: $($Response.StatusCode)" -ForegroundColor Red
        Write-Host "  Body:" -ForegroundColor Red
        Write-Host ($Response.RawBody ?? '(vacío)') -ForegroundColor Red
        Write-Host ""
        Write-Host "El state file $StateFile conserva los IDs ya creados — re-corre el script para reintentar desde este paso." -ForegroundColor Yellow
        throw "Paso '$StepName' falló."
    }
}

function Write-Step {
    param([string]$Num, [string]$Title)
    Write-Host ""
    Write-Host ("─" * 70) -ForegroundColor Cyan
    Write-Host "PASO $Num : $Title" -ForegroundColor Cyan
    Write-Host ("─" * 70) -ForegroundColor Cyan
}

function Write-Skip {
    param([string]$Num, [string]$Detail)
    Write-Host "[skip] Paso $Num ya completado en corrida previa: $Detail" -ForegroundColor Gray
}

# Lee el Subject CN de un .cer del SAT — punto de partida para la razón
# social que SAT exige en el CFDI (validaciones CFDI40139 / CFDI40145).
function Get-CertCommonName {
    param([Parameter(Mandatory)][string]$CerPath)
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CerPath)
    # Subject viene como "..., CN=NAME" o "CN=NAME, ...". Extraemos CN.
    $subject = $cert.Subject
    $m = [regex]::Match($subject, 'CN=([^,]+)')
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
    throw "No se pudo extraer CN del subject del cert ($subject)."
}

# Catálogo de RFCs de prueba de FiscalAPI/SAT, con sus CPs oficiales
# (validación CFDI40148: DomicilioFiscalReceptor debe coincidir con
# el registrado en SAT). Fuente: https://docs.fiscalapi.com/testing-data
# Si pasas un RFC fuera de este catálogo y no especificas -ZipCode, el
# script usa el default que viene del parámetro.
$Script:TestRfcCatalog = @{
    'EKU9003173C9' = @{ Cp = '42501' }
    'IIA040805DZ4' = @{ Cp = '62661' }
    'H&E951128469' = @{ Cp = '06002' }
    'IVD920810GU2' = @{ Cp = '63901' }
    'IXS7607092R5' = @{ Cp = '23004' }
    'JES900109Q90' = @{ Cp = '37161' }
    'KIJ0906199R1' = @{ Cp = '28971' }
    'L&O950913MSA' = @{ Cp = '60922' }
    'OÑO120726RX3' = @{ Cp = '40501' }
    'S&S051221SE2' = @{ Cp = '76022' }
    'URE180429TM6' = @{ Cp = '86991' }
    'XIA190128J61' = @{ Cp = '76343' }
    'ZUÑ920208KL4' = @{ Cp = '34541' }
}

function Get-TestRfcZipCode {
    param([Parameter(Mandatory)][string]$Rfc, [string]$Fallback)
    if ($Script:TestRfcCatalog.ContainsKey($Rfc)) {
        return $Script:TestRfcCatalog[$Rfc].Cp
    }
    return $Fallback
}

# CFDI 4.0 exige razón social EN MAYÚSCULAS Y SIN RÉGIMEN SOCIETARIO.
# El cert SAT lleva el nombre legal completo (incluye "SA DE CV", etc.);
# para el CFDI hay que quitar el sufijo. Esta función cubre los regímenes
# societarios más comunes en México.
function Remove-RegimenSocietario {
    param([Parameter(Mandatory)][string]$Nombre)
    # Patrón: espacio + (régimen con o sin puntos y espacios) al FINAL.
    $patterns = @(
        '\s+S\.?A\.?P\.?I\.?\s+DE\s+C\.?V\.?$',   # SAPI DE CV / S.A.P.I. DE C.V.
        '\s+S\.?A\.?B\.?\s+DE\s+C\.?V\.?$',       # SAB DE CV
        '\s+S\.?\s+DE\s+R\.?L\.?\s+DE\s+C\.?V\.?$', # S DE RL DE CV
        '\s+S\.?A\.?\s+DE\s+C\.?V\.?$',           # SA DE CV / S.A. DE C.V.
        '\s+S\.?\s+EN\s+C\.?$',                   # S EN C
        '\s+S\.?A\.?S\.?$',                        # SAS / S.A.S.
        '\s+S\.?A\.?$',                            # SA / S.A.
        '\s+A\.?C\.?$',                            # AC / A.C.
        '\s+S\.?C\.?$'                             # SC / S.C.
    )
    $clean = $Nombre.ToUpperInvariant().Trim()
    foreach ($p in $patterns) {
        $clean = [regex]::Replace($clean, $p, '', 'IgnoreCase')
    }
    return $clean.Trim()
}

function Resolve-CerForRfc {
    param([Parameter(Mandatory)][string]$CsdRoot, [Parameter(Mandatory)][string]$Rfc)
    $folder = Get-ChildItem -Path $CsdRoot -Recurse -Directory |
        Where-Object { $_.Name -match [regex]::Escape($Rfc) } |
        Select-Object -First 1
    if (-not $folder) { throw "No se encontró carpeta de $Rfc bajo $CsdRoot." }
    $cer = Get-ChildItem -Path $folder.FullName -Filter '*.cer' | Select-Object -First 1
    if (-not $cer) { throw "No hay .cer en $($folder.FullName)." }
    return $cer.FullName
}

# ===========================================================================
# Paso 1 — Descargar y extraer ZIP de CSDs de prueba
# ===========================================================================

Write-Step "1" "Descargar ZIP de certificados de prueba"

$zipUrl  = 'https://developers.sw.com.mx/wp-content/uploads/2023/07/Certificados_Pruebas.zip'
$zipPath = Join-Path $PSScriptRoot 'Certificados_Pruebas.zip'
$csdDir  = Join-Path $PSScriptRoot 'csd-test'

if ($state.csdDir -and (Test-Path $state.csdDir)) {
    Write-Skip "1" "csd-test ya extraído en $($state.csdDir)"
} else {
    if (-not (Test-Path $zipPath)) {
        Write-Host "Descargando $zipUrl ..."
        Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -TimeoutSec 60
    } else {
        Write-Host "ZIP ya presente en $zipPath" -ForegroundColor Gray
    }
    if (-not (Test-Path $csdDir)) {
        Expand-Archive -Path $zipPath -DestinationPath $csdDir -Force
    }
    $state.csdDir = $csdDir
    Save-State
    Write-Host "Extraído en $csdDir"
}

# Resolver paths de .cer y .key del emisor
$emisorFolder = Get-ChildItem -Path $state.csdDir -Recurse -Directory |
    Where-Object { $_.Name -match [regex]::Escape($EmisorRfc) } |
    Select-Object -First 1
if (-not $emisorFolder) {
    throw "No se encontró carpeta con RFC $EmisorRfc bajo $($state.csdDir). RFCs disponibles: $((Get-ChildItem -Path $state.csdDir -Recurse -Directory | Select-Object -ExpandProperty Name) -join ', ')"
}
$cerFile = Get-ChildItem -Path $emisorFolder.FullName -Filter '*.cer' | Select-Object -First 1
$keyFile = Get-ChildItem -Path $emisorFolder.FullName -Filter '*.key' | Select-Object -First 1
if (-not $cerFile -or -not $keyFile) {
    throw "Faltan .cer o .key en $($emisorFolder.FullName)."
}
Write-Host ("Certificados localizados: " + $cerFile.Name + ", " + $keyFile.Name) -ForegroundColor Green

# Resolver legalName desde el Subject CN del .cer, quitando régimen
# societario. CFDI 4.0 valida (CFDI40139 / CFDI40145) el campo Nombre
# contra el registro SAT, que está SIN régimen societario en mayúsculas.
$emisorCN     = Get-CertCommonName -CerPath $cerFile.FullName
$emisorClean  = Remove-RegimenSocietario -Nombre $emisorCN
Write-Host ("Emisor   CN crudo: '$emisorCN'") -ForegroundColor Gray
Write-Host ("Emisor   CN limpio (para CFDI): '$emisorClean'") -ForegroundColor Green
$EmisorLegalName = $emisorClean

$receptorCerPath = Resolve-CerForRfc -CsdRoot $state.csdDir -Rfc $ReceptorRfc
$receptorCN      = Get-CertCommonName -CerPath $receptorCerPath
$receptorClean   = Remove-RegimenSocietario -Nombre $receptorCN
Write-Host ("Receptor CN crudo: '$receptorCN'") -ForegroundColor Gray
Write-Host ("Receptor CN limpio (para CFDI): '$receptorClean'") -ForegroundColor Green
$ReceptorLegalName = $receptorClean

# Resolver código postal OFICIAL por RFC (validación CFDI40148).
$EmisorZipCode   = Get-TestRfcZipCode -Rfc $EmisorRfc   -Fallback $ZipCode
$ReceptorZipCode = Get-TestRfcZipCode -Rfc $ReceptorRfc -Fallback $ZipCode
Write-Host ("Emisor   CP oficial: $EmisorZipCode") -ForegroundColor Green
Write-Host ("Receptor CP oficial: $ReceptorZipCode") -ForegroundColor Green

# ===========================================================================
# Paso 2 — Crear Person Emisor
# ===========================================================================

Write-Step "2" "Crear Person Emisor ($EmisorRfc)"

if ($state.emisorId) {
    Write-Skip "2" "emisorId=$($state.emisorId)"
} else {
    # Si ya existe en FiscalAPI, lo reusamos.
    $list = Invoke-FiscalApi -Method GET -Path "/api/v4/people?pageNumber=1&pageSize=50"
    Assert-Success $list "list-people"
    $found = ($list.Body.data.items ?? @()) | Where-Object { $_.tin -eq $EmisorRfc } | Select-Object -First 1
    if ($found) {
        $state.emisorId = $found.id
        Write-Host "Reusando Person existente con RFC=$EmisorRfc (id=$($found.id))." -ForegroundColor Yellow
    } else {
        $body = @{
            legalName       = $EmisorLegalName
            email           = "emisor+$([guid]::NewGuid().ToString('N').Substring(0,8))@millet-erp.test"
            password        = 'Probe.Mil123!'
            tin             = $EmisorRfc
            taxPassword     = '12345678a'
            satTaxRegimeId  = '601'   # General de Ley Personas Morales
            zipCode         = $ZipCode
        }
        $resp = Invoke-FiscalApi -Method POST -Path '/api/v4/people' -Body $body
        Assert-Success $resp "create-emisor"
        $state.emisorId = $resp.Body.data.id
        Write-Host "Emisor creado: id=$($resp.Body.data.id)" -ForegroundColor Green
    }
    Save-State
}

# ===========================================================================
# Paso 3 — Subir .cer del emisor
# ===========================================================================

Write-Step "3" "Subir .cer del emisor (tax-file fileType=0)"

if ($state.cerFileId) {
    Write-Skip "3" "cerFileId=$($state.cerFileId)"
} else {
    $cerBytes  = [System.IO.File]::ReadAllBytes($cerFile.FullName)
    $cerBase64 = [Convert]::ToBase64String($cerBytes)
    $resp = Invoke-FiscalApi -Method POST -Path '/api/v4/tax-files' -Body @{
        personId   = $state.emisorId
        tin        = $EmisorRfc
        base64File = $cerBase64
        fileType   = 0
        password   = '12345678a'
    }
    Assert-Success $resp "upload-cer"
    $state.cerFileId = $resp.Body.data.id
    Save-State
    Write-Host ".cer subido: id=$($resp.Body.data.id), validFrom=$($resp.Body.data.validFrom), validTo=$($resp.Body.data.validTo)" -ForegroundColor Green
}

# ===========================================================================
# Paso 4 — Subir .key del emisor
# ===========================================================================

Write-Step "4" "Subir .key del emisor (tax-file fileType=1)"

if ($state.keyFileId) {
    Write-Skip "4" "keyFileId=$($state.keyFileId)"
} else {
    $keyBytes  = [System.IO.File]::ReadAllBytes($keyFile.FullName)
    $keyBase64 = [Convert]::ToBase64String($keyBytes)
    $resp = Invoke-FiscalApi -Method POST -Path '/api/v4/tax-files' -Body @{
        personId   = $state.emisorId
        tin        = $EmisorRfc
        base64File = $keyBase64
        fileType   = 1
        password   = '12345678a'
    }
    Assert-Success $resp "upload-key"
    $state.keyFileId = $resp.Body.data.id
    Save-State
    Write-Host ".key subido: id=$($resp.Body.data.id)" -ForegroundColor Green
}

# ===========================================================================
# Paso 4b — Asegurar certs del SDK en Person emisor
# El cert del ZIP (serial 30001000000500003415) NO está en la LCO
# sintética del simulador SAT. El cert del SDK sample (serial 3416) SÍ.
# Para que la simulación POST /api/v4/download-rules/test detecte
# nuestras facturas, el emisor debe timbrar con el cert "bueno" via
# by-reference. Reemplazamos los tax-files del Person emisor.
# ===========================================================================

Write-Step "4b" "Asegurar certs del SDK en Person emisor (reemplaza los del ZIP)"

$SdkSampleEmisorCer = 'MIIFsDCCA5igAwIBAgIUMzAwMDEwMDAwMDA1MDAwMDM0MTYwDQYJKoZIhvcNAQELBQAwggErMQ8wDQYDVQQDDAZBQyBVQVQxLjAsBgNVBAoMJVNFUlZJQ0lPIERFIEFETUlOSVNUUkFDSU9OIFRSSUJVVEFSSUExGjAYBgNVBAsMEVNBVC1JRVMgQXV0aG9yaXR5MSgwJgYJKoZIhvcNAQkBFhlvc2Nhci5tYXJ0aW5lekBzYXQuZ29iLm14MR0wGwYDVQQJDBQzcmEgY2VycmFkYSBkZSBjYWxpejEOMAwGA1UEEQwFMDYzNzAxCzAJBgNVBAYTAk1YMRkwFwYDVQQIDBBDSVVEQUQgREUgTUVYSUNPMREwDwYDVQQHDAhDT1lPQUNBTjERMA8GA1UELRMIMi41LjQuNDUxJTAjBgkqhkiG9w0BCQITFnJlc3BvbnNhYmxlOiBBQ0RNQS1TQVQwHhcNMjMwNTE4MTE0MzUxWhcNMjcwNTE4MTE0MzUxWjCB1zEnMCUGA1UEAxMeRVNDVUVMQSBLRU1QRVIgVVJHQVRFIFNBIERFIENWMScwJQYDVQQpEx5FU0NVRUxBIEtFTVBFUiBVUkdBVEUgU0EgREUgQ1YxJzAlBgNVBAoTHkVTQ1VFTEEgS0VNUEVSIFVSR0FURSBTQSBERSBDVjElMCMGA1UELRMcRUtVOTAwMzE3M0M5IC8gVkFEQTgwMDkyN0RKMzEeMBwGA1UEBRMVIC8gVkFEQTgwMDkyN0hTUlNSTDA1MRMwEQYDVQQLEwpTdWN1cnNhbCAxMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAtmecO6n2GS0zL025gbHGQVxznPDICoXzR2uUngz4DqxVUC/w9cE6FxSiXm2ap8Gcjg7wmcZfm85EBaxCx/0J2u5CqnhzIoGCdhBPuhWQnIh5TLgj/X6uNquwZkKChbNe9aeFirU/JbyN7Egia9oKH9KZUsodiM/pWAH00PCtoKJ9OBcSHMq8Rqa3KKoBcfkg1ZrgueffwRLws9yOcRWLb02sDOPzGIm/jEFicVYt2Hw1qdRE5xmTZ7AGG0UHs+unkGjpCVeJ+BEBn0JPLWVvDKHZAQMj6s5Bku35+d/MyATkpOPsGT/VTnsouxekDfikJD1f7A1ZpJbqDpkJnss3vQIDAQABox0wGzAMBgNVHRMBAf8EAjAAMAsGA1UdDwQEAwIGwDANBgkqhkiG9w0BAQsFAAOCAgEAFaUgj5PqgvJigNMgtrdXZnbPfVBbukAbW4OGnUhNrA7SRAAfv2BSGk16PI0nBOr7qF2mItmBnjgEwk+DTv8Zr7w5qp7vleC6dIsZFNJoa6ZndrE/f7KO1CYruLXr5gwEkIyGfJ9NwyIagvHHMszzyHiSZIA850fWtbqtythpAliJ2jF35M5pNS+YTkRB+T6L/c6m00ymN3q9lT1rB03YywxrLreRSFZOSrbwWfg34EJbHfbFXpCSVYdJRfiVdvHnewN0r5fUlPtR9stQHyuqewzdkyb5jTTw02D2cUfL57vlPStBj7SEi3uOWvLrsiDnnCIxRMYJ2UA2ktDKHk+zWnsDmaeleSzonv2CHW42yXYPCvWi88oE1DJNYLNkIjua7MxAnkNZbScNw01A6zbLsZ3y8G6eEYnxSTRfwjd8EP4kdiHNJftm7Z4iRU7HOVh79/lRWB+gd171s3d/mI9kte3MRy6V8MMEMCAnMboGpaooYwgAmwclI2XZCczNWXfhaWe0ZS5PmytD/GDpXzkX0oEgY9K/uYo5V77NdZbGAjmyi8cE2B2ogvyaN2XfIInrZPgEffJ4AB7kFA2mwesdLOCh0BLD9itmCve3A1FGR4+stO2ANUoiI3w3Tv2yQSg4bjeDlJ08lXaaFCLW2peEXMXjQUk7fmpb5MNuOUTW6BE='
$SdkSampleEmisorKey = 'MIIFDjBABgkqhkiG9w0BBQ0wMzAbBgkqhkiG9w0BBQwwDgQIAgEAAoIBAQACAggAMBQGCCqGSIb3DQMHBAgwggS/AgEAMASCBMh4EHl7aNSCaMDA1VlRoXCZ5UUmqErAbucoZQObOaLUEm+I+QZ7Y8Giupo+F1XWkLvAsdk/uZlJcTfKLJyJbJwsQYbSpLOCLataZ4O5MVnnmMbfG//NKJn9kSMvJQZhSwAwoGLYDm1ESGezrvZabgFJnoQv8Si1nAhVGTk9FkFBesxRzq07dmZYwFCnFSX4xt2fDHs1PMpQbeq83aL/PzLCce3kxbYSB5kQlzGtUYayiYXcu0cVRu228VwBLCD+2wTDDoCmRXtPesgrLKUR4WWWb5N2AqAU1mNDC+UEYsENAerOFXWnmwrcTAu5qyZ7GsBMTpipW4Dbou2yqQ0lpA/aB06n1kz1aL6mNqGPaJ+OqoFuc8Ugdhadd+MmjHfFzoI20SZ3b2geCsUMNCsAd6oXMsZdWm8lzjqCGWHFeol0ik/xHMQvuQkkeCsQ28PBxdnUgf7ZGer+TN+2ZLd2kvTBOk6pIVgy5yC6cZ+o1Tloql9hYGa6rT3xcMbXlW+9e5jM2MWXZliVW3ZhaPjptJFDbIfWxJPjz4QvKyJk0zok4muv13Iiwj2bCyefUTRz6psqI4cGaYm9JpscKO2RCJN8UluYGbbWmYQU+Int6LtZj/lv8p6xnVjWxYI+rBPdtkpfFYRp+MJiXjgPw5B6UGuoruv7+vHjOLHOotRo+RdjZt7NqL9dAJnl1Qb2jfW6+d7NYQSI/bAwxO0sk4taQIT6Gsu/8kfZOPC2xk9rphGqCSS/4q3Os0MMjA1bcJLyoWLp13pqhK6bmiiHw0BBXH4fbEp4xjSbpPx4tHXzbdn8oDsHKZkWh3pPC2J/nVl0k/yF1KDVowVtMDXE47k6TGVcBoqe8PDXCG9+vjRpzIidqNo5qebaUZu6riWMWzldz8x3Z/jLWXuDiM7/Yscn0Z2GIlfoeyz+GwP2eTdOw9EUedHjEQuJY32bq8LICimJ4Ht+zMJKUyhwVQyAER8byzQBwTYmYP5U0wdsyIFitphw+/IH8+v08Ia1iBLPQAeAvRfTTIFLCs8foyUrj5Zv2B/wTYIZy6ioUM+qADeXyo45uBLLqkN90Rf6kiTqDld78NxwsfyR5MxtJLVDFkmf2IMMJHTqSfhbi+7QJaC11OOUJTD0v9wo0X/oO5GvZhe0ZaGHnm9zqTopALuFEAxcaQlc4R81wjC4wrIrqWnbcl2dxiBtD73KW+wcC9ymsLf4I8BEmiN25lx/OUc1IHNyXZJYSFkEfaxCEZWKcnbiyf5sqFSSlEqZLc4lUPJFAoP6s1FHVcyO0odWqdadhRZLZC9RCzQgPlMRtji/OXy5phh7diOBZv5UYp5nb+MZ2NAB/eFXm2JLguxjvEstuvTDmZDUb6Uqv++RdhO5gvKf/AcwU38ifaHQ9uvRuDocYwVxZS2nr9rOwZ8nAh+P2o4e0tEXjxFKQGhxXYkn75H3hhfnFYjik/2qunHBBZfcdG148MaNP6DjX33M238T9Zw/GyGx00JMogr2pdP4JAErv9a5yt4YR41KGf8guSOUbOXVARw6+ybh7+meb7w4BeTlj3aZkv8tVGdfIt3lrwVnlbzhLjeQY6PplKp3/a5Kr5yM0T4wJoKQQ6v3vSNmrhpbuAtKxpMILe8CQoo='

if ($state.usedSdkCerts -eq $true) {
    Write-Skip "4b" "El Person emisor ya tiene los certs del SDK (state.usedSdkCerts=true)"
} else {
    # 1) Listar tax-files actuales y borrarlos.
    $existing = Invoke-FiscalApi -Method GET -Path "/api/v4/tax-files/$($state.emisorId)/default-values"
    Assert-Success $existing "get-existing-taxfiles"
    foreach ($tf in $existing.Body.data) {
        $del = Invoke-FiscalApi -Method DELETE -Path "/api/v4/tax-files/$($tf.id)"
        if ($del.StatusCode -ge 200 -and $del.StatusCode -lt 300) {
            Write-Host "  borrado tax-file id=$($tf.id) (fileType=$($tf.fileType))" -ForegroundColor Gray
        } else {
            Write-Host "  no se pudo borrar tax-file $($tf.id): HTTP $($del.StatusCode) — continuando" -ForegroundColor Yellow
        }
    }
    # Limpiar refs viejos en state.
    if ($state.ContainsKey('cerFileId')) { $state.Remove('cerFileId') }
    if ($state.ContainsKey('keyFileId')) { $state.Remove('keyFileId') }

    # 2) Subir cert del SDK al Person emisor.
    $resp = Invoke-FiscalApi -Method POST -Path '/api/v4/tax-files' -Body @{
        personId   = $state.emisorId
        tin        = $EmisorRfc
        base64File = $SdkSampleEmisorCer
        fileType   = 0
        password   = '12345678a'
    }
    Assert-Success $resp "upload-sdk-cer"
    $state.cerFileId = $resp.Body.data.id
    Write-Host "SDK .cer subido al Person emisor: id=$($resp.Body.data.id)" -ForegroundColor Green

    # 3) Subir key del SDK.
    $resp = Invoke-FiscalApi -Method POST -Path '/api/v4/tax-files' -Body @{
        personId   = $state.emisorId
        tin        = $EmisorRfc
        base64File = $SdkSampleEmisorKey
        fileType   = 1
        password   = '12345678a'
    }
    Assert-Success $resp "upload-sdk-key"
    $state.keyFileId = $resp.Body.data.id
    Write-Host "SDK .key subida al Person emisor: id=$($resp.Body.data.id)" -ForegroundColor Green

    $state.usedSdkCerts = $true
    Save-State
}

# ===========================================================================
# Paso 5 — Crear Person Receptor
# ===========================================================================

Write-Step "5" "Crear Person Receptor ($ReceptorRfc)"

if ($state.receptorId) {
    Write-Skip "5" "receptorId=$($state.receptorId)"
} else {
    $list = Invoke-FiscalApi -Method GET -Path "/api/v4/people?pageNumber=1&pageSize=50"
    Assert-Success $list "list-people-2"
    $found = ($list.Body.data.items ?? @()) | Where-Object { $_.tin -eq $ReceptorRfc } | Select-Object -First 1
    if ($found) {
        $state.receptorId = $found.id
        Write-Host "Reusando Person existente con RFC=$ReceptorRfc (id=$($found.id))." -ForegroundColor Yellow
    } else {
        $body = @{
            legalName      = $ReceptorLegalName
            email          = "receptor+$([guid]::NewGuid().ToString('N').Substring(0,8))@millet-erp.test"
            password       = 'Probe.Mil123!'
            tin            = $ReceptorRfc
            satTaxRegimeId = '601'
            satCfdiUseId   = 'G03'    # Gastos en general
            zipCode        = $ZipCode
        }
        $resp = Invoke-FiscalApi -Method POST -Path '/api/v4/people' -Body $body
        Assert-Success $resp "create-receptor"
        $state.receptorId = $resp.Body.data.id
        Write-Host "Receptor creado: id=$($resp.Body.data.id)" -ForegroundColor Green
    }
    Save-State
}

# ===========================================================================
# Paso 5b — Sincronizar legalName del receptor con el CN del .cer
# El SAT valida CFDI40145: el nombre del receptor en el CFDI debe coincidir
# EXACTAMENTE con el registrado en SAT para ese RFC. El CN del .cer público
# de prueba es la fuente de verdad.
# ===========================================================================

Write-Step "5b" "Sync legalName de receptor con CN del .cer"

$current = Invoke-FiscalApi -Method GET -Path "/api/v4/people/$($state.receptorId)"
Assert-Success $current "get-receptor-current"
$needsUpdate = ($current.Body.data.legalName -cne $ReceptorLegalName) -or `
               ($current.Body.data.zipCode   -ne  $ReceptorZipCode)
if (-not $needsUpdate) {
    Write-Host "Receptor ya sincronizado: legalName='$($current.Body.data.legalName)', zipCode=$($current.Body.data.zipCode)" -ForegroundColor Gray
} else {
    Write-Host "Sincronizando receptor con SAT:" -ForegroundColor Yellow
    Write-Host "  legalName: '$($current.Body.data.legalName)' -> '$ReceptorLegalName'"
    Write-Host "  zipCode:   '$($current.Body.data.zipCode)' -> '$ReceptorZipCode'"
    $updateBody = @{
        id             = $state.receptorId
        legalName      = $ReceptorLegalName
        email          = $current.Body.data.email
        tin            = $ReceptorRfc
        satTaxRegimeId = $current.Body.data.satTaxRegimeId ?? '601'
        satCfdiUseId   = $current.Body.data.satCfdiUseId   ?? 'G03'
        zipCode        = $ReceptorZipCode
    }
    $upd = Invoke-FiscalApi -Method PUT -Path "/api/v4/people/$($state.receptorId)" -Body $updateBody
    Assert-Success $upd "update-receptor-sync"
    Write-Host "Receptor sincronizado." -ForegroundColor Green
}

# Mismo sync para el emisor. Como vamos a hacer self-invoice (emisor =
# receptor en paso 7), también necesitamos que el emisor tenga
# satCfdiUseId — sin él, el CFDI falla con error de "uso CFDI faltante".
$currentEm = Invoke-FiscalApi -Method GET -Path "/api/v4/people/$($state.emisorId)"
Assert-Success $currentEm "get-emisor-current"
$needsUpdateEm = ($currentEm.Body.data.legalName -cne $EmisorLegalName) -or `
                 ($currentEm.Body.data.zipCode   -ne  $EmisorZipCode) -or `
                 ([string]::IsNullOrEmpty($currentEm.Body.data.satCfdiUseId))
if (-not $needsUpdateEm) {
    Write-Host "Emisor ya sincronizado: legalName='$($currentEm.Body.data.legalName)', zipCode=$($currentEm.Body.data.zipCode), satCfdiUseId='$($currentEm.Body.data.satCfdiUseId)'" -ForegroundColor Gray
} else {
    Write-Host "Sincronizando emisor con SAT (para self-invoice):" -ForegroundColor Yellow
    Write-Host "  legalName:    '$($currentEm.Body.data.legalName)' -> '$EmisorLegalName'"
    Write-Host "  zipCode:      '$($currentEm.Body.data.zipCode)' -> '$EmisorZipCode'"
    Write-Host "  satCfdiUseId: '$($currentEm.Body.data.satCfdiUseId)' -> 'G03'"
    $updateBody = @{
        id             = $state.emisorId
        legalName      = $EmisorLegalName
        email          = $currentEm.Body.data.email
        tin            = $EmisorRfc
        taxPassword    = '12345678a'
        satTaxRegimeId = $currentEm.Body.data.satTaxRegimeId ?? '601'
        satCfdiUseId   = 'G03'   # Gastos en general — válido para self-invoice
        zipCode        = $EmisorZipCode
    }
    $upd = Invoke-FiscalApi -Method PUT -Path "/api/v4/people/$($state.emisorId)" -Body $updateBody
    Assert-Success $upd "update-emisor-sync"
    Write-Host "Emisor sincronizado." -ForegroundColor Green
}

# ===========================================================================
# Paso 6 — Crear Producto
# ===========================================================================

Write-Step "6" "Crear Producto de prueba"

if ($state.productId) {
    Write-Skip "6" "productId=$($state.productId)"
} else {
    $body = @{
        description = 'Servicio de prueba — Millet ERP probe'
        unitPrice   = 1000
    }
    $resp = Invoke-FiscalApi -Method POST -Path '/api/v4/products' -Body $body
    Assert-Success $resp "create-product"
    $state.productId = $resp.Body.data.id
    Save-State
    Write-Host "Producto creado: id=$($resp.Body.data.id)" -ForegroundColor Green
}

# ===========================================================================
# Paso 7 — Emitir factura de prueba
# ===========================================================================

Write-Step "7" "Emitir factura (POST /api/v4/invoices) — modo by-references"

# Modo BY-REFERENCES — issuer/recipient/items referenciados por id. Esta
# es la forma "registrada" en el tenant: FiscalAPI vincula la factura al
# Person.id del emisor (necesario para que la simulación SAT del
# endpoint /download-rules/test la detecte). Pre-requisito: el Person
# emisor ya tiene los certs del SDK sample (serial 30001000000500003416,
# que sí está en LCO sintética) — eso se aseguró en el paso 4b.

if ($state.invoiceId) {
    Write-Skip "7" "invoiceId=$($state.invoiceId)"
} else {
    # La fecha del CFDI debe estar en hora México (CST/CDT) — el servidor
    # SAT acepta un rango ±72h respecto a su hora local. Si la máquina del
    # cliente está en otra TZ (ej. Europa), Get-Date local cae fuera del
    # rango. Convertimos UTC -> America/Mexico_City explícitamente.
    $mexicoTz = try {
        [TimeZoneInfo]::FindSystemTimeZoneById('America/Mexico_City')
    } catch {
        # Windows IDs (PowerShell pre-IANA-support)
        [TimeZoneInfo]::FindSystemTimeZoneById('Central Standard Time (Mexico)')
    }
    $nowMexico = [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $mexicoTz)
    $dateMexico = $nowMexico.ToString('yyyy-MM-ddTHH:mm:ss')
    Write-Host "Fecha CFDI (hora México): $dateMexico" -ForegroundColor Gray

    $body = @{
        versionCode        = '4.0'
        series             = 'F'
        date               = $dateMexico
        paymentFormCode    = '01'
        paymentMethodCode  = 'PUE'
        currencyCode       = 'MXN'
        typeCode           = 'I'
        expeditionZipCode  = $EmisorZipCode
        exchangeRate       = 1
        exportCode         = '01'
        issuer             = @{ id = $state.emisorId }
        # SELF-INVOICE (EKU emite hacia EKU). En sandbox, FiscalAPI ignora
        # el personId que pasamos a /download-rules/test y crea una rule
        # fija sobre el primer Person del tenant con cert válido (el
        # emisor EKU). Para que la rule de simulación encuentre nuestra
        # factura, EKU debe ser el receptor. Self-invoicing es legal en
        # CFDI 4.0 y FiscalAPI lo soporta.
        recipient          = @{ id = $state.emisorId }
        items              = @(
            @{
                id       = $state.productId
                quantity = 1
            }
        )
    }
    $resp = Invoke-FiscalApi -Method POST -Path '/api/v4/invoices' -Body $body
    Assert-Success $resp "emit-invoice"
    $state.invoiceId = $resp.Body.data.id
    $state.invoiceUuid = $resp.Body.data.fiscalSchema?.complement?.taxStamp?.uuid ?? $resp.Body.data.uuid
    Save-State
    Write-Host "Factura emitida: id=$($resp.Body.data.id), UUID=$($state.invoiceUuid)" -ForegroundColor Green
}

# ===========================================================================
# Paso 8 — Crear DownloadRule
# ===========================================================================

Write-Step "8" "Crear DownloadRule sandbox (POST /api/v4/download-rules/test)"

# IMPORTANTE: En sandbox, FiscalAPI bloquea POST /api/v4/download-rules y
# requiere el endpoint específico /test, que activa la simulación SAT
# usando las facturas que tú emitiste hacia el RFC receptor (paso 7).
# En producción se usa /api/v4/download-rules (sin /test) y va al SAT real.

if ($state.ruleId) {
    Write-Skip "8" "ruleId=$($state.ruleId)"
} else {
    # Antes de POST /test: verificar si ya existe una rule isTest=true en
    # el tenant. FiscalAPI ignora el personId del body y siempre crea una
    # rule fija para el primer Person con cert válido (el emisor). Si ya
    # corrimos /test antes, la rule existe y el POST puede devolver otra
    # representación (wrapper). Listamos y resolvemos el ID real.
    $list = Invoke-FiscalApi -Method GET -Path '/api/v4/download-rules?pageNumber=1&pageSize=20'
    Assert-Success $list "list-rules-before-test"
    $existing = ($list.Body.data.items ?? @()) | Where-Object { $_.isTest -eq $true } | Select-Object -First 1
    if ($existing) {
        $state.ruleId = $existing.id
        Write-Host "Rule de simulación ya existente: id=$($existing.id), tin=$($existing.tin), satQueryType=$($existing.satQueryType.id)" -ForegroundColor Yellow
    } else {
        $body = @{
            description        = 'Probe Millet E2E — Recibidos Vigente (sandbox /test)'
            personId           = $state.receptorId
            satQueryTypeId     = 'Metadata'
            downloadTypeId     = 'Recibidos'
            satInvoiceStatusId = 'Vigente'
        }
        $resp = Invoke-FiscalApi -Method POST -Path '/api/v4/download-rules/test' -Body $body
        Assert-Success $resp "create-rule-test"
        # El response del POST /test puede tener shape distinto al GET. Re-listar
        # para obtener el ID canónico de la rule recién creada.
        $list2 = Invoke-FiscalApi -Method GET -Path '/api/v4/download-rules?pageNumber=1&pageSize=20'
        Assert-Success $list2 "list-rules-after-test"
        $created = ($list2.Body.data.items ?? @()) | Where-Object { $_.isTest -eq $true } |
                   Sort-Object createdAt -Descending | Select-Object -First 1
        if (-not $created) {
            throw "POST /download-rules/test devolvió OK pero no se encontró rule isTest=true en la lista. Body: $($resp.RawBody)"
        }
        $state.ruleId = $created.id
        Write-Host "Rule creada (ID resuelto vía GET): id=$($created.id), tin=$($created.tin), satQueryType=$($created.satQueryType.id)" -ForegroundColor Green
    }
    Save-State
}

# ===========================================================================
# Paso 9 — Crear DownloadRequest
# ===========================================================================

Write-Step "9" "Crear DownloadRequest (últimos $WindowDays días)"

if ($state.requestId) {
    Write-Skip "9" "requestId=$($state.requestId) — para crear una NUEVA pasa -Reset"
} else {
    $endDate   = (Get-Date).ToString('yyyy-MM-ddT00:00:00')
    $startDate = (Get-Date).AddDays(-$WindowDays).ToString('yyyy-MM-ddT00:00:00')
    $body = @{
        downloadRuleId        = $state.ruleId
        downloadRequestTypeId = 'Manual'
        startDate             = $startDate
        endDate               = $endDate
    }
    # IMPORTANTE: solo /download-rules tiene variante /test en sandbox.
    # Los demás endpoints (requests, meta-items, etc.) son los normales
    # apuntando a test.fiscalapi.com. Confirmado por el owner.
    $resp = Invoke-FiscalApi -Method POST -Path '/api/v4/download-requests' -Body $body
    Assert-Success $resp "create-request"
    $state.requestId           = $resp.Body.data.id
    $state.requestCreatedAt    = $resp.Body.data.createdAt
    $state.requestInitialState = "$($resp.Body.data.satRequestStatusId)/$($resp.Body.data.downloadRequestStatusId)"
    Save-State
    Write-Host "Request creada: id=$($resp.Body.data.id), estado inicial: satStatus=$($resp.Body.data.satRequestStatusId), reqStatus=$($resp.Body.data.downloadRequestStatusId)" -ForegroundColor Green
}

# ===========================================================================
# Paso 10 — Pollear el DownloadRequest
# ===========================================================================

Write-Step "10" "Pollear DownloadRequest ($PollAttempts × ${PollIntervalSeconds}s)"

$polls = @()
for ($i = 1; $i -le $PollAttempts; $i++) {
    if ($i -gt 1) { Start-Sleep -Seconds $PollIntervalSeconds }
    $resp = Invoke-FiscalApi -Method GET -Path "/api/v4/download-requests/$($state.requestId)"
    Assert-Success $resp "poll-${i}"
    $satStatus  = $resp.Body.data.satRequestStatusId
    $reqStatus  = $resp.Body.data.downloadRequestStatusId
    $invCount   = $resp.Body.data.invoiceCount
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
        break
    }
    if ($reqStatus -in '-1') {
        Write-Host "  -> Abandonada terminal. Stop." -ForegroundColor Red
        break
    }
}
$state.polls = $polls
Save-State

# ===========================================================================
# Paso 11 — Cosechar /meta-items (si Completada)
# ===========================================================================

Write-Step "11" "Cosechar /meta-items (si reqStatus=3)"

$lastPoll = $polls | Select-Object -Last 1
if ($lastPoll.downloadRequestStatusId -eq '3') {
    $meta = Invoke-FiscalApi -Method GET -Path "/api/v4/download-requests/$($state.requestId)/meta-items?pageNumber=1&pageSize=20"
    Assert-Success $meta "list-meta-items"
    $items = $meta.Body.data.items ?? @()
    $state.metaItems = @{
        count            = $items.Count
        totalCount       = $meta.Body.data.totalCount
        totalPages       = $meta.Body.data.totalPages
        sampleUuids      = ($items | Select-Object -First 5 -ExpandProperty uuid -ErrorAction SilentlyContinue)
        firstItemShape   = if ($items.Count -gt 0) { $items[0].PSObject.Properties.Name } else { @() }
    }
    Save-State
    Write-Host "Cosechados $($items.Count) items (totalCount=$($meta.Body.data.totalCount), totalPages=$($meta.Body.data.totalPages))." -ForegroundColor Green
    if ($items.Count -gt 0) {
        Write-Host "Primer item shape: $($state.metaItems.firstItemShape -join ', ')" -ForegroundColor Gray
    }
} else {
    Write-Host "Request aún no en estado 3 (Completada). Re-corre el script más tarde para que skipee los pasos 1-9 y siga polleando." -ForegroundColor Yellow
}

# ===========================================================================
# Reporte final
# ===========================================================================

Write-Host ""
Write-Host ("═" * 70) -ForegroundColor Cyan
Write-Host "RESUMEN" -ForegroundColor Cyan
Write-Host ("═" * 70) -ForegroundColor Cyan

$report = [ordered]@{
    ranAt              = (Get-Date).ToString('o')
    baseUrl            = $BaseUrl
    emisor             = @{ id = $state.emisorId; rfc = $EmisorRfc }
    receptor           = @{ id = $state.receptorId; rfc = $ReceptorRfc }
    cerFileId          = $state.cerFileId
    keyFileId          = $state.keyFileId
    productId          = $state.productId
    invoiceId          = $state.invoiceId
    invoiceUuid        = $state.invoiceUuid
    ruleId             = $state.ruleId
    requestId          = $state.requestId
    requestInitialState= $state.requestInitialState
    polls              = $polls
    metaItems          = $state.metaItems
}
$report | ConvertTo-Json -Depth 25 | Set-Content -Path $OutputJson -Encoding UTF8

Write-Host ""
Write-Host "Emisor: $($state.emisorId) ($EmisorRfc)"
Write-Host "Receptor: $($state.receptorId) ($ReceptorRfc)"
Write-Host "Factura: $($state.invoiceId)  UUID: $($state.invoiceUuid)"
Write-Host "Rule: $($state.ruleId)"
Write-Host "Request: $($state.requestId)"
Write-Host "Estado final: satStatus=$($lastPoll.satRequestStatusId), reqStatus=$($lastPoll.downloadRequestStatusId), invoiceCount=$($lastPoll.invoiceCount)"
if ($state.metaItems) {
    Write-Host "Meta-items cosechados: $($state.metaItems.count)"
}
Write-Host ""
Write-Host "Reporte: $OutputJson" -ForegroundColor Cyan
Write-Host "State (re-runs skipean pasos completados): $StateFile" -ForegroundColor Cyan
