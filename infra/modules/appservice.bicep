// ============================================================================
// Módulo: App Service (Linux) para la API .NET 9
// Crea el plan + web app con managed identity. App settings referencian
// secretos del Key Vault vía sintaxis @Microsoft.KeyVault(...). Health check
// probe configurado a /health/ready (ADR-0019).
//
// Por convención de costo:
//   - dev: Basic B1 (~$13/mes)
//   - prod: PremiumV3 P1v3 con alwaysOn (~$80/mes)
// ============================================================================

param location string
param projectName string
param environmentTier string
param regionCode string
param tags object

@description('Nombre del Key Vault donde viven los secretos referenciados.')
param keyVaultName string

@description('Nombre del Storage Account. La Managed Identity del App Service recibe Storage Blob Data Contributor sobre el container dataprotection-keys (ADR-0037).')
param storageAccountName string

@description('URI versionado de la DEK de DataProtection en el Key Vault. Se inyecta como app setting DataProtection__KeyIdentifier.')
param dataProtectionKeyIdentifier string

@description('Connection string de Application Insights (referenciada en app settings).')
param appInsightsConnectionString string

@description('SKU del App Service Plan (B1, S1, P1v3, etc.).')
param planSkuName string = 'B1'

@description('Tier del SKU (Basic, Standard, PremiumV3).')
param planSkuTier string = 'Basic'

@description('Valor de ASPNETCORE_ENVIRONMENT (Development, Staging, Production).')
param aspNetCoreEnvironment string = 'Development'

// ============================================================================
// Bootstrap del SuperAdmin (ADR-0007). Estos valores van directo en app
// settings (no son secretos — son metadata pública de la empresa). El oid
// del SuperAdmin y la JWT signing key sí van en KV (ver app settings abajo).
// Si Rfc viene vacío, el bootstrap solo crea Usuario+Rol sin empresa/asignación.
// ============================================================================

@description('RFC de la empresa inicial creada por el bootstrap. Vacío = no crear empresa.')
param empresaInicialRfc string = ''

@description('Razón social de la empresa inicial.')
param empresaInicialRazonSocial string = ''

@description('Régimen fiscal SAT (3 dígitos). Default 601 = Personas Morales.')
param empresaInicialRegimenFiscal string = '601'

@description('Nombre comercial opcional de la empresa inicial.')
param empresaInicialNombreComercial string = ''

@description('Origins permitidos por CORS. Default vacío = no se aceptan cross-origin requests. Para SPA en SWA: https://<swa-host>.')
param corsAllowedOrigins array = []

// ============================================================================
// Integración A+W (módulo Millet.Integraciones.Aw)
//
// El App Service se "junta" con dos Hybrid Connections del Azure Relay
// namespace para alcanzar `SER-DATA` on-prem sin abrir puertos entrantes
// en Millet. Los params abajo vienen de los outputs de relay.bicep +
// hybridConnections.bicep (orquestados en main.bicep).
//
// Las keys de Send se pasan @secure() y se inyectan al recurso de join
// `Microsoft.Web/sites/hybridConnectionNamespaces/relays`. NO se exponen
// como app settings — la app no las usa directamente; el routing lo hace
// la plataforma del App Service cuando ve un connect a `<host>:<port>`
// que matchea una HC registrada.
// ============================================================================

@description('Nombre del Azure Relay namespace que sostiene las Hybrid Connections (output de relay.bicep). Vacío = no wirear HCs (modo pre-integración).')
param relayNamespaceName string = ''

@description('Hostname del namespace en formato servicebus.windows.net (output de relay.bicep).')
param relayServiceBusNamespace string = ''

@description('Nombre de la Hybrid Connection que apunta al SQL Server AWBUSINESS on-prem (output de hybridConnections.bicep).')
param hcAwBusinessSqlName string = ''

@description('Hostname on-prem destino del SQL Server (default `SER-DATA`).')
param hcAwBusinessSqlHost string = 'SER-DATA'

@description('Puerto del SQL Server destino (1433 estático post-cambio coordinado).')
param hcAwBusinessSqlPort int = 1433

@secure()
@description('Primary key de la SAS rule `defaultSender` para hc-aw-business-sql.')
param hcAwBusinessSqlSendKey string = ''

@description('Nombre de la Hybrid Connection del drop service on-prem.')
param hcAwDropServiceName string = ''

