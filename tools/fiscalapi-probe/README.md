# `fiscalapi-probe` — validador de sandbox FiscalAPI

Script PowerShell que pega a `test.fiscalapi.com` con credenciales reales
y valida las **10 variables abiertas** del doc
[`docs/modulos/integraciones-fiscal/02-flujo-asincrono.md`](../../docs/modulos/integraciones-fiscal/02-flujo-asincrono.md) §12,
antes de arrancar PR-9 del módulo `Integraciones.Fiscal`.

## Por qué este probe existe

El audit del 2026-05-25 reveló que el `IFiscalApiClient` actual asume un
flujo síncrono que no corresponde al modelo real del PAC (asíncrono,
estilo SAT). Antes de codear los workers nuevos (`Submitter` + `Poller`),
hay que confirmar 10 puntos contra el vendor para no construir sobre
suposiciones.

Este script no reemplaza el código de producción — solo es un harness
de validación one-shot. **No mete nada al repo de prod**, no toca BD.

## Prerrequisitos

- PowerShell 7+ (`pwsh`). El script no corre en Windows PowerShell 5.1.
- Credenciales sandbox de FiscalAPI (`X-TENANT-KEY` + `X-API-KEY`).

## Uso

### Modo read-only (default — recomendado para primera corrida)

```powershell
# Opción A: por parámetros
.\tools\fiscalapi-probe\Invoke-FiscalApiProbe.ps1 `
    -TenantKey 'tu-tenant-key' `
    -ApiKey 'tu-api-key'

# Opción B: por env vars (mejor para no dejar secretos en historial)
$env:FISCALAPI_SANDBOX_TENANT_KEY = 'tu-tenant-key'
$env:FISCALAPI_SANDBOX_API_KEY    = 'tu-api-key'
.\tools\fiscalapi-probe\Invoke-FiscalApiProbe.ps1
```

Esto verifica auth, lista catálogos, intenta endpoints conocidos y
prueba rate limit con 30 requests back-to-back. **No crea recursos.**

### Modo end-to-end (crea Person + Rule + Request en sandbox)

```powershell
.\tools\fiscalapi-probe\Invoke-FiscalApiProbe.ps1 `
    -TenantKey 'tu-tenant-key' `
    -ApiKey 'tu-api-key' `
    -CreateData `
    -TestRfc 'XAXX010101000' `
    -TestLegalName 'Millet Probe SA de CV'
