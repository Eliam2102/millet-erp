# Millet A+W Drop Service

Servicio Windows .NET 8 que corre on-prem en `SER-DATA` (servidor de
Millet) y recibe archivos EDI vía HTTP en `localhost:5000`. Cuando el
módulo `Millet.Integraciones.Aw` del ERP necesita "soltar" una cotización
en A+W, abre una conexión a `localhost:5000` que Azure Hybrid Connection
intercepta y forwardea a `SER-DATA:5000` (este servicio). El servicio
valida la API key, escribe atómicamente el archivo a la carpeta de import
de A+W, y A+W lo procesa en su batch siguiente.

> Este proyecto vive en `on-prem/aw-drop-service/`, **fuera del Millet.sln
> del backend**. Es deliberadamente standalone: no comparte
> `Directory.Build.props`, no usa MediatR/FluentValidation/Mapster, no
> tiene EF Core. Su responsabilidad es **estrictamente**: recibir bytes,
> validar, escribir.

---

## Tabla de contenidos

1. [Spec resumida](#spec-resumida)
2. [Compilar](#compilar)
3. [Instalar](#instalar)
4. [Configurar](#configurar)
5. [Verificar](#verificar)
6. [Conectividad Hybrid Connection](#conectividad-hybrid-connection)
7. [Tests locales](#tests-locales)
8. [Troubleshooting](#troubleshooting)
9. [Actualizar](#actualizar)
10. [Desinstalar](#desinstalar)

---

## Spec resumida

| Aspecto | Valor |
|---|---|
| Runtime | .NET 8 (LTS) self-contained `win-x64` |
| Hosting | Windows Service (`Microsoft.Extensions.Hosting.WindowsServices`) |
| Listen | `http://localhost:5000` — **nunca interfaces externas** |
| Endpoints | `GET /`, `GET /healthz`, `POST /drop-edi`, `GET /completions`, `POST /results/{name}/archive`, `GET /documents`, `GET /documents/{filename}`, `POST /documents/{filename}/archive` |
| Correlación | Tras el drop, espera el **marcador vacío** `cot_<REF>.<AWDOCID>` que el customizing de A+W escribe en `ResultsFolder` al crear el pedido. Presente → `success`+`aw_doc_id`; timeout → `stuck`. Sin log que parsear |
| Auth | Header `X-API-Key` (constant-time compare) |
| Filename | Regex `^[a-zA-Z0-9_.-]+\.edi$` + path traversal guard |
| Body | Min 100 bytes, contiene marca `#END#`, máx `MaxBodySizeMB` (default 10 MB) |
| Escritura | Atómica vía `{filename}.tmp` → `File.Move(filename)` |
| Logging | Estructurado a Event Log (source `MilletAwDropService`) + consola |
| Anti-scope | NO acceso a internet, NO modifica EDI, NO toca A+W ni SQL |

Especificación completa: [`docs/integration/02-edi-correlation.md`](../../docs/integration/02-edi-correlation.md)
§9 (sección "Drop service (.NET 8 on-prem)").

---

## Compilar

En una workstation con **.NET 8 SDK** instalado (puedes ser tu laptop, no
hace falta hacerlo en `SER-DATA`):

```powershell
# Desde la raíz del repo
cd on-prem\aw-drop-service
dotnet publish DropService.csproj `
    -c Release `
    -r win-x64 `
    --self-contained `
    -o publish\
```

El resultado en `publish\` incluye `MilletAwDropService.exe` autocontenido
(no requiere .NET runtime instalado en el destino). Copia toda la carpeta
`publish\` a `SER-DATA` (Robocopy / SMB share / RDP clipboard).

---

## Instalar

En **`SER-DATA`**, copia `publish\` a la ruta de instalación final:

```powershell
# Ejemplo: instalación en C:\Apps\MilletAwDropService
$InstallDir = 'C:\Apps\MilletAwDropService'
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Recurse -Force .\publish\* $InstallDir\
Copy-Item -Force ..\appsettings.example.json $InstallDir\appsettings.json
Copy-Item -Recurse -Force ..\install $InstallDir\install
```

Edita `$InstallDir\appsettings.json` con la `ApiKey` real (ver
[Configurar](#configurar)).

Luego, **PowerShell elevado** (`Run as administrator`):

```powershell
cd C:\Apps\MilletAwDropService\install
.\install-service.ps1
```

Esto:
- Verifica que estás como admin.
- Verifica que `MilletAwDropService.exe` y `appsettings.json` existen.
- Verifica que `DropService.ApiKey` no esté vacía.
- Crea el Event Log source `MilletAwDropService` (si no existe).
- Registra el servicio con `sc.exe create` + `displayName` + `description`.
- Configura `Reset = 24h, restart 10s/30s/60s` en caso de crash.
- Arranca el servicio.

> Si el servicio ya existía y quieres reinstalarlo, agrega `-Force`:
> `.\install-service.ps1 -Force`

---

## Configurar

Plantilla documentada en [`appsettings.example.json`](appsettings.example.json).
Copia a `appsettings.json` y edita:

```jsonc
{
  "DropService": {
    "ListenUrl": "http://localhost:5000",   // NUNCA bindear a 0.0.0.0
    "ApiKey": "<obtener de Key Vault, ver abajo>",
    "AwImportFolder": "C:\\AW\\Import",      // WorkDir (PLATFORM-TODO confirmar con A+W)
    "ResultsFolder": "C:\\AW\\Import\\Results", // marcadores cot_<REF>.<docid> (vacío → {AwImportFolder}\Results)
    "MaxBodySizeMB": 10,
    "WriteTimeoutSeconds": 30
  },
  "Logging": {
    "EventLog": { "SourceName": "MilletAwDropService" }
  }
}
```

### Obtener la `ApiKey` desde Key Vault del ERP

La misma cadena debe estar **en este `appsettings.json` y en el secret
`aw-drop-service-api-key`** del Key Vault del ERP del ambiente
correspondiente (`kv-millet-dev-mxc-01` para dev). La sincronización es
manual.

```powershell
# En tu workstation, con az CLI autenticado al tenant tiglass.net:
$ApiKey = az keyvault secret show `
    --vault-name kv-millet-dev-mxc-01 `
    --name aw-drop-service-api-key `
    --query value --output tsv

# Guarda este valor en tu password manager. Después pégalo en
# appsettings.json en SER-DATA. NO lo dejes en archivos temporales.
Write-Host "Longitud de la key: $($ApiKey.Length) chars"
```

### Rotación de la `ApiKey`

Cada 6 meses (ADR pendiente). Procedimiento:
1. Generar nueva key: `openssl rand -base64 48 | tr -d '\n'` o equivalente.
2. `az keyvault secret set --vault-name kv-millet-... --name aw-drop-service-api-key --value <nueva>`.
3. Reiniciar App Service del ERP (resuelve la KV ref nueva).
4. Editar `appsettings.json` en `SER-DATA` con la nueva key.
5. Reiniciar `MilletAwDropService` (`Restart-Service MilletAwDropService`).
6. Ejecutar `verify-install.ps1` para validar end-to-end.

> Hay una ventana de inconsistencia entre pasos 3 y 5. Para evitar caídas,
> hacer en horario de bajo tráfico o implementar dual-key acceptance
> (post-MVP).

---

## Verificar

```powershell
cd C:\Apps\MilletAwDropService\install
.\verify-install.ps1
```

Corre 5 checks: servicio Running, `GET /`, `GET /healthz`,
`POST /drop-edi` sin API key (esperando 401), `POST /drop-edi` con API
key correcta y payload válido (esperando 200 + archivo escrito y luego
limpiado).

Smoke manual:
```powershell
# Liveness sin auth
Invoke-RestMethod http://localhost:5000/healthz

# Drop con API key (la lees del appsettings local)
$ApiKey = (Get-Content C:\Apps\MilletAwDropService\appsettings.json -Raw `
    | ConvertFrom-Json).DropService.ApiKey
$Body = "FH dummy`r`n" + ('X' * 200) + "`r`n#END#`r`n"
Invoke-RestMethod -Uri http://localhost:5000/drop-edi -Method POST `
    -Headers @{ 'X-API-Key' = $ApiKey; 'X-Filename' = 'smoke.edi' } `
    -Body $Body -ContentType 'text/plain'
```

---

## Conectividad Hybrid Connection

El servicio escucha en `localhost:5000`. Para que el módulo
`Millet.Integraciones.Aw` del ERP pueda llegar, el HCM (Hybrid Connection
Manager) de Microsoft debe estar instalado en `SER-DATA` y tener la
Hybrid Connection `hc-aw-drop-service` configurada (target
`SER-DATA:5000`).

El connection string del listener vive en Azure Relay:

```powershell
# En tu workstation
az relay hyco authorization-rule keys list `
    --resource-group rg-millet-dev-mxc-01 `
    --namespace-name relay-millet-dev-mxc-01 `
    --hybrid-connection-name hc-aw-drop-service `
    --name defaultListener `
    --query primaryConnectionString --output tsv
```

Pegar ese connection string en HCM UI (`Add Connection`). Tras unos
segundos debe quedar **Connected**. Si no, revisar:

- Firewall saliente: HTTPS 443 hacia `*.servicebus.windows.net` debe estar
  permitido.
- DNS desde `SER-DATA` resuelve `relay-millet-dev-mxc-01.servicebus.windows.net`.

Configuración completa de las 2 Hybrid Connections (SQL + drop service):
[`docs/integration/00-system-overview.md`](../../docs/integration/00-system-overview.md)
§5 (diagrama) y los pasos del último deploy en el historial del repo.

---

## Tests locales

Los tests usan `WebApplicationFactory<Program>` con configuración override
(temp dir, ApiKey fija). Funcionan en cualquier máquina con .NET 8 SDK —
no requieren Windows ni Event Log:

```powershell
cd on-prem\aw-drop-service\tests
dotnet test --logger "console;verbosity=detailed"
```

Tests cubren:
- POST sin `X-API-Key` → 401
- POST con `X-API-Key` incorrecto → 401
- POST con filename inválido (path traversal, separadores, sin `.edi`, espacios) → 400
- POST con body vacío → 400
- POST con body `< 100 bytes` → 400
- POST con body sin `#END#` → 422
- Happy path → 200 + archivo escrito al temp folder
- `GET /healthz` → 200 con uptime + writable
- `GET /` → 200 + texto con versión

---

## Troubleshooting

### El servicio no arranca

Revisar Event Log (Application, source `MilletAwDropService`):

```powershell
Get-EventLog -LogName Application -Source MilletAwDropService -Newest 20 `
    | Format-List TimeGenerated, EntryType, Message
```

Causas comunes:
- `appsettings.json` con JSON inválido → fallo de bind. Validar con `ConvertFrom-Json`.
- `DropService.ApiKey` vacía → endpoint devuelve 401 a todo. Editar y reiniciar.
- Puerto `5000` ocupado → cambiar `ListenUrl` o detener el otro proceso
  (`Get-NetTCPConnection -LocalPort 5000`).

### `/healthz` devuelve `import_folder_writable=false`

El service account no tiene permisos de escritura en `AwImportFolder`.
Por default los servicios corren como `LocalSystem`, pero si lo cambiaste
a una cuenta limitada (`NetworkService`, cuenta de dominio), darle
permisos:

```powershell
$folder = 'C:\AW\Import'
icacls $folder /grant 'NT AUTHORITY\NetworkService:(OI)(CI)M'
```

### Drops devuelven 401 cuando deberían pasar

API key desincronizada entre `appsettings.json` y Key Vault del ERP.
Verificar (comparando longitudes, no valores):

```powershell
$local = (Get-Content C:\Apps\MilletAwDropService\appsettings.json -Raw `
    | ConvertFrom-Json).DropService.ApiKey
$kv = az keyvault secret show --vault-name kv-millet-dev-mxc-01 `
    --name aw-drop-service-api-key --query value --output tsv

Write-Host "Local len: $($local.Length), KV len: $($kv.Length)"
```

Si difieren, decide cuál es la "buena" y sincroniza (ver
[Rotación](#rotación-de-la-apikey)).

### Drops devuelven 200 pero A+W nunca procesa

Verificar que la carpeta a la que escribimos es la misma que A+W vigila:

```powershell
Get-ChildItem 'C:\AW\Import' | Sort-Object LastWriteTime -Descending | Select -First 5
```

Si el archivo está ahí y A+W no lo recoge, el problema es del lado A+W
(servicio de import detenido, permisos, configuración del watcher).

### Hybrid Connection `Not Connected` en HCM

- Verifica reglas de firewall saliente para HTTPS 443.
- Verifica que el connection string usado es de `defaultListener` (no
  `defaultSender` que es para el lado Azure).
- En HCM UI, click `Status` para ver el último error.

---

## Actualizar

Para una nueva versión del binario:

```powershell
# 1. Detener el servicio
Stop-Service MilletAwDropService

# 2. Reemplazar binarios (mantén el appsettings.json existente)
$InstallDir = 'C:\Apps\MilletAwDropService'
Copy-Item -Recurse -Force .\publish\* $InstallDir\ `
    -Exclude appsettings.json

# 3. Arrancar
Start-Service MilletAwDropService

# 4. Verificar
cd $InstallDir\install
.\verify-install.ps1
```

---

## Desinstalar

```powershell
# PowerShell elevado
cd C:\Apps\MilletAwDropService\install
.\uninstall-service.ps1

# Opcional: también borrar el Event Log source
.\uninstall-service.ps1 -RemoveEventLogSource

# Limpieza de binarios y config
Remove-Item -Recurse -Force C:\Apps\MilletAwDropService
```