@description('Hostname on-prem destino del drop service (default `SER-DATA`).')
param hcAwDropServiceHost string = 'SER-DATA'

@description('Puerto del drop service destino (5000 por convención).')
param hcAwDropServicePort int = 5000

@secure()
@description('Primary key de la SAS rule `defaultSender` para hc-aw-drop-service.')
param hcAwDropServiceSendKey string = ''

@description('Client ID (appid) del service principal `Glass Agent SPN` en Entra ID. Valor PÚBLICO (no es secret); el secret correspondiente va en KV como `glass-agent-spn-client-secret`. Vacío = no wirear todavía (Entra app reg pendiente).')
param glassAgentSpnClientId string = ''

@description('''EmpresaId (GUID) que procesa el AwSolicitudesWorker (ingesta de
pedidos A+W → Facturación, ADR-0048). Vacío = worker apagado (candado #2 del
runbook). Se setea por ambiente en el .bicepparam; prod queda vacío hasta el
go-live M3. Antes vivía como app setting manual y cada deploy de infra lo
borraba (incidente 2026-07-07: el deploy re-aplica la lista COMPLETA de app
settings y elimina lo no declarado).''')
param awSolicitudesEmpresaId string = ''

@description('Timeout por query (segundos) del flujo 2 (IntegracionesAw:Pedidos). La vista de líneas ejecuta DEVUELVE_IMPORTE_Y_DESCTO_N (pesada, gap G13); el default de código (5s) no alcanza.')
param awPedidosSqlQueryTimeoutSeconds int = 30

@description('''Días que una solicitud A+W puede quedarse Pospuesta por causas
corregibles en catálogos del ERP (canal/sucursal sin clave_aw, regla G14,
cliente no provisionable) antes de escalar a Rechazada (FAC-ING-PR3).
0 = rechazo inmediato (comportamiento previo).''')
param awSolicitudesPospuestaMaxDias int = 7

@description('''Vigencia de la autorización consumible de apertura de caja
ajena (Facturacion:Cajas, CAJAS-PR7/[Decisión 12-1]). Formato TimeSpan
"hh:mm:ss"; default espejo del código (30 min).''')
param cajasVigenciaAutorizacionApertura string = '00:30:00'

@description('''Clave de la sucursal emisora de los REPP automáticos
(Facturacion:ReppAutomatico, PR gemelo TES-PR7): los cobros bancarios que
Tesorería confirma no nacen en una sucursal física y el folio se reserva
por sucursal. Default dev: CON (CONKAL, única con serie CFDI activa).''')
param reppAutomaticoSucursalClave string = 'CON'

@description('''Apaga el SDK de FiscalAPI (IntegracionesFiscal:Sdk:Disabled).
Default true (SDK en NoOp — catálogos SAT responden 503 degradable y el
timbrado falla visible: desde F12-PR3 no hay stub de emisión). Se enciende
por ambiente en el .bicepparam; requiere el secreto `fiscalapi-tenant-key`
cargado en el KV del ambiente y la ApiKey por empresa capturada en
ConfiguracionPac (ventana de Integraciones Fiscal). El tenant es POR
AMBIENTE: dev usa el tenant sandbox (credenciales sk_test → BaseUrl
https://test.fiscalapi.com en la ConfiguracionPac); prod usará su propio
tenant en su propio KV.''')
param integracionesFiscalSdkDisabled bool = true

// ============================================================================
// Variables: app settings construidas dinámicamente
// ============================================================================

var corsAppSettings = [for (origin, i) in corsAllowedOrigins: {
  name: 'Cors__AllowedOrigins__${i}'
  value: origin
}]

