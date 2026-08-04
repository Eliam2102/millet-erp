// ============================================================================
// MILLET ERP - Infraestructura principal
// Despliegue a nivel de suscripción que crea el resource group y orquesta
// los módulos de servicios PaaS.
// ============================================================================

targetScope = 'subscription'

// ============================================================================
// PARÁMETROS
// ============================================================================

@description('Ambiente de despliegue. Controla SKUs, alta disponibilidad y otras decisiones de costo/robustez.')
@allowed([
  'dev'
  'qa'
  'prod'
])
param environmentTier string

@description('Región de Azure para todos los recursos.')
param location string = 'mexicocentral'

@description('Región para Azure SignalR Service. mexicocentral NO está soportado; default a southcentralus (la más cercana a MX). Ver ADR-0001 para racional.')
param signalrLocation string = 'southcentralus'

@description('Región para Azure Static Web Apps (frontend). mexicocentral NO está soportado; default a centralus (la más cercana a MX).')
param staticWebAppLocation string = 'centralus'

@description('Región para Azure Relay (Hybrid Connections con A+W on-prem). mexicocentral NO está soportado al momento de escritura; default a southcentralus (misma región que SignalR para consolidar latencia). Verificar con `az provider show -n Microsoft.Relay --query resourceTypes[?resourceType==\'namespaces\'].locations`.')
param relayLocation string = 'southcentralus'

@description('Código corto de región usado en nombres de recursos.')
@maxLength(4)
param regionCode string = 'mxc'

@description('Nombre corto del proyecto (3-6 caracteres, solo minúsculas).')
@minLength(3)
@maxLength(6)
param projectName string

@description('Object ID del grupo de Entra ID con rol Owner sobre el resource group.')
param adminsGroupObjectId string

@description('Object ID del grupo de Entra ID con rol Contributor sobre el resource group.')
param developersGroupObjectId string

@description('Nombre legible del grupo de administradores (para registros).')
param adminsGroupDisplayName string = 'MILLET-ERP-Admins'

@description('Correo del responsable del proyecto. Se usa como tag.')
param ownerEmail string

@description('Centro de costos para tags de gobierno.')
param costCenter string = 'it-projects'

@description('Monto mensual del presupuesto de alertas en USD.')
param budgetAmount int = 150

@description('Direcciones de correo que reciben alertas de presupuesto.')
param budgetAlertEmails array

@description('Fecha de inicio del presupuesto (primer día de un mes). OJO: Azure NO permite actualizar startDate de un budget existente - una vez creado el budget del ambiente, FIJAR este valor en el .bicepparam con la fecha original (dev = 2026-05-01); el default de mes corriente solo sirve para la PRIMERA creación.')
param budgetStartDate string = '${utcNow('yyyy-MM')}-01'

@description('Usuario administrador de PostgreSQL (login local).')
param postgresAdminUsername string = 'pgadmin'

@description('Contraseña del administrador de PostgreSQL. Se debe pasar al desplegar via parámetro CLI, NUNCA dejarla en archivos versionados.')
@secure()
@minLength(12)
param postgresAdminPassword string

// === Bootstrap del SuperAdmin (ADR-0007) ===
// Los KV secrets (jwt-signing-key, initial-admin-oid, entra-*) son operator-
// managed (manual via az keyvault secret set; Bicep solo referencia). Los
// datos de empresa inicial sí vienen via parámetros porque NO son sensibles.

@description('RFC de la empresa inicial creada por el bootstrap del SuperAdmin. Vacío = no crear empresa (operador la crea después).')
param empresaInicialRfc string = ''

@description('Razón social de la empresa inicial.')
param empresaInicialRazonSocial string = ''

@description('Régimen fiscal SAT. Default 601 (Personas Morales).')
param empresaInicialRegimenFiscal string = '601'

@description('Nombre comercial opcional de la empresa inicial.')
param empresaInicialNombreComercial string = ''

// ============================================================================
// Integración A+W (módulo Millet.Integraciones.Aw)
//
// Parámetros de conectividad hacia el servidor on-prem `SER-DATA` de Millet
// vía Azure Hybrid Connection. Defaults pensados para dev y prod (mismo
// servidor on-prem físico). Para QA con un servidor on-prem distinto se
// pueden overridear en el `.bicepparam` del ambiente.
// ============================================================================

