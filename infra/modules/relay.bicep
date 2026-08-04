// ============================================================================
// Módulo: Azure Relay Namespace
// Sostiene las Hybrid Connections que comunican el App Service del ERP con
// recursos on-prem en `SER-DATA` (SQL Server AWBUSINESS y drop service .NET 8).
// Las Hybrid Connections en sí se crean en `hybridConnections.bicep`.
//
// SKU:
//   - Standard es la única opción que soporta Hybrid Connections (Basic NO
//     soporta este tipo de relay; solo WCF Relay clásico).
//   - No hay tier "Premium"; no hay diferencia de SKU entre dev/qa/prod.
//
// Region: Azure Relay NO está disponible en `mexicocentral` al momento de
// escritura (verificar con `az provider show -n Microsoft.Relay --query
// resourceTypes[?resourceType=='namespaces'].locations`). Por eso este módulo
// recibe su propio param `location` (no usa el global), siguiendo el mismo
// patrón documentado para SignalR Service (ADR-0001 §"Notas de
// implementación"). Default: `southcentralus` (~50 ms desde MX, misma región
// que SignalR para consolidar latencia).
//
// El relay es un transporte (TCP forwarding sobre TLS): no almacena payloads,
// solo enruta. Esta excepción de región NO rompe data residency del ERP.
//
// Networking: HCM on-prem hace UNA conexión saliente HTTPS (puerto 443) hacia
// este namespace y multiplexa todas las Hybrid Connections que apunten a
// `SER-DATA`. No requiere abrir puertos entrantes en el firewall del cliente.
// ============================================================================

@description('Región del recurso. Azure Relay NO está soportado en mexicocentral; usar una región cercana (southcentralus por default, igual que SignalR).')
param location string

@description('Nombre corto del proyecto (3-6 caracteres, solo minúsculas).')
param projectName string

@description('Ambiente (dev | qa | prod).')
param environmentTier string

@description('Código de región usado en el nombre (default mxc aunque el recurso viva en otra región — convención de naming del proyecto).')
param regionCode string

@description('Tags estándar del proyecto.')
param tags object

resource relayNamespace 'Microsoft.Relay/namespaces@2024-01-01' = {
  name: 'relay-${projectName}-${environmentTier}-${regionCode}-01'
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    // Permitir tráfico público entrante (Hybrid Connections requiere endpoint
    // público; HCM on-prem se conecta saliente a este endpoint). El control
    // de acceso se hace por SAS keys de las Hybrid Connections individuales.
    publicNetworkAccess: 'Enabled'
  }
}

// La regla `RootManageSharedAccessKey` la crea Azure por default al crear el
// namespace. Se referencia para outputs de gobierno (no se usa en runtime —
// cada Hybrid Connection tiene sus propias SAS rules).
resource rootAuthRule 'Microsoft.Relay/namespaces/authorizationRules@2024-01-01' existing = {
  parent: relayNamespace
  name: 'RootManageSharedAccessKey'
}

// ============================================================================
// Outputs
// ============================================================================

@description('Nombre del Relay namespace (usado para construir IDs de hybrid connections).')
output namespaceName string = relayNamespace.name

@description('Resource ID del Relay namespace.')
output namespaceId string = relayNamespace.id

@description('Resource ID de la regla RootManageSharedAccessKey (gobierno; el wiring runtime usa SAS rules per-hybridConnection).')
output defaultAuthorizationRuleId string = rootAuthRule.id

@description('Hostname del namespace (formato `<name>.servicebus.windows.net`). El App Service lo necesita en la propiedad `serviceBusNamespace` del recurso de join.')
output serviceBusNamespace string = '${relayNamespace.name}.servicebus.windows.net'