// App settings del módulo Integraciones.Aw. Las KV refs apuntan a secretos
// que crea (o no) `keyvault-secrets.bicep` — si el operador no los seteó
// todavía, la resolución falla en runtime y el App Service no arranca
// limpio. Esa es la garantía de "fail loud" deseada (D2 del overview:
// módulo completo antes de mergear).
//
// **Naming**: los nombres deben coincidir EXACTO con los SectionName/Property
// que el código bindea — caso contrario el binder ignora silenciosamente y se
// queda con el default del POCO o el valor del appsettings.{Env}.json. PR de
// fix tras incidente Q-2026-00274 (los nombres viejos `AwIntegration__*` no
// matcheaban con `IntegracionesAwOptions.SectionName = "IntegracionesAw"`, así
// que el binder cargaba `http://localhost:5000` desde appsettings.Development.json
// en lugar del valor on-prem):
//
//   IntegracionesAw__DropServiceBaseAddress → IntegracionesAwOptions.DropServiceBaseAddress
//   IntegracionesAw__DropServiceApiKey       → IntegracionesAwOptions.DropServiceApiKey
//   ConnectionStrings__AwSqlServer           → SqlConnectionFactory (configuration.GetConnectionString)
//
// `IntegracionesAw__DropServiceBaseAddress` apunta al hostname:puerto on-prem
// porque la Hybrid Connection intercepta la conexión TCP a ese exact pair y la
// enruta vía Relay → HCM → drop service. No hay nombre Azure intermedio.
//
// Los settings de polling/correlation timeout (`Polling__IntervalSeconds`,
// `Correlation__TimeoutMinutes`) fueron retirados en PR #201 al migrar al flow
// callback per-EDI — el ERP ya no hace polling SQL desde Azure.
var awIntegrationAppSettings = [
  {
    name: 'ConnectionStrings__AwSqlServer'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=aw-sql-connection-string)'
  }
  {
    // ADR-0048 (flujo 2): BD de integración MILLET_INTEGRACION(_DEV) para la
    // ingesta de pedidos → Facturación. Si el secreto no existe en KV, App
    // Service pasa la ref SIN resolver como string literal — el toggle de
    // Program.cs lo detecta (prefijo @Microsoft.KeyVault) y deja los stubs.
    name: 'ConnectionStrings__AwIntegracionDb'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=aw-integracion-connection-string)'
  }
  {
    name: 'IntegracionesAw__DropServiceBaseAddress'
    value: 'http://${hcAwDropServiceHost}:${hcAwDropServicePort}'
  }
  {
    name: 'IntegracionesAw__DropServiceApiKey'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=aw-drop-service-api-key)'
  }
  {
    name: 'GlassAgent__ServicePrincipal__ClientId'
    value: glassAgentSpnClientId
  }
  {
    // Gate del worker de ingesta de pedidos (flujo 2, ADR-0048).
    name: 'Facturacion__Workers__AwSolicitudes__EmpresaId'
    value: awSolicitudesEmpresaId
  }
  {
    name: 'IntegracionesAw__Pedidos__SqlQueryTimeoutSeconds'
    value: string(awPedidosSqlQueryTimeoutSeconds)
  }
  {
    name: 'Facturacion__Workers__AwSolicitudes__PospuestaMaxDias'
    value: string(awSolicitudesPospuestaMaxDias)
  }
  {
    // Capa B de Cajas ([Decisión 12-1]): vigencia de la autorización de
    // apertura ajena, afinable por ambiente sin redeploy de código.
    name: 'Facturacion__Cajas__VigenciaAutorizacionApertura'
    value: cajasVigenciaAutorizacionApertura
  }
  {
    // PR gemelo TES-PR7: sucursal emisora de los REPP automáticos que
    // dispara tesoreria.pago-cliente.confirmado.v1.
    name: 'Facturacion__ReppAutomatico__SucursalClave'
    value: reppAutomaticoSucursalClave
  }
  {
    // Nudge de baja latencia (PR7): la manda MilletAwPedidoNotify.exe desde
    // SER-DATA tras cada INSERT en la tabla-puente. Secreto operator-managed
    // (patrón Soketi): `az keyvault secret set --name aw-pedido-nudge-api-key`
    // + restart. Sin secreto en KV, la ref queda como string literal y el
    // endpoint /pedidos/nudge responde 401 a la key real — inofensivo (el
    // nudge solo adelanta un tick); el worker sigue barriendo por intervalo.
    name: 'IntegracionesAw__Pedidos__NudgeApiKey'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=aw-pedido-nudge-api-key)'
  }
]

