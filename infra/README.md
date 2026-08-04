# Infraestructura Millet ERP

Repositorio de **Infrastructure as Code (Bicep)** para el proyecto Millet ERP.
Este repositorio define el aprovisionamiento de los ambientes de Azure
(desarrollo, QA, producción) de manera idempotente y versionada.

---

## Índice

1. [Resumen](#resumen)
2. [Componentes desplegados](#componentes-desplegados)
3. [Prerequisitos](#prerequisitos)
4. [Primer despliegue de desarrollo](#primer-despliegue-de-desarrollo)
5. [Verificación post-despliegue](#verificación-post-despliegue)
6. [Cargar secretos en Key Vault](#cargar-secretos-en-key-vault)
7. [Operaciones cotidianas](#operaciones-cotidianas)
8. [Destruir el ambiente](#destruir-el-ambiente)
9. [Convenciones de nombres y tags](#convenciones-de-nombres-y-tags)
10. [Costo estimado](#costo-estimado)
11. [Troubleshooting](#troubleshooting)

---

## Resumen

Este código aprovisiona el "núcleo" mínimo de infraestructura necesario para
empezar a construir el ERP. Los componentes que están aquí son los de **Fase 0**:
red virtual, base de datos, almacenamiento, registro de contenedores, almacén de
secretos, y observabilidad. Los componentes de **Fase 1** (Container Apps,
Service Bus, Application Gateway) se agregarán cuando se necesiten.

Cada ambiente (`dev`, `qa`, `prod`) se aprovisiona con el mismo código, pero con
parámetros distintos que controlan:

- SKUs (más pequeños en dev, más robustos en prod)
- Alta disponibilidad (sin HA en dev, con HA zonal en prod)
- Retención de logs y backups (más corta en dev)
- Replicación geográfica de almacenamiento (LRS en dev, GRS en prod)
- Purge protection del Key Vault (deshabilitada en dev para permitir destrucción)

---

## Componentes desplegados

| Componente | Recurso de Azure | SKU dev | SKU prod |
|---|---|---|---|
| Red virtual | VNet con 3 subredes | Estándar | Estándar |
| Base de datos | PostgreSQL Flexible Server v16 | Burstable B1ms | GeneralPurpose D2ds_v5 con HA |
| Secretos | Key Vault Standard | Standard | Standard con purge protection |
| Almacenamiento | Storage Account | Standard_LRS | Standard_GRS |
| Registry | Container Registry | Basic | Premium |
| Logs y telemetría | Log Analytics + App Insights | 30 días retención | 90 días retención |
| Presupuesto | Budget alerts | $150 USD/mes | $900 USD/mes |
| RBAC | Role assignments | Admins + Developers | Admins + Developers |

---

## Prerequisitos

### En tu máquina local

Necesitas tener instalado:

1. **Azure CLI** versión 2.60 o superior  
   Verifica con: `az --version`  
   Instalación: <https://learn.microsoft.com/cli/azure/install-azure-cli>

2. **Bicep CLI** versión 0.30 o superior  
   Verifica con: `az bicep version`  
   Si no está instalado: `az bicep install`

3. **Una shell** (Bash, PowerShell, o similar). Los ejemplos abajo usan Bash;
   en PowerShell la sintaxis de variables cambia ligeramente.

### En Azure

Antes del primer despliegue debes tener:

1. **Suscripción de Azure activa** en tu tenant de Entra ID.
2. **Tu usuario** debe ser miembro del grupo `MILLET-ERP-Admins` en Entra ID.
3. **Permisos** para desplegar a nivel de suscripción (rol `Contributor` o
   `Owner` sobre la suscripción mientras se asigna RBAC inicial). Una vez que
   el primer despliegue termine, las asignaciones RBAC posteriores las hace
   automáticamente el grupo Admins.

### Verificar tu sesión

```bash
az login
az account show
```

Confirma que aparece la suscripción correcta y tu cuenta de
`eduardo.paredes@tiglass.net`. Si tienes varias suscripciones:

```bash
az account list --output table
az account set --subscription "<NOMBRE_O_ID_DE_LA_SUSCRIPCIÓN>"
```

---

## Primer despliegue de desarrollo

### Paso 1: Generar una password segura para PostgreSQL

PostgreSQL requiere una password local de respaldo aunque la autenticación
principal sea por Entra ID. Genera una password segura y **guárdala en tu
gestor de contraseñas personal**. Después de aprovisionar la subiremos a Key
Vault para que el resto del equipo la consulte de manera segura.

Genera una password aleatoria:

```bash
# En Linux/macOS:
PG_ADMIN_PASSWORD=$(openssl rand -base64 24 | tr -d '/+=')Aa1!

echo "Guarda esta password en tu password manager: $PG_ADMIN_PASSWORD"
```

```powershell
# En PowerShell:
$PG_ADMIN_PASSWORD = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 20 | ForEach-Object {[char]$_}) + "Aa1!"
Write-Host "Guarda esta password: $PG_ADMIN_PASSWORD"
```

> **Importante**: la password debe tener al menos 12 caracteres y cumplir las
> reglas de complejidad de Azure (mayúsculas, minúsculas, números, símbolos).

> **Nota — el override `postgresAdminPassword` es SOLO para bootstrap.** En
> operación normal el `.bicepparam` la resuelve vía `az.getSecret()` contra
> el secreto `postgres-admin-password` del Key Vault, y los comandos van SIN
> `--parameters postgresAdminPassword=...` (ver "Referencia rápida"). El
> override por CLI solo se usa aquí porque el Key Vault todavía no existe;
> gana sobre el `getSecret()` del archivo. Tras el primer deploy, cargar el
> secreto en KV (Paso de secretos, abajo) para que los deploys siguientes se
> resuelvan solos. Contexto: incidente 2026-07-07 — con el placeholder
> anterior, un deploy sin override reseteaba el password real de `pgadmin`.

### Paso 2: Validar el template antes de desplegar

Esto compila el Bicep y verifica que no haya errores sintácticos ni lógicos
sin crear recursos.

```bash
az deployment sub validate \
  --location mexicocentral \
  --template-file infra/main.bicep \
  --parameters infra/parameters/dev.bicepparam \
  --parameters postgresAdminPassword="$PG_ADMIN_PASSWORD"
```

Si la validación es exitosa, verás un JSON con el resultado. Si hay errores,
los corregimos antes de desplegar realmente.

### Paso 3: Hacer un what-if para ver los cambios

Esto muestra qué recursos se crearían sin crearlos. Es la mejor forma de
revisar el alcance del despliegue antes de aplicarlo.

```bash
az deployment sub what-if \
  --location mexicocentral \
  --template-file infra/main.bicep \
  --parameters infra/parameters/dev.bicepparam \
  --parameters postgresAdminPassword="$PG_ADMIN_PASSWORD"
```

Revisa la lista. Deberías ver aproximadamente 15-20 recursos nuevos a crear
(resource group, VNet, subredes, PostgreSQL, Key Vault, Storage, Registry,
Log Analytics, App Insights, role assignments, budget).

### Paso 4: Desplegar

```bash
DEPLOY_NAME="deploy-millet-dev-$(date +%Y%m%d-%H%M%S)"

az deployment sub create \
  --location mexicocentral \
  --template-file infra/main.bicep \
  --parameters infra/parameters/dev.bicepparam \
  --parameters postgresAdminPassword="$PG_ADMIN_PASSWORD" \
  --name "$DEPLOY_NAME"
```

El despliegue tarda aproximadamente **8 a 15 minutos**. La mayor parte del
tiempo lo consume la creación de PostgreSQL Flexible Server.

Si el despliegue falla a la mitad, **no hay problema**: puedes ejecutar el
mismo comando de nuevo y Bicep continúa donde se quedó (es idempotente).

---

## Verificación post-despliegue

### Verificar el resource group

```bash
az group show --name rg-millet-dev-mxc-01 --output table
```

Debería mostrar el grupo en estado `Succeeded`.

### Listar todos los recursos creados

```bash
az resource list \
  --resource-group rg-millet-dev-mxc-01 \
  --output table
```

Debería listar la VNet, PostgreSQL, Key Vault, Storage, Registry, Log
Analytics, App Insights.

### Probar conexión a PostgreSQL

```bash
PG_FQDN=$(az deployment sub show \
  --name "$DEPLOY_NAME" \
  --query 'properties.outputs.postgresFqdn.value' \
  --output tsv)

echo "PostgreSQL host: $PG_FQDN"

# Conectar con psql usando la password local:
psql "host=$PG_FQDN port=5432 dbname=postgres user=pgadmin password=$PG_ADMIN_PASSWORD sslmode=require"
```

Si `psql` no está instalado: `sudo apt install postgresql-client` (Ubuntu/WSL)
o equivalente.

### Verificar autenticación por Entra ID

Cualquier miembro del grupo `MILLET-ERP-Admins` debería poder conectarse a
PostgreSQL con su cuenta de Entra ID, sin password local:

```bash
# Obtener un access token para PostgreSQL
ACCESS_TOKEN=$(az account get-access-token \
  --resource-type oss-rdbms \
  --query accessToken \
  --output tsv)

# Conectar usando el token como password
PGPASSWORD="$ACCESS_TOKEN" psql \
  "host=$PG_FQDN port=5432 dbname=postgres user=eduardo.paredes@tiglass.net sslmode=require"
```

---

## Cargar secretos en Key Vault

Después del primer despliegue, sube los secretos al Key Vault para que el
resto del equipo (y la aplicación cuando la construyamos) puedan consultarlos
sin verlos en archivos.

```bash
KV_NAME=$(az deployment sub show \
  --name "$DEPLOY_NAME" \
  --query 'properties.outputs.keyVaultName.value' \
  --output tsv)

# Subir el usuario y password de PostgreSQL
az keyvault secret set \
  --vault-name "$KV_NAME" \
  --name "postgres-admin-username" \
  --value "pgadmin"

az keyvault secret set \
  --vault-name "$KV_NAME" \
  --name "postgres-admin-password" \
  --value "$PG_ADMIN_PASSWORD"

# Subir la connection string para que la app la consulte
PG_FQDN=$(az deployment sub show \
  --name "$DEPLOY_NAME" \
  --query 'properties.outputs.postgresFqdn.value' \
  --output tsv)

az keyvault secret set \
  --vault-name "$KV_NAME" \
  --name "postgres-connection-string" \
  --value "Host=$PG_FQDN;Port=5432;Database=postgres;Username=pgadmin;Password=$PG_ADMIN_PASSWORD;Ssl Mode=Require;"
```

---

## Auth: app registrations en Entra ID + secrets en Key Vault

La autenticación (ADR-0003, ADR-0007, ADR-0015) requiere setup manual en
Entra ID + algunos secretos en Key Vault. Bicep no expone Microsoft.Graph
nativamente, así que las app registrations se crean en el portal o via
`az ad app create`. Una vez creadas, los IDs viven en Key Vault y el
App Service los consume vía `@Microsoft.KeyVault(...)` references que ya
están declaradas en `modules/appservice.bicep`.

> Los comandos están en **PowerShell** (Windows). Si usas WSL/Bash,
> reemplaza `$VAR = "..."` por `VAR=...` y backtick `` ` `` por
> backslash `\` en las continuaciones de línea.

### Paso 1: Crear app registrations en Entra ID

Necesitas dos app registrations:

1. **API app registration** — el backend del ERP, valida tokens.
2. **SPA app registration** (PR 7+) — el frontend React, obtiene tokens
   con MSAL. Se puede crear cuando llegue la fase frontend.

**API app registration (este PR):**

```powershell
# Crear app registration para el API
$APP_NAME = "millet-erp-api-dev"

az ad app create `
  --display-name $APP_NAME `
  --sign-in-audience AzureADMyOrg

# Obtener el client ID
$API_CLIENT_ID = az ad app list --display-name $APP_NAME --query "[0].appId" --output tsv
Write-Host "API Client ID: $API_CLIENT_ID"

# Configurar el AppIdUri (audience)
$API_AUDIENCE = "api://$API_CLIENT_ID"
az ad app update --id $API_CLIENT_ID --identifier-uris $API_AUDIENCE

# Tenant ID del directorio actual
$TENANT_ID = az account show --query tenantId --output tsv
Write-Host "Tenant ID: $TENANT_ID"
```

**Exponer el scope `access_as_user` en el API (requerido por el frontend MSAL):**

`az ad app update` no soporta el formato de `oauth2PermissionScopes`
directamente — más simple desde el portal:

1. Portal → `App registrations` → `millet-erp-api-dev` → `Expose an API`
2. `Add a scope`:
   - Scope name: `access_as_user`
   - Who can consent: `Admins and users`
   - Admin consent display name: `Acceso al API de Millet ERP como usuario`
   - Admin consent description: `Permite al app llamar al API en nombre del usuario.`
   - State: `Enabled`
3. Después del save, el scope completo es `api://<API_CLIENT_ID>/access_as_user`.

**SPA app registration (para el frontend, PR 7):**

```powershell
# Crear SPA app registration
$SPA_NAME = "millet-erp-spa-dev"

az ad app create `
  --display-name $SPA_NAME `
  --sign-in-audience AzureADMyOrg

$SPA_CLIENT_ID = az ad app list --display-name $SPA_NAME --query "[0].appId" --output tsv
Write-Host "SPA Client ID: $SPA_CLIENT_ID"
```

**Configurar redirect URI tipo SPA + permisos al API (vía portal):**

`az` no expone bien el tipo `spa` ni `requiredResourceAccess` para apps
nuevas. Pasos en el portal:

1. Portal → `App registrations` → `millet-erp-spa-dev` → `Authentication`:
   - `Add a platform` → `Single-page application`
   - Redirect URIs:
     - `http://localhost:5173/` (Vite dev server default)
     - `http://localhost:5173` (sin slash, MSAL es estricto)
     - Cuando haya hosting prod: `https://<frontend-host>/`
   - `Implicit grant and hybrid flows`: ambos checkboxes desmarcados (PKCE only)
2. Portal → `API permissions` → `Add a permission`:
   - `My APIs` → selecciona `millet-erp-api-dev`
   - `Delegated permissions` → marca `access_as_user`
   - `Add permissions`
   - `Grant admin consent for <tu tenant>` (botón gris arriba)

Después de esto, MSAL en el frontend puede pedir el scope
`api://<API_CLIENT_ID>/access_as_user` y el usuario obtiene un token con
audience = API_CLIENT_ID que el backend valida.

### Paso 2: Encontrar el OID del primer SuperAdmin

```powershell
# OID = objectId del usuario en Entra ID. Para tu propio usuario:
$SUPERADMIN_OID = az ad signed-in-user show --query id --output tsv
Write-Host "Tu OID: $SUPERADMIN_OID"

# Para un usuario distinto (ej. otro miembro del equipo):
# $SUPERADMIN_EMAIL = "otro.usuario@tiglass.net"
# $SUPERADMIN_OID = az ad user show --id $SUPERADMIN_EMAIL --query id --output tsv
```

### Paso 3: Generar JWT signing key

```powershell
# 256+ bits aleatorios, base64 — usado por JwtTokenService para firmar
# (HMAC SHA-256) y por el JwtBearer middleware para validar tokens del API.
# .NET expone RandomNumberGenerator nativamente, no necesitas openssl.
$bytes = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$JWT_SIGNING_KEY = [Convert]::ToBase64String($bytes)
Write-Host "JWT key generada (length=$($JWT_SIGNING_KEY.Length) chars)"
```

### Paso 4: Subir todo a Key Vault

```powershell
$KV_NAME = "kv-millet-dev-mxc-01"

az keyvault secret set --vault-name $KV_NAME `
  --name "auth-jwt-signing-key" --value $JWT_SIGNING_KEY

az keyvault secret set --vault-name $KV_NAME `
  --name "auth-initial-admin-oid" --value $SUPERADMIN_OID

az keyvault secret set --vault-name $KV_NAME `
  --name "auth-entra-tenant-id" --value $TENANT_ID

az keyvault secret set --vault-name $KV_NAME `
  --name "auth-entra-api-client-id" --value $API_CLIENT_ID

az keyvault secret set --vault-name $KV_NAME `
  --name "auth-entra-api-audience" --value $API_AUDIENCE
```

### Paso 5: Restart del App Service

Los app settings que referencian KV se resuelven al arranque del container.

```powershell
az webapp restart --name app-millet-dev-mxc-01 `
  --resource-group rg-millet-dev-mxc-01
```

### Paso 6: Verificar end-to-end

```powershell
# Health check anonymous (debe pasar siempre)
Invoke-WebRequest https://app-millet-dev-mxc-01.azurewebsites.net/health/ready -UseBasicParsing

# Logs del App Service: deben mostrar que el bootstrap corrió:
#   "Rol 'super-admin' creado..."
#   "Usuario SuperAdmin creado: oid=..., userId=..."
#   "SuperAdmin asignado a empresa TIG890101AAA con rol super-admin"
az webapp log tail --name app-millet-dev-mxc-01 `
  --resource-group rg-millet-dev-mxc-01

# Hit /health detallado: requiere permiso 'infra.health.leer' (que el
# SuperAdmin tiene tras el bootstrap). Para obtener un token, el frontend
# MSAL llamará /api/auth/sesion (PR 7+). Mientras tanto, validar via logs.
```

### Resumen de KV secrets de auth

| Secret | Origen | Cuándo cambia |
|--------|--------|---------------|
| `auth-jwt-signing-key` | `openssl rand -base64 48` | Solo en rotación (raro) |
| `auth-initial-admin-oid` | `az ad signed-in-user show` o `az ad user show` | Solo si cambia el primer admin (debería ser inmutable) |
| `auth-entra-tenant-id` | `az account show --query tenantId` | Nunca (es del tenant) |
| `auth-entra-api-client-id` | Output de `az ad app create` | Si se recrea la app reg |
| `auth-entra-api-audience` | `api://<clientId>` | Igual que client id |

### Empresa inicial (datos NO sensibles)

Los datos de la empresa inicial creada por el bootstrap (RFC, RazonSocial,
RegimenFiscal, NombreComercial) NO van en KV — son metadata pública. Se
configuran en `parameters/dev.bicepparam` y se pasan al App Service como
app settings directos. Para cambiar la empresa inicial post-deploy hay que
re-deployar el Bicep o editarlas en Portal → App Service → Configuration.

---

## Frontend: Static Web Apps + SPA app registration

El frontend SPA se hostea en Azure Static Web Apps (SKU Free) en
`centralus` (SWA no soporta `mexicocentral`). Bicep crea el recurso vacío;
el deploy del bundle lo hace el workflow `deploy-app-dev.yml` (job
`deploy-frontend`).

### Paso 1: Obtener el deployment token del SWA

Bicep crea el SWA pero no expone el token (es secreto runtime). Después
del primer `az deployment sub create`:

```powershell
$SWA_TOKEN = az staticwebapp secrets list `
  --name swa-millet-dev-mxc-01 `
  --resource-group rg-millet-dev-mxc-01 `
  --query "properties.apiKey" --output tsv

az keyvault secret set --vault-name kv-millet-dev-mxc-01 `
  --name "swa-deployment-token" --value $SWA_TOKEN
```

### Paso 2: Subir el SPA Client ID a KV

```powershell
# Asume que ya creaste la SPA app reg (millet-erp-spa-dev) en sección
# "Auth: app registrations en Entra ID + secrets en Key Vault" arriba.
$SPA_NAME = "millet-erp-spa-dev"
$SPA_CLIENT_ID = az ad app list --display-name $SPA_NAME --query "[0].appId" --output tsv

az keyvault secret set --vault-name kv-millet-dev-mxc-01 `
  --name "auth-entra-spa-client-id" --value $SPA_CLIENT_ID
```

### Paso 3: Agregar el hostname del SWA como redirect URI de la SPA app reg

```powershell
$SWA_HOSTNAME = az staticwebapp show `
  --name swa-millet-dev-mxc-01 `
  --resource-group rg-millet-dev-mxc-01 `
  --query "defaultHostname" --output tsv
Write-Host "SWA hostname: https://$SWA_HOSTNAME"
```

Luego en portal: `App registrations → millet-erp-spa-dev → Authentication
→ Single-page application → Add URI`:
- `https://<SWA_HOSTNAME>` (sin slash final, MSAL es estricto)
- `https://<SWA_HOSTNAME>/` (con slash, por si acaso)

### Paso 4: Disparar el deploy

El próximo push a `main` que toque `frontend/**` o el workflow dispara
`deploy-frontend` automáticamente. Para forzar uno manual:

```powershell
gh workflow run "Deploy Application to Dev"
```

### Paso 5: Verificar end-to-end

```powershell
# Abrir la URL en el browser
Start-Process "https://$SWA_HOSTNAME"
```

Esperado:
- LoginScreen con botón "Iniciar sesión con Microsoft"
- Click → popup MSAL → consent (primera vez) → callback → app shell con
  tu nombre + empresa Tiglass como activa + permisos completos del SuperAdmin

Si MSAL devuelve error tipo `AADSTS50011: redirect_uri does not match`,
asegúrate que el hostname del SWA esté en la lista de redirect URIs de
la app reg (Paso 3).

### Resumen de KV secrets de frontend

| Secret | Origen | Cuándo cambia |
|--------|--------|---------------|
| `auth-entra-spa-client-id` | Output de `az ad app create` para la SPA | Solo si se recrea la app reg |
| `swa-deployment-token` | `az staticwebapp secrets list` | Si se rota manualmente |

### CORS

El backend (`app-millet-dev-mxc-01`) tiene CORS configurado vía Bicep
(`Cors__AllowedOrigins__0` = hostname del SWA). En dev local el frontend
en `http://localhost:5173` también está permitido (vía
`appsettings.Development.json`). Para agregar otro origen (ej. custom
domain del SWA), agrégalo al array `corsAllowedOrigins` en
`appservice.bicep` o como app setting manual.

---

## Operaciones cotidianas

### Re-desplegar tras un cambio en Bicep

Mismo comando del Paso 4. Bicep detecta los cambios y solo aplica las
diferencias.

### Agregar un desarrollador al equipo

1. Agregar al usuario al grupo `MILLET-ERP-Developers` en el portal de Entra
   ID. La asignación de roles es automática gracias al RBAC ya configurado.
2. El nuevo usuario hace `az login` con su cuenta corporativa y ya tiene
   acceso de Contributor a los recursos.

### Ver el costo actual del ambiente

```bash
az consumption usage list \
  --start-date "$(date -u -d 'first day of this month' +%Y-%m-%d)" \
  --end-date "$(date -u +%Y-%m-%d)" \
  --output table
```

O en el portal de Azure: **Cost Management + Billing** > **Cost analysis**,
filtrando por el resource group `rg-millet-dev-mxc-01`.

---

## Destruir el ambiente

En desarrollo a veces conviene borrar todo y volver a empezar desde cero.

```bash
# 1. Borrar el resource group (todos los recursos dentro)
az group delete \
  --name rg-millet-dev-mxc-01 \
  --yes \
  --no-wait

# 2. Esperar a que termine
az group wait --deleted --name rg-millet-dev-mxc-01

# 3. Purgar el Key Vault del soft-delete (para liberar el nombre)
az keyvault purge \
  --name kv-millet-dev-mxc-01 \
  --location mexicocentral
```

> **Nota**: en producción esto no funciona porque purge protection está
> habilitado. Es a propósito.

---

## Convenciones de nombres y tags

### Patrón general

```
{tipo}-{proyecto}-{ambiente}-{región}-{secuencia}
```

Ejemplos:
- `rg-millet-dev-mxc-01` (resource group)
- `pg-millet-dev-mxc-01` (PostgreSQL)
- `kv-millet-dev-mxc-01` (Key Vault)
- `vnet-millet-dev-mxc` (VNet, sin secuencia porque es única)

Recursos con restricciones especiales:
- `stmilletdevmxc01` (Storage: solo minúsculas, sin guiones, máx 24 chars)
- `crmilletdevmxc01` (Container Registry: solo alfanumérico, máx 50 chars)

### Tags aplicados a todos los recursos

| Tag | Valor en dev |
|---|---|
| `Project` | `millet-erp` |
| `Environment` | `dev` |
| `Owner` | `eduardo.paredes@tiglass.net` |
| `CostCenter` | `it-projects` |
| `CreatedBy` | `bicep` |
| `DataClassification` | `internal` |

---

## Costo estimado

### Desarrollo (este ambiente)

Con sizing ligero y Container Apps con escala a cero, el costo realista en
estado estable es **30 a 50 USD/mes**, después del periodo de crédito gratuito
de Azure.

Componente principal del costo:
- PostgreSQL Burstable B1ms: ~13 USD/mes
- Storage Account: ~3 USD/mes
- Container Registry Basic: ~5 USD/mes
- Log Analytics + App Insights: ~10 USD/mes (con cap de 1 GB diario)
- Otros (Key Vault, networking): ~5 USD/mes

El primer mes suele cubrirse con el crédito de $200 USD que da Azure al crear
una cuenta nueva.

### Producción (futuro, en suscripción del cliente)

Con sizing equilibrado robusto: **700 a 900 USD/mes** (presupuestado y
aprobado).

---

## Troubleshooting

### Error: "RoleAssignmentExists"

Ocurre cuando ya existe una asignación de rol con el mismo GUID. Generalmente
significa que un despliegue anterior se ejecutó parcialmente. Solución:

1. Ir al portal: Resource Group > Access Control (IAM) > Role assignments
2. Eliminar las asignaciones obsoletas
3. Re-ejecutar el despliegue

### Error: "VaultAlreadyExists" después de destruir y recrear

El Key Vault está en soft-delete. Purgarlo:

```bash
az keyvault purge --name kv-millet-dev-mxc-01 --location mexicocentral
```

### Error de cuota o región no soportada

Algún SKU puede no estar disponible en `mexicocentral`. Soluciones:
1. Cambiar `location` y `regionCode` en `dev.bicepparam` a `eastus2` y `eus2`.
2. O reducir el SKU del recurso afectado en `main.bicep`.

### El despliegue tarda más de 20 minutos

Normal en el primer despliegue por la creación de PostgreSQL. Si se atora
realmente, cancelar y revisar logs:

```bash
az deployment sub list --output table
az deployment sub show --name "$DEPLOY_NAME"
```

### No tengo permisos para crear role assignments

Necesitas el rol `User Access Administrator` o `Owner` sobre la suscripción
en el primer despliegue. Una vez que el grupo Admins quede asignado como
Owner del resource group, los despliegues subsecuentes pueden usar solo ese
rol.

---

## Referencia rápida de comandos

```bash
# Login
az login
az account set --subscription "<NOMBRE>"

# Validar (la password se resuelve sola vía az.getSecret del .bicepparam)
az deployment sub validate --location mexicocentral --template-file infra/main.bicep --parameters infra/parameters/dev.bicepparam

# What-if
az deployment sub what-if --location mexicocentral --template-file infra/main.bicep --parameters infra/parameters/dev.bicepparam

# Desplegar
az deployment sub create --location mexicocentral --template-file infra/main.bicep --parameters infra/parameters/dev.bicepparam --name "deploy-millet-dev-$(date +%Y%m%d-%H%M%S)"

# (Solo bootstrap de ambiente nuevo, cuando el KV aún no existe: agregar
#  --parameters postgresAdminPassword="$PG_ADMIN_PASSWORD" a los 3 comandos.)

# Listar recursos
az resource list --resource-group rg-millet-dev-mxc-01 --output table

# Destruir
az group delete --name rg-millet-dev-mxc-01 --yes --no-wait
az keyvault purge --name kv-millet-dev-mxc-01 --location mexicocentral
```
