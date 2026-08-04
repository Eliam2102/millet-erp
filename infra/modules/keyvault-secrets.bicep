// ============================================================================
// Módulo: Secretos del Key Vault con connection strings de servicios
// El Key Vault central concentra los secretos que las apps consumen vía
// referencias @Microsoft.KeyVault(...) en sus app settings. Phase 1 maneja
// connection strings; Phase 2+ evaluará migrar a managed identity + RBAC.
//
// Tipos de secretos en este archivo:
//
//   1) Secretos COMPUTADOS por Bicep (Service Bus, SignalR): el módulo
//      receptor extrae la connection string vía listKeys() y la persiste
//      aquí en cada deploy. Idempotente — el valor cambia solo si el
//      recurso fuente se recrea.
//
//   2) Secretos OPERATOR-MANAGED (AW: SQL conn string, drop service API
//      key, Glass Agent SPN secret): vienen del exterior (configuración
//      on-prem o de Entra ID), no de un recurso Azure. Bicep solo
//      DECLARA el slot vía param `@secure()` opcional. Si el operador
//      pasa el valor por CLI (`--parameters awSqlConnectionString=...`),
//      Bicep lo materializa; si no, NO se crea el secret y el operador
//      lo setea manualmente con `az keyvault secret set`.
//
//      Esta indirección evita el bug clásico del placeholder: si Bicep
//      forzara un valor "__PENDIENTE__" en cada deploy, sobrescribiría
//      el valor real seteado por el operador en la siguiente corrida de
//      `az deployment sub create`. Con el `if (!empty(...))` el
//      operador conserva control completo.
// ============================================================================

@description('Nombre del Key Vault donde se persisten los secretos.')
param keyVaultName string

// === Computados por Bicep (siempre presentes) ===

@description('Connection string del Service Bus Namespace (RootManageSharedAccessKey).')
@secure()
param serviceBusConnectionString string

@description('Connection string del SignalR Service.')
@secure()
param signalrConnectionString string

@description('Connection string del Storage Account (DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...). Lo consume ASP.NET DataProtection para persistir el key ring en el container dataprotection-keys (ADR-0037).')
@secure()
param storageConnectionString string

// === Operator-managed (opcionales — vacío = no crear, operador setea con `az keyvault secret set`) ===

@description('Connection string al SQL Server AWBUSINESS on-prem (BD MILMAIN) vía Hybrid Connection. Formato: `Server=SER-DATA,1433;Database=MILMAIN;User ID=...;Password=...;Encrypt=False;TrustServerCertificate=True;`. Vacío = no crear; operador lo setea post-deploy.')
@secure()
param awSqlConnectionString string = ''

@description('Connection string a la BD de integración MILLET_INTEGRACION(_DEV) on-prem vía Hybrid Connection (ADR-0048, flujo 2 — ingesta de pedidos → Facturación). Formato: `Server=SER-DATA,1433;Database=MILLET_INTEGRACION;User ID=millet_erp_integracion;Password=...;Encrypt=False;TrustServerCertificate=True;`. Vacío = no crear; operador lo setea post-deploy (dev apunta a _DEV).')
@secure()
param awIntegracionConnectionString string = ''

@description('API key compartida entre AwDropWorker y el drop service on-prem (header `X-API-Key`). Vacío = no crear; generar con `openssl rand -base64 48` y setear manualmente.')
@secure()
param awDropServiceApiKey string = ''

@description('Client secret del service principal `Glass Agent SPN` registrado en Entra ID. Vacío = no crear; el operador lo extrae del portal de Entra (App Registration → Certificates & secrets) y lo sube manualmente.')
@secure()
param glassAgentSpnClientSecret string = ''

resource keyVault 'Microsoft.KeyVault/vaults@2024-04-01-preview' existing = {
  name: keyVaultName
}

// === Computados ===

resource serviceBusSecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'service-bus-connection-string'
  properties: {
    value: serviceBusConnectionString
    contentType: 'text/plain'
  }
}

resource signalrSecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'signalr-connection-string'
  properties: {
    value: signalrConnectionString
    contentType: 'text/plain'
  }
}