@description('Hostname on-prem destino de las Hybrid Connections. Default `SER-DATA` per overview §4.4.')
param awOnPremHostname string = 'SER-DATA'

@description('Puerto del SQL Server AWBUSINESS on-prem. Default `1433` (puerto estático coordinado tras downtime — hoy 49900 dinámico).')
param awSqlPort int = 1433

@description('Puerto del drop service .NET 8 on-prem.')
param awDropServicePort int = 5000

@description('Client ID (appid, public) del service principal `Glass Agent SPN` en Entra ID. Vacío hasta que se cree la App Registration; el módulo Integraciones.Aw lee este valor para validar tokens `appid` (D3).')
param glassAgentSpnClientId string = ''

@description('EmpresaId (GUID) que procesa el AwSolicitudesWorker (ingesta de pedidos A+W → Facturación, ADR-0048). Vacío = worker apagado. Ver appservice.bicep.')
param awSolicitudesEmpresaId string = ''

@description('Apaga el SDK de FiscalAPI. Default true; dev lo enciende en su .bicepparam (tenant sandbox en el KV dev). Ver appservice.bicep.')
param integracionesFiscalSdkDisabled bool = true

// ============================================================================
// VARIABLES
// ============================================================================

var resourceGroupName = 'rg-${projectName}-${environmentTier}-${regionCode}-01'

var commonTags = {
  Project: '${projectName}-erp'
  Environment: environmentTier
  Owner: ownerEmail
  CostCenter: costCenter
  CreatedBy: 'bicep'
  DataClassification: 'internal'
}

// Decisiones por ambiente
var isProd = environmentTier == 'prod'
var enableHighAvailability = isProd
var logRetentionDays = isProd ? 90 : 30
var backupRetentionDays = isProd ? 35 : 7
var enableKeyVaultPurgeProtection = isProd

// Mapeo de environmentTier a ASPNETCORE_ENVIRONMENT (estándar de .NET)
var aspNetCoreEnvironmentMap = {
  dev: 'Development'
  qa: 'Staging'
  prod: 'Production'
}

// ============================================================================
// RESOURCE GROUP
// ============================================================================

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: commonTags
}

// ============================================================================
// MÓDULOS
// ============================================================================

module network 'modules/network.bicep' = {
  scope: rg
  name: 'deploy-network'
  params: {
    location: location
    projectName: projectName
    environmentTier: environmentTier
    regionCode: regionCode
    tags: commonTags
  }
}

module monitoring 'modules/monitoring.bicep' = {
  scope: rg
  name: 'deploy-monitoring'
  params: {
    location: location
    projectName: projectName
    environmentTier: environmentTier
    regionCode: regionCode
    tags: commonTags
    logRetentionDays: logRetentionDays
    cappedDailyQuotaGb: isProd ? -1 : 1
  }
}

module keyvault 'modules/keyvault.bicep' = {
  scope: rg
  name: 'deploy-keyvault'
  params: {
    location: location
    projectName: projectName
    environmentTier: environmentTier
    regionCode: regionCode
    tags: commonTags
    enablePurgeProtection: enableKeyVaultPurgeProtection
  }
}

module storage 'modules/storage.bicep' = {
  scope: rg
  name: 'deploy-storage'
  params: {
    location: location
    projectName: projectName
    environmentTier: environmentTier
    regionCode: regionCode
    tags: commonTags
    skuName: isProd ? 'Standard_GRS' : 'Standard_LRS'
  }
}

module registry 'modules/registry.bicep' = {
  scope: rg
  name: 'deploy-registry'
  params: {
    location: location
    projectName: projectName
    environmentTier: environmentTier
    regionCode: regionCode
    tags: commonTags
    skuName: isProd ? 'Premium' : 'Basic'
  }
}

module database 'modules/database.bicep' = {
  scope: rg
  name: 'deploy-database'
  params: {
    location: location
    projectName: projectName
    environmentTier: environmentTier
    regionCode: regionCode
    tags: commonTags
    adminUsername: postgresAdminUsername
    adminPassword: postgresAdminPassword
    entraAdminGroupObjectId: adminsGroupObjectId
    entraAdminGroupName: adminsGroupDisplayName
    enableHighAvailability: enableHighAvailability
    backupRetentionDays: backupRetentionDays
    storageSizeGB: isProd ? 128 : 32
    skuName: isProd ? 'Standard_D2ds_v5' : 'Standard_B1ms'
    skuTier: isProd ? 'GeneralPurpose' : 'Burstable'
  }
}