// App settings de Blob Storage por módulo. Los tres módulos con puerto
// `IAlmacenarBlobPort` (Compras OC, Almacén, Integraciones.Aw) hacen toggle
// en Program.cs: con connection string usan Azure Blob real; sin ella caen
// al stub filesystem local — efímero en el container Linux, se pierde en
// cada deploy (incidente: PDFs de A+W borrados tras cada merge). El setting
// existía solo como configuración manual del portal y este bloque declarativo
// lo pisaba en cada deploy de infra (mismo drift que aw-sql-onprem).
//
// Los nombres deben coincidir EXACTO con los SectionName del código:
//   Compras__Oc__BlobStorage__ConnectionString  → "Compras:Oc:BlobStorage"
//   Almacen__BlobStorage__ConnectionString      → "Almacen:BlobStorage"
//   IntegracionesAw__BlobStorage__ConnectionString → "IntegracionesAw:BlobStorage"
//   CuentasPorPagar__Cfdi__BlobStorage__ConnectionString → "CuentasPorPagar:Cfdi:BlobStorage"
//   Tesoreria__Repp__BlobStorage__ConnectionString → "Tesoreria:Repp:BlobStorage"
//
// Todos reusan el secreto `storage-connection-string` (misma cuenta que
// DataProtection). Los containers (compras-oc-blobs, almacen-blobs,
// integraciones-aw-blobs, cuentas-por-pagar-cfdis, tesoreria-repp) no se
// declaran en storage.bicep: cada adapter hace CreateIfNotExists y la
// connection string por AccountKey tiene permiso.
var blobStorageAppSettings = [
  {
    name: 'Compras__Oc__BlobStorage__ConnectionString'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=storage-connection-string)'
  }
  {
    name: 'Almacen__BlobStorage__ConnectionString'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=storage-connection-string)'
  }
  {
    name: 'IntegracionesAw__BlobStorage__ConnectionString'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=storage-connection-string)'
  }
  {
    name: 'CuentasPorPagar__Cfdi__BlobStorage__ConnectionString'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=storage-connection-string)'
  }
  {
    // TES-PR8: XML de REPP recibidos de proveedor (ADR-0024). Mismo
    // patrón de toggle: sin esta connection string el adapter cae al stub
    // filesystem efímero. Container tesoreria-repp (CreateIfNotExists).
    name: 'Tesoreria__Repp__BlobStorage__ConnectionString'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=storage-connection-string)'
  }
]

// App settings del publisher Soketi (PR #206). Los 6 secretos son
// OPERATOR-MANAGED — el equipo Tiglass los carga manualmente en KV via
// `az keyvault secret set` (script en PR #206 PowerShell). Bicep solo
// declara las KV refs.
//
// Si los secretos NO están cargados en KV (deploy inicial), la
// resolución de las KV refs falla y el App Service arranca con esos
// settings como string literal `@Microsoft.KeyVault(...)`. El binding
// a `SoketiOptions` falla silenciosamente: `IsEnabled = false` (porque
// AppId/Secret/Host quedan con los valores literales que no matchean
// ningún check útil), y el DI selecciona `NoOpAgentRealtimePublisher`.
// **No hay crash** — el flow del worker sigue funcionando sin push.
//
// Una vez los 6 secretos estén cargados, hace falta un `az webapp restart`
// para que el App Service re-resuelva las KV refs y `SoketiOptions.IsEnabled`
// pase a true. A partir de ahí, el DI usa `SoketiAgentRealtimePublisher`
// real.
var soketiAppSettings = [
  {
    name: 'Soketi__AppId'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=soketi-app-id)'
  }
  {
    name: 'Soketi__Key'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=soketi-key)'
  }
  {
    name: 'Soketi__Secret'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=soketi-secret)'
  }
  {
    name: 'Soketi__Host'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=soketi-host)'
  }
  {
    name: 'Soketi__Port'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=soketi-port)'
  }
  {
    name: 'Soketi__UseTls'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=soketi-tls)'
  }
]

// App settings del SDK FiscalAPI (Integraciones.Fiscal, FAC-DET encendido).
// TenantKey es el X-TENANT-KEY a nivel cuenta — POR AMBIENTE vía el KV de
// cada ambiente (dev = tenant sandbox; prod = tenant productivo cuando
// exista). Secreto OPERATOR-MANAGED, patrón Soketi:
//   az keyvault secret set --vault-name <kv> --name fiscalapi-tenant-key --value <tenant>
// Sin el secreto cargado, la KV ref queda como string literal → el SDK
// falla la llamada → los catálogos SAT responden 503 (el FE degrada a
// captura manual). No hay crash.
// La ApiKey NO va aquí: es por empresa y vive cifrada en ConfiguracionPac
// (ADR-0037/0038), capturada en la ventana de Integraciones Fiscal junto
// con la BaseUrl (modo sandbox = https://test.fiscalapi.com).
var integracionesFiscalAppSettings = [
  {
    name: 'IntegracionesFiscal__Sdk__TenantKey'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=fiscalapi-tenant-key)'
  }
  {
    name: 'IntegracionesFiscal__Sdk__Disabled'
    value: string(integracionesFiscalSdkDisabled)
  }
]

