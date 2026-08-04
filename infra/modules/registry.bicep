// ============================================================================
// Módulo: Container Registry
// Registry privado para imágenes Docker del ERP.
// Basic en dev (suficiente), Premium en prod (requerido para private endpoints).
// ============================================================================

param location string
param projectName string
param environmentTier string
param regionCode string
param tags object

@description('SKU del Container Registry.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param skuName string = 'Basic'

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: 'cr${projectName}${environmentTier}${regionCode}01'
  location: location
  tags: tags
  sku: {
    name: skuName
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
    zoneRedundancy: 'Disabled'
    anonymousPullEnabled: false
  }
}

output name string = containerRegistry.name
output id string = containerRegistry.id
output loginServer string = containerRegistry.properties.loginServer
