// ============================================================================
// Módulo: Hybrid Connections (Azure Relay)
// Crea las dos Hybrid Connections que permiten al App Service alcanzar
// recursos on-prem en `SER-DATA` sin abrir puertos entrantes en el firewall
// de Millet:
//
//   1. `hc-aw-business-sql`     → SER-DATA:1433 (SQL Server AWBUSINESS)
//   2. `hc-aw-drop-service`     → SER-DATA:5000 (drop service .NET 8)
//
// Auth a nivel de TCP forwarding:
//   - `requiresClientAuthorization: false` — el Hybrid Connection NO exige
//     un token de autorización por cada paquete forwardeado a nivel de
//     protocolo Relay. La autenticación real vive en la capa aplicación:
//     · SQL Server: SQL login `awserv` (o `millet_erp_reader`) por TDS.
//     · Drop service: API key vía header `X-API-Key`.
//   - El SAS de Send/Listen sí sigue siendo requerido para que App Service
//     pueda registrar la conexión y HCM pueda registrar el listener.
//
// Por cada Hybrid Connection se crean dos SAS rules:
//   - `defaultListener` (rights: Listen)  — usada por HCM on-prem para
//     registrar el endpoint y aceptar conexiones forwardeadas.
//   - `defaultSender`   (rights: Send)    — usada por el App Service para
//     iniciar conexiones. La key se inyecta en el recurso `Microsoft.Web/sites
//     /hybridConnectionNamespaces/relays` desde `appservice.bicep`.
//
// Después del deploy, el operador descarga el connection string de
// `defaultListener` (vía portal o `az relay hyco authorization-rule keys
// list`) y lo configura en el HCM on-prem para cada Hybrid Connection.
// ============================================================================

@description('Nombre del Azure Relay namespace ya creado (output de relay.bicep).')
param relayNamespaceName string

@description('Hostname on-prem destino (FQDN o NetBIOS). Default `SER-DATA` per overview §4.4. Usado por el drop service; el SQL usa `sqlSyntheticHostname`.')
param onPremHostname string = 'SER-DATA'

@description('''Hostname SINTÉTICO para la HC del SQL Server. Fix del incidente
2026-07-06: con `SER-DATA` como endpoint, el HCM on-prem resolvía el nombre
propio de la máquina a su IPv6 y el listener se colgaba; con IP literal, la
interceptación DNS del App Service (Linux) no funciona — solo intercepta
hostnames. Solución: hostname sintético que (a) el App Service intercepta y
enruta por el Relay, y (b) SER-DATA resuelve a 127.0.0.1 vía entrada en su
archivo hosts (paso manual del runbook). Las connection strings en KV
(`aw-sql-connection-string`, `aw-integracion-connection-string`) usan este
hostname como `Server=`.''')
param sqlSyntheticHostname string = 'aw-sql-onprem'

@description('Puerto del SQL Server AWBUSINESS on-prem. Hoy en `49900` dinámico — el overview pide cambiarlo a `1433` estático antes de operar (acción coordinada con downtime).')
param sqlPort int = 1433

@description('Puerto del drop service .NET 8 on-prem (escucha en `localhost:5000`).')
param dropServicePort int = 5000

// ============================================================================
// Namespace existente (referenciado, no creado aquí)
// ============================================================================

resource relayNamespace 'Microsoft.Relay/namespaces@2024-01-01' existing = {
  name: relayNamespaceName
}

// ============================================================================
// Hybrid Connection 1: SQL Server AWBUSINESS (lectura para AwCorrelationWorker)
// ============================================================================