module servicebus 'modules/servicebus.bicep' = {
  scope: rg
  name: 'deploy-servicebus'
  params: {
    location: location
    projectName: projectName
    environmentTier: environmentTier
    regionCode: regionCode
    tags: commonTags
    skuName: 'Standard'
  }
}

module signalr 'modules/signalr.bicep' = {
  scope: rg
  name: 'deploy-signalr'
  params: {
    // SignalR Service no soporta mexicocentral; usa signalrLocation
    // (default southcentralus) como excepción documentada en ADR-0001.
    location: signalrLocation
    projectName: projectName
    environmentTier: environmentTier
    regionCode: regionCode
    tags: commonTags
    skuName: isProd ? 'Standard_S1' : 'Free_F1'
    capacity: 1
  }
}

// ============================================================================
// Azure Relay + Hybrid Connections (Integración A+W on-prem)
//
// Azure Relay NO está soportado en mexicocentral al momento de escritura;
// usa `relayLocation` (default southcentralus, igual que SignalR) como
// excepción documentada en ADR-0001 §"Notas de implementación".
//
// El namespace se crea primero; las dos Hybrid Connections (SQL + drop
// service) dependen de él. Bicep infiere la dependencia automáticamente
// vía el output `relay.outputs.namespaceName` consumido por
// `hybridConnections`.
// ============================================================================

module relay 'modules/relay.bicep' = {
  scope: rg
  name: 'deploy-relay'
  params: {
    location: relayLocation
    projectName: projectName
    environmentTier: environmentTier
    regionCode: regionCode
    tags: commonTags
  }
}

module hybridConnections 'modules/hybridConnections.bicep' = {
  scope: rg
  name: 'deploy-hybrid-connections'
  params: {
    relayNamespaceName: relay.outputs.namespaceName
    onPremHostname: awOnPremHostname
    sqlPort: awSqlPort
    dropServicePort: awDropServicePort
  }
}

// Persistir connection strings de Service Bus y SignalR en Key Vault. El App
// Service las consume vía referencias @Microsoft.KeyVault(...) en sus app
// settings. Phase 1 usa connection strings; Phase 2+ evaluará migrar a
// managed identity + RBAC eliminando estas secretos.
module connectionStringSecrets 'modules/keyvault-secrets.bicep' = {
  scope: rg
  name: 'deploy-conn-string-secrets'
  params: {
    keyVaultName: keyvault.outputs.name
    serviceBusConnectionString: servicebus.outputs.primaryConnectionString
    signalrConnectionString: signalr.outputs.primaryConnectionString
    storageConnectionString: storage.outputs.connectionString
  }
}

module staticwebapp 'modules/staticwebapp.bicep' = {
  scope: rg
  name: 'deploy-staticwebapp'
  params: {
    // SWA no soporta mexicocentral; usa staticWebAppLocation (default centralus).
    location: staticWebAppLocation
    projectName: projectName
    environmentTier: environmentTier
    regionCode: regionCode
    tags: commonTags
    skuName: 'Free'
  }
}