```

Esto sí persiste recursos en tu cuenta sandbox (un Person, una Rule, una
Request para los últimos 7 días). Limpia desde el dashboard FiscalAPI
cuando termines.

> Las solicitudes creadas tardarán **horas** en pasar a `Terminada`
> (FiscalAPI pollea al SAT cada 30 min). El probe hace solo 3 polls de
> 30s para confirmar el flujo, no para esperar la cosecha.

## Output

- **Consola**: tabla con `[PASS/FAIL/WARN/INFO]` por variable.
- **JSON**: reporte completo en `fiscalapi-probe-report.json` con
  request/response raw de cada llamada. Compártelo en el PR-9 como
  evidencia de validación.

## Las 10 variables (§12 del doc)

| Variable | Qué valida |
|---|---|
| V1 | Headers `X-TENANT-KEY` + `X-API-KEY` + `X-TIME-ZONE` autentican; response envelope `{data, succeeded, ...}`. |
| V2 | Catálogos `SatQueryTypes`, `DownloadTypes`, `SatInvoiceStatuses`, `SatInvoiceTypes`. |
| V3 | Catálogos `SatRequestStatuses`, `DownloadRequestStatuses`, `DownloadRequestTypes`. |
| V4 | ¿Hay tax-files cargados? ¿Aparece FIEL o solo CSD? |
| V5 | Existe endpoint "Consultar estado de factura" para refresh por UUID. |
| V6 | Rate limit y header `Retry-After` (best-effort: 30 requests burst). |
| V7 | Listado de People (RFCs receptores). |
| V8 | Listados de Download-rules + Download-requests + TTL inferido. |
| V9 | Flujo end-to-end Crear Rule → Crear Request → Pollear → Listar meta-items (solo con `-CreateData`). |
| V10 | ¿`personId` debe pre-existir o se crea on-the-fly? |

## Modo end-to-end completo: `Invoke-FiscalApiProbeE2E.ps1`

Si quieres validar el flujo de descarga masiva con datos sintéticos
reales, hay un segundo script que automatiza el flujo de
aprovisionamiento que la doc de FiscalAPI exige en sandbox:

```powershell
.\tools\fiscalapi-probe\Invoke-FiscalApiProbeE2E.ps1
```

**Lo que hace** (11 pasos secuenciales, con checkpoint en cada uno):

1. Descarga el ZIP de CSDs de prueba de `developers.sw.com.mx`.
2. Crea un Person Emisor (default RFC `EKU9003173C9` — Escuela Kemper Urgate).
3-4. Sube el `.cer` y el `.key` del emisor vía `POST /api/v4/tax-files`
     (password universal `12345678a`).
5. Crea un Person Receptor (default RFC `IIA040805DZ4` — Industria
   Iluminadora de Almacenes).
6. Crea un Producto genérico.
7. Emite una factura de prueba Emisor → Receptor vía
   `POST /api/v4/invoices/income`.
8. Crea una DownloadRule (Recibidos × Metadata × Vigente).
9. Crea una DownloadRequest de los últimos 7 días.
10. Pollea el estado del request hasta `Completada` (5 × 30s default).
11. Lista `/meta-items` si quedó cosechable.

### Por qué hace falta este flujo

Cita de la doc oficial: *"el SAT no proporciona un ambiente de pruebas
para el servicio de descarga masiva. Sin embargo, FiscalAPI creó un
entorno de simulación propio"*. Para que ese entorno tenga "CFDIs
recibidos" que simular, primero hay que **emitir facturas en sandbox**
hacia el RFC receptor. El probe E2E hace eso de punta a punta.

### State file + resume

Cada paso persiste su resultado en `fiscalapi-probe-e2e-state.json`
(gitignored). Si el script falla en, digamos, el paso 7 (emisión),
al re-correrlo skipea los pasos 1-6 y reintenta desde el 7. Para
empezar de cero pasa `-Reset` (los recursos ya creados en sandbox
quedan; el script no los borra).

### Resultados esperados

| Resultado | Significado | Próxima acción |
|---|---|---|
| Todos los pasos PASS + `metaItems.count >= 1` | Flujo end-to-end confirmado. **D2 cerrada**: sandbox NO requiere FIEL del receptor. | Arrancar PR-9 con confianza. |
| Pasos 1-9 OK pero polleo nunca llega a Completada en 5 intentos | Normal — SAT-simulado puede tardar más de 2.5 min. Re-corre el script más tarde (skipea hasta paso 10). | Confirmar tiempo típico tras 1-2h. |
| Paso 7 (emisión) falla con error de catálogo SAT | Régimen fiscal o uso CFDI incorrecto para el RFC. | Ajustar `satTaxRegimeId` o `satCfdiUseId` en el script (líneas 256, 318). |
| Paso 3/4 (subir CSD) falla | Password incorrecta o archivo malformado. | Verificar que el ZIP se extrajo bien en `csd-test/`. |

## Modo PRODUCCIÓN: `Invoke-FiscalApiProbeProd.ps1`

**Caso de uso**: validar el flujo de descarga masiva end-to-end contra
`live.fiscalapi.com` con la FIEL real de Millet, antes de cablear PR-9.

**Por qué existe** (decisión arquitectónica del 2026-05-25 tras múltiples
iteraciones): el endpoint `POST /api/v4/download-rules` está bloqueado
en sandbox (`test.fiscalapi.com`) — solo se puede crear vía dashboard.
Para validar el flujo via API hay que ir a producción. Por la misma
razón, **Millet apuntará el módulo Integraciones.Fiscal a
`live.fiscalapi.com` en todos los ambientes** (dev/qa/prod) — sandbox no
soporta el contrato que necesitamos.

### Setup

1. Copia la FIEL real (`fiel.cer` + `fiel.key`) a un directorio local
   fuera del repo o a `tools/fiscalapi-probe/fiel/` (gitignored).
2. Set credentials como env vars (ejecuta una sola vez en tu sesión PS):

   ```powershell
   $env:FISCALAPI_PROD_TENANT_KEY = '<tenant-key-prod>'
   $env:FISCALAPI_PROD_API_KEY    = '<api-key-prod>'
   $env:FISCALAPI_FIEL_PASSWORD   = '<password-fiel>'
   ```

### Uso

```powershell
.\tools\fiscalapi-probe\Invoke-FiscalApiProbeProd.ps1 `
    -ReceptorRfc 'MIL010101AAA' `
    -ReceptorLegalName 'MILLET RAZON SOCIAL EXACTA EN SAT' `
    -ReceptorZipCode '97000' `
    -ReceptorEmail 'admin@millet.com' `
    -FielCerPath '.\tools\fiscalapi-probe\fiel\fiel.cer' `
    -FielKeyPath '.\tools\fiscalapi-probe\fiel\fiel.key'
```

7 pasos: Person Receptor → FIEL .cer → FIEL .key → DownloadRule →
DownloadRequest → Poll → Cosecha. Mismo patrón checkpoint que el probe
sandbox; re-runs skipean pasos completados.

### Seguridad

- FIEL nunca se loggea ni va al reporte JSON.
- Password se pasa por env var o param (nunca queda en historial salvo
  si lo escribes con `-FielPassword` literal en el shell).
- `.cer`, `.key`, `fiel/` y los state/report JSON están en `.gitignore`.
- Solo IDs de FiscalAPI (no PII fiscal) van al state file.

### Costos

Cada `DownloadRequest` consume cuota real de tu suscripción FiscalAPI
prod. El probe crea 1 request por corrida (a menos que pases `-Reset`).

## Después de correr

1. Si **todo PASS/INFO**: actualiza `02-flujo-asincrono.md` §12
   marcando los `[Gap]` como resueltos y adjunta el JSON al PR.
2. Si hay **FAIL**: revisa el reporte JSON (`rawRequests[]`) para entender
   la respuesta cruda. Probablemente las suposiciones del doc están mal
   o las credenciales son inválidas.
3. Si hay **WARN**: no bloquea, pero deja PLATFORM-TODO en el código
   correspondiente.

## Seguridad

- **No commitees** `fiscalapi-probe-report.json` — está en `.gitignore`.
  Contiene IDs internos de tu cuenta sandbox (no son secretos pero no
  son útiles fuera de tu cuenta).
- Las claves se pasan por parámetro o env var; nunca aparecen en el
  reporte JSON.
- El script usa `SkipHttpErrorCheck` para capturar 4xx/5xx sin throw;
  no oculta errores.