resource hcAwBusinessSql 'Microsoft.Relay/namespaces/hybridConnections@2024-01-01' = {
  parent: relayNamespace
  name: 'hc-aw-business-sql'
  properties: {
    // No exigir token de autorización por mensaje a nivel Relay (auth real
    // = SQL login). Hybrid Connection forwardea TCP raw entre App Service y
    // SQL Server.
    requiresClientAuthorization: false
    // userMetadata es texto libre que aparece en el portal — útil para
    // operación. Documentar el target hace explícito a qué apunta cada HC.
    // El HCM lee el endpoint de aquí: hostname sintético (ver param).
    userMetadata: '[{"key":"endpoint","value":"${sqlSyntheticHostname}:${sqlPort}"},{"key":"target","value":"SQL Server AWBUSINESS (read-only) via hostname sintetico — SER-DATA lo resuelve por hosts"}]'
  }
}

resource hcAwBusinessSqlListener 'Microsoft.Relay/namespaces/hybridConnections/authorizationRules@2024-01-01' = {
  parent: hcAwBusinessSql
  name: 'defaultListener'
  properties: {
    rights: [
      'Listen'
    ]
  }
}

resource hcAwBusinessSqlSender 'Microsoft.Relay/namespaces/hybridConnections/authorizationRules@2024-01-01' = {
  parent: hcAwBusinessSql
  name: 'defaultSender'
  properties: {
    rights: [
      'Send'
    ]
  }
}

// ============================================================================
// Hybrid Connection 2: Drop service on-prem (recibe EDI desde AwDropWorker)
// ============================================================================

resource hcAwDropService 'Microsoft.Relay/namespaces/hybridConnections@2024-01-01' = {
  parent: relayNamespace
  name: 'hc-aw-drop-service'
  properties: {
    requiresClientAuthorization: false
    userMetadata: '[{"key":"endpoint","value":"${onPremHostname}:${dropServicePort}"},{"key":"target","value":"Drop service (.NET 8 Windows Service) que escribe EDI a carpeta de import A+W"}]'
  }
}

resource hcAwDropServiceListener 'Microsoft.Relay/namespaces/hybridConnections/authorizationRules@2024-01-01' = {
  parent: hcAwDropService
  name: 'defaultListener'
  properties: {
    rights: [
      'Listen'
    ]
  }
}

resource hcAwDropServiceSender 'Microsoft.Relay/namespaces/hybridConnections/authorizationRules@2024-01-01' = {
  parent: hcAwDropService
  name: 'defaultSender'
  properties: {
    rights: [
      'Send'
    ]
  }
}

// ============================================================================
// Outputs
// ============================================================================
//
// Las keys de `defaultSender` se exponen como @secure() para que `main.bicep`
// las pase a `appservice.bicep` y este construya los recursos de join sin
// que las keys se loguen en el deployment output.

@description('Nombre de la Hybrid Connection del SQL Server AWBUSINESS.')
output hcAwBusinessSqlName string = hcAwBusinessSql.name

@description('Resource ID de la Hybrid Connection del SQL Server AWBUSINESS.')
output hcAwBusinessSqlId string = hcAwBusinessSql.id

@description('Hostname destino para el SQL Server (necesario en el recurso de join del App Service). Sintético — ver `sqlSyntheticHostname`.')
output hcAwBusinessSqlHost string = sqlSyntheticHostname

@description('Puerto destino on-prem para el SQL Server.')
output hcAwBusinessSqlPort int = sqlPort

@secure()
@description('Primary key de la SAS rule `defaultSender` para hc-aw-business-sql. Inyectada en el recurso de join del App Service.')
output hcAwBusinessSqlSendKey string = hcAwBusinessSqlSender.listKeys().primaryKey

@description('Nombre de la Hybrid Connection del drop service.')
output hcAwDropServiceName string = hcAwDropService.name

@description('Resource ID de la Hybrid Connection del drop service.')
output hcAwDropServiceId string = hcAwDropService.id

@description('Hostname destino on-prem para el drop service.')
output hcAwDropServiceHost string = onPremHostname

@description('Puerto destino on-prem para el drop service.')
output hcAwDropServicePort int = dropServicePort

@secure()
@description('Primary key de la SAS rule `defaultSender` para hc-aw-drop-service.')
output hcAwDropServiceSendKey string = hcAwDropServiceSender.listKeys().primaryKey