// ============================================================================
// App Service Plan
// ============================================================================

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-${projectName}-${environmentTier}-${regionCode}-01'
  location: location
  tags: tags
  sku: {
    name: planSkuName
    tier: planSkuTier
  }
  kind: 'linux'
  properties: {
    reserved: true // requerido para Linux
  }
}

// ============================================================================
// Web App
// ============================================================================

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: 'app-${projectName}-${environmentTier}-${regionCode}-01'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0'
      // alwaysOn no está soportado en Free/Basic. Solo activarlo en Standard+.
      alwaysOn: planSkuTier != 'Basic' && planSkuTier != 'Free'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/health/ready'
      // CORS app settings se generan dinámicamente desde corsAllowedOrigins
      // y se concatenan al array estático. .NET binding lee
      // `Cors:AllowedOrigins:N` → string[] desde estos.
      appSettings: concat([
        {
          name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE'
          value: 'false'
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: aspNetCoreEnvironment
        }
        // Azure Monitor OpenTelemetry Distro (Azure.Monitor.OpenTelemetry.AspNetCore,
        // ADR-0006) lee este env var DIRECTAMENTE — no via IConfiguration. El nombre
        // exacto requerido es APPLICATIONINSIGHTS_CONNECTION_STRING (mayúsculas,
        // single underscores). Si está mal nombrado, el IHostedService de OTel falla
        // al arrancar y el container crashea con SIGABRT (exit 134) en bucle.
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'ConnectionStrings__Postgres'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=postgres-connection-string)'
        }
        {
          name: 'ServiceBus__ConnectionString'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=service-bus-connection-string)'
        }
        {
          name: 'SignalR__ConnectionString'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=signalr-connection-string)'
        }
        // === DataProtection (ADR-0037, PR-3) ===
        // BlobConnString: KV ref a la connection string del Storage Account.
        // DataProtection persiste el ring de keys en el container
        // dataprotection-keys (blob millet-erp.xml).
        // KeyIdentifier: URI versionado de la DEK. DataProtection lo usa
        // para envolver cada key del ring antes de persistirla. Al rotar
        // la versión en KV (operador), DataProtection re-detecta en el
        // próximo refresh — sin redeploy.
        {
          name: 'DataProtection__BlobConnString'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=storage-connection-string)'
        }
        {
          name: 'DataProtection__KeyIdentifier'
          value: dataProtectionKeyIdentifier
        }
        // === Auth (ADR-0007, ADR-0015) ===
        // Los KV secrets son operator-managed: Bicep solo agrega las
        // referencias. El operador setea los valores con `az keyvault secret
        // set` después del primer deploy (ver infra/README.md).
        {
          name: 'Auth__Jwt__SigningKey'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=auth-jwt-signing-key)'
        }
        {
          name: 'Auth__InitialAdminEntraOid'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=auth-initial-admin-oid)'
        }
        {
          name: 'Auth__EntraId__TenantId'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=auth-entra-tenant-id)'
        }
        {
          name: 'Auth__EntraId__ClientId'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=auth-entra-api-client-id)'
        }
        {
          name: 'Auth__EntraId__Audience'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=auth-entra-api-audience)'
        }
        // Datos de la empresa inicial: van directo (no son sensibles).
        {
          name: 'Auth__Bootstrap__EmpresaInicial__Rfc'
          value: empresaInicialRfc
        }
        {
          name: 'Auth__Bootstrap__EmpresaInicial__RazonSocial'
          value: empresaInicialRazonSocial
        }
        {
          name: 'Auth__Bootstrap__EmpresaInicial__RegimenFiscal'
          value: empresaInicialRegimenFiscal
        }
        {
          name: 'Auth__Bootstrap__EmpresaInicial__NombreComercial'
          value: empresaInicialNombreComercial
        }
      ], corsAppSettings, awIntegrationAppSettings, soketiAppSettings, blobStorageAppSettings, integracionesFiscalAppSettings)
    }
  }
}