// PR-3 (ADR-0037): connection string del Storage Account. La consume el
// App Service como DataProtection__BlobConnString para que el ring de
// keys del DataProtection persista en el container dataprotection-keys.
resource storageConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'storage-connection-string'
  properties: {
    value: storageConnectionString
    contentType: 'text/plain'
  }
}

// === Operator-managed (creación condicional) ===
//
// Si el operador NO pasa el valor por CLI, el recurso no se materializa y
// el secret debe crearse aparte:
//
//   az keyvault secret set --vault-name <kv> --name aw-sql-connection-string --value '<conn-string>'
//   az keyvault secret set --vault-name <kv> --name aw-drop-service-api-key --value '<api-key>'
//   az keyvault secret set --vault-name <kv> --name glass-agent-spn-client-secret --value '<spn-secret>'
//
// Hasta que los tres estén presentes en KV, el App Service que los
// referencia desde appsettings va a fallar a resolver las refs
// @Microsoft.KeyVault(...) y la app puede crashear al arranque (visible
// en /health/ready y en logs del container).

resource awSqlConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = if (!empty(awSqlConnectionString)) {
  parent: keyVault
  name: 'aw-sql-connection-string'
  properties: {
    value: awSqlConnectionString
    contentType: 'text/plain'
  }
}

// ADR-0048 (flujo 2): BD MILLET_INTEGRACION on-prem.
//   az keyvault secret set --vault-name <kv> --name aw-integracion-connection-string --value '<conn-string>'
resource awIntegracionConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = if (!empty(awIntegracionConnectionString)) {
  parent: keyVault
  name: 'aw-integracion-connection-string'
  properties: {
    value: awIntegracionConnectionString
    contentType: 'text/plain'
  }
}

resource awDropServiceApiKeySecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = if (!empty(awDropServiceApiKey)) {
  parent: keyVault
  name: 'aw-drop-service-api-key'
  properties: {
    value: awDropServiceApiKey
    contentType: 'text/plain'
  }
}

resource glassAgentSpnClientSecretSecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = if (!empty(glassAgentSpnClientSecret)) {
  parent: keyVault
  name: 'glass-agent-spn-client-secret'
  properties: {
    value: glassAgentSpnClientSecret
    contentType: 'text/plain'
  }
}

// === Soketi (operator-managed, sin slot Bicep) ===
//
// Los 6 secretos del broker Soketi (hospedado por el Glass Agent on-prem)
// son TOTALMENTE operator-managed: a diferencia de los anteriores, Bicep
// no declara params @secure() para ellos porque el operador los carga una
// sola vez con `az keyvault secret set` y no hace falta poder pasarlos
// por CLI en cada deploy.
//
// Los nombres de los 6 secretos en KV deben ser exactamente:
//   - soketi-app-id     (App ID configurado en el broker Soketi)
//   - soketi-key        (Public key, identifica al ERP como publisher)
//   - soketi-secret     (Secret key, firma los eventos del ERP)
//   - soketi-host       (Hostname público del Agent, ej. agent.millet.mx)
//   - soketi-port       (Puerto público del broker, típicamente 6001 o 443)
//   - soketi-tls        (true|false — si el broker está detrás de TLS)
//
// `infra/modules/appservice.bicep` referencia estos 6 secretos vía KV refs
// para inyectarlos como Soketi__* env vars al App Service. Si los secretos
// NO existen en KV, las refs fallan a resolver y la app arranca con valores
// literales `@Microsoft.KeyVault(...)`, lo que hace que SoketiOptions.IsEnabled
// quede en false y el DI seleccione NoOpAgentRealtimePublisher — el flujo
// del worker sigue funcionando, solo se pierde el push realtime al Agent.
//
// Cargar con:
//   az keyvault secret set --vault-name <kv> --name soketi-app-id     --value '<app-id>'
//   az keyvault secret set --vault-name <kv> --name soketi-key        --value '<key>'
//   az keyvault secret set --vault-name <kv> --name soketi-secret     --value '<secret>'
//   az keyvault secret set --vault-name <kv> --name soketi-host       --value '<host>'
//   az keyvault secret set --vault-name <kv> --name soketi-port       --value '<port>'
//   az keyvault secret set --vault-name <kv> --name soketi-tls        --value '<true|false>'
//
// Después de cargar (o actualizar) los valores, `az webapp restart` sobre
// el App Service para forzar re-resolución de las KV refs.