module appservice 'modules/appservice.bicep' = {
  scope: rg
  name: 'deploy-appservice'
  params: {
    location: location
    projectName: projectName
    environmentTier: environmentTier
    regionCode: regionCode
    tags: commonTags
    keyVaultName: keyvault.outputs.name
    storageAccountName: storage.outputs.name
    dataProtectionKeyIdentifier: keyvault.outputs.dataProtectionKeyIdentifier
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    planSkuName: isProd ? 'P1v3' : 'B1'
    planSkuTier: isProd ? 'PremiumV3' : 'Basic'
    aspNetCoreEnvironment: aspNetCoreEnvironmentMap[environmentTier]
    empresaInicialRfc: empresaInicialRfc
    empresaInicialRazonSocial: empresaInicialRazonSocial
    empresaInicialRegimenFiscal: empresaInicialRegimenFiscal
    empresaInicialNombreComercial: empresaInicialNombreComercial
    // CORS: el frontend SWA vive en otro hostname → preflight OPTIONS
    // requiere AllowedOrigins explícito. El backend leía vacío antes.
    corsAllowedOrigins: [
      'https://${staticwebapp.outputs.defaultHostname}'
    ]
    // Integración A+W: wiring del relay namespace + ambas Hybrid
    // Connections (SQL + drop service) y app settings del módulo
    // Millet.Integraciones.Aw. Las @secure() send keys viajan de un
    // output @secure de hybridConnections.bicep al param @secure
    // de appservice.bicep — el deployment output no las loguea.
    relayNamespaceName: relay.outputs.namespaceName
    relayServiceBusNamespace: relay.outputs.serviceBusNamespace
    hcAwBusinessSqlName: hybridConnections.outputs.hcAwBusinessSqlName
    hcAwBusinessSqlHost: hybridConnections.outputs.hcAwBusinessSqlHost
    hcAwBusinessSqlPort: hybridConnections.outputs.hcAwBusinessSqlPort
    hcAwBusinessSqlSendKey: hybridConnections.outputs.hcAwBusinessSqlSendKey
    hcAwDropServiceName: hybridConnections.outputs.hcAwDropServiceName
    hcAwDropServiceHost: hybridConnections.outputs.hcAwDropServiceHost
    hcAwDropServicePort: hybridConnections.outputs.hcAwDropServicePort
    hcAwDropServiceSendKey: hybridConnections.outputs.hcAwDropServiceSendKey
    glassAgentSpnClientId: glassAgentSpnClientId
    awSolicitudesEmpresaId: awSolicitudesEmpresaId
    integracionesFiscalSdkDisabled: integracionesFiscalSdkDisabled
  }
  // Esperar a que los secretos estén en KV antes de arrancar el App Service:
  // las referencias @Microsoft.KeyVault(...) deben resolver al primer arranque.
  dependsOn: [
    connectionStringSecrets
  ]
}

module rbac 'modules/rbac.bicep' = {
  scope: rg
  name: 'deploy-rbac'
  params: {
    adminsGroupObjectId: adminsGroupObjectId
    developersGroupObjectId: developersGroupObjectId
    keyVaultName: keyvault.outputs.name
  }
}

module budget 'modules/budget.bicep' = {
  scope: rg
  name: 'deploy-budget'
  params: {
    budgetName: 'budget-${projectName}-${environmentTier}'
    amount: budgetAmount
    alertEmails: budgetAlertEmails
    startDate: budgetStartDate
  }
}

// CollaborationHub Sprint 3: alertas Azure Monitor sobre SignalR Service.
// Action Group → email del owner; metric alerts SystemErrors + ServerLoad.
module monitoringAlerts 'modules/monitoring-alerts.bicep' = {
  scope: rg
  name: 'deploy-monitoring-alerts'
  params: {
    projectName: projectName
    environmentTier: environmentTier
    regionCode: regionCode
    tags: commonTags
    notificationEmail: ownerEmail
    signalrId: signalr.outputs.id
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output resourceGroupName string = rg.name
output postgresFqdn string = database.outputs.fqdn
output postgresName string = database.outputs.name
output keyVaultName string = keyvault.outputs.name
output keyVaultUri string = keyvault.outputs.uri
output storageAccountName string = storage.outputs.name
output containerRegistryLoginServer string = registry.outputs.loginServer
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
output logAnalyticsWorkspaceId string = monitoring.outputs.logAnalyticsId
output vnetName string = network.outputs.vnetName
output appServiceName string = appservice.outputs.name
output appServiceHostName string = appservice.outputs.defaultHostName
output serviceBusEndpoint string = servicebus.outputs.endpoint
output signalrHostName string = signalr.outputs.hostName
output staticWebAppName string = staticwebapp.outputs.name
output staticWebAppHostName string = staticwebapp.outputs.defaultHostname
output relayNamespaceName string = relay.outputs.namespaceName
output relayServiceBusNamespace string = relay.outputs.serviceBusNamespace
output hcAwBusinessSqlName string = hybridConnections.outputs.hcAwBusinessSqlName
output hcAwDropServiceName string = hybridConnections.outputs.hcAwDropServiceName