// ============================================================================
// Hybrid Connection join (App Service ↔ Azure Relay HCs)
// ============================================================================
// Por cada Hybrid Connection del relay, el App Service necesita un recurso
// `Microsoft.Web/sites/hybridConnectionNamespaces/relays` que la "monte".
// Solo después de este join, cuando código dentro del App Service intenta
// conectarse a `<hostname>:<port>` que matchea exactamente un HC, la
// plataforma intercepta el socket y lo enruta vía Relay → HCM → on-prem.
//
// Wiring condicional: si `relayNamespaceName` no se pasó (modo
// pre-integración, p.ej. primer deploy donde el namespace todavía no
// existe), se omiten ambos joins y el App Service arranca sin las HCs.
// El módulo Integraciones.Aw entonces no podrá tocar on-prem — el wiring
// real se hace cuando main.bicep pase los outputs reales.

resource appHcBusinessSql 'Microsoft.Web/sites/hybridConnectionNamespaces/relays@2023-12-01' = if (!empty(relayNamespaceName) && !empty(hcAwBusinessSqlName)) {
  name: '${webApp.name}/${relayNamespaceName}/${hcAwBusinessSqlName}'
  properties: {
    serviceBusNamespace: relayServiceBusNamespace
    relayName: hcAwBusinessSqlName
    relayArmUri: resourceId('Microsoft.Relay/namespaces/hybridConnections', relayNamespaceName, hcAwBusinessSqlName)
    hostname: hcAwBusinessSqlHost
    port: hcAwBusinessSqlPort
    sendKeyName: 'defaultSender'
    sendKeyValue: hcAwBusinessSqlSendKey
    serviceBusSuffix: '.servicebus.windows.net'
  }
}

resource appHcDropService 'Microsoft.Web/sites/hybridConnectionNamespaces/relays@2023-12-01' = if (!empty(relayNamespaceName) && !empty(hcAwDropServiceName)) {
  name: '${webApp.name}/${relayNamespaceName}/${hcAwDropServiceName}'
  properties: {
    serviceBusNamespace: relayServiceBusNamespace
    relayName: hcAwDropServiceName
    relayArmUri: resourceId('Microsoft.Relay/namespaces/hybridConnections', relayNamespaceName, hcAwDropServiceName)
    hostname: hcAwDropServiceHost
    port: hcAwDropServicePort
    sendKeyName: 'defaultSender'
    sendKeyValue: hcAwDropServiceSendKey
    serviceBusSuffix: '.servicebus.windows.net'
  }
}

// ============================================================================
// Role: Key Vault Secrets User para que el App Service pueda leer los secretos
// vía las referencias @Microsoft.KeyVault(...). Sin este role, las app
// settings se quedan en blanco y la app falla al iniciar.
// ============================================================================

resource keyVault 'Microsoft.KeyVault/vaults@2024-04-01-preview' existing = {
  name: keyVaultName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2024-01-01' existing = {
  name: storageAccountName
}

var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
// PR-3 (ADR-0037): la app necesita wrapKey/unwrapKey sobre la DEK del
// DataProtection en KV. "Key Vault Crypto User" expone justo esas dos
// operaciones (no permite create/delete keys, no permite sign/decrypt).
var keyVaultCryptoUserRoleId = '12338af0-0e69-4776-bea7-57ae8d297424'
// PR-3 (ADR-0037): la app necesita read/write blobs sobre el container
// dataprotection-keys. "Storage Blob Data Contributor" da read+write+delete
// sobre TODOS los containers del storage. Suficiente para Phase 1; si en
// el futuro la app gana otros containers sensibles, se restringe a un
// custom role scoped al container específico.
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource webAppKvSecretsUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, webApp.id, keyVaultSecretsUserRoleId)
  properties: {
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
  }
}

resource webAppKvCryptoUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, webApp.id, keyVaultCryptoUserRoleId)
  properties: {
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultCryptoUserRoleId)
  }
}

resource webAppStorageBlobDataAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(storageAccount.id, webApp.id, storageBlobDataContributorRoleId)
  properties: {
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
  }
}

// ============================================================================
// Outputs
// ============================================================================

output name string = webApp.name
output id string = webApp.id
output principalId string = webApp.identity.principalId
output defaultHostName string = webApp.properties.defaultHostName
