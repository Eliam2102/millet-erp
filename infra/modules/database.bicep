// ============================================================================
// Módulo: PostgreSQL Flexible Server
// Base de datos transaccional principal del ERP.
// En dev: Burstable B1ms, sin HA, acceso público con firewall.
// En prod: General Purpose D2ds_v5, HA zonal, acceso vía VNet.
// Autenticación por Entra ID (grupo Admins) + password local de respaldo.
// ============================================================================

param location string
param projectName string
param environmentTier string
param regionCode string
param tags object

@description('Usuario administrador local de PostgreSQL.')
param adminUsername string

@description('Password del administrador local.')
@secure()
param adminPassword string

@description('Object ID del grupo de Entra ID que será administrador del servidor.')
param entraAdminGroupObjectId string

@description('Display name del grupo de Entra ID administrador (informativo).')
param entraAdminGroupName string

@description('Habilitar alta disponibilidad zonal. Solo true en producción.')
param enableHighAvailability bool = false

@description('Días de retención de backups.')
param backupRetentionDays int = 7

@description('Tamaño del storage en GB.')
param storageSizeGB int = 32

@description('Versión de PostgreSQL.')
param postgresVersion string = '16'

@description('Nombre del SKU. Burstable B1ms en dev, General Purpose en prod.')
param skuName string = 'Standard_B1ms'

@description('Tier del SKU.')
@allowed([
  'Burstable'
  'GeneralPurpose'
  'MemoryOptimized'
])
param skuTier string = 'Burstable'

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: 'pg-${projectName}-${environmentTier}-${regionCode}-01'
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuTier
  }
  properties: {
    version: postgresVersion
    administratorLogin: adminUsername
    administratorLoginPassword: adminPassword
    storage: {
      storageSizeGB: storageSizeGB
      autoGrow: 'Enabled'
    }
    backup: {
      backupRetentionDays: backupRetentionDays
      geoRedundantBackup: environmentTier == 'prod' ? 'Enabled' : 'Disabled'
    }
    highAvailability: {
      mode: enableHighAvailability ? 'ZoneRedundant' : 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Enabled'
      tenantId: subscription().tenantId
    }
  }
}

// Permitir conexiones desde servicios de Azure (Container Apps, etc.)
// Esta regla con 0.0.0.0 -> 0.0.0.0 es la sintaxis especial para "Allow Azure services".
resource firewallAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'AllowAllAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Configurar el grupo de Admins como administrador de Entra ID.
// Esto permite que los miembros del grupo se conecten con su cuenta corporativa
// sin necesidad de la password local.
resource entraAdmin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = {
  parent: postgres
  name: entraAdminGroupObjectId
  properties: {
    principalType: 'Group'
    principalName: entraAdminGroupName
    tenantId: subscription().tenantId
  }
  dependsOn: [
    firewallAzure
  ]
}

output name string = postgres.name
output id string = postgres.id
output fqdn string = postgres.properties.fullyQualifiedDomainName
