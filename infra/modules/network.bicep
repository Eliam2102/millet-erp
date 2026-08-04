// ============================================================================
// Módulo: Red virtual
// VNet con tres subredes preparadas para uso futuro:
//   - snet-app: para Container Apps (cuando se agregue en Fase 1)
//   - snet-data: delegada a PostgreSQL Flexible Server (para VNet integration)
//   - snet-pep: para private endpoints en QA y producción
// ============================================================================

param location string
param projectName string
param environmentTier string
param regionCode string
param tags object

// Espacios de direcciones por ambiente. No solapados para permitir
// peering futuro entre ambientes si fuera necesario.
var addressSpaceMap = {
  dev: '10.10.0.0/16'
  qa: '10.20.0.0/16'
  prod: '10.30.0.0/16'
}

var addressSpace = addressSpaceMap[environmentTier]

resource vnet 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: 'vnet-${projectName}-${environmentTier}-${regionCode}'
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [
        addressSpace
      ]
    }
  }
}

resource subnetApp 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = {
  parent: vnet
  name: 'snet-app'
  properties: {
    addressPrefix: cidrSubnet(addressSpace, 24, 0)
  }
}

resource subnetData 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = {
  parent: vnet
  name: 'snet-data'
  properties: {
    addressPrefix: cidrSubnet(addressSpace, 24, 1)
    delegations: [
      {
        name: 'delegation-postgres'
        properties: {
          serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers'
        }
      }
    ]
  }
  dependsOn: [
    subnetApp
  ]
}

resource subnetPep 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = {
  parent: vnet
  name: 'snet-pep'
  properties: {
    addressPrefix: cidrSubnet(addressSpace, 24, 2)
    privateEndpointNetworkPolicies: 'Disabled'
  }
  dependsOn: [
    subnetData
  ]
}

output vnetId string = vnet.id
output vnetName string = vnet.name
output subnetAppId string = subnetApp.id
output subnetDataId string = subnetData.id
output subnetPepId string = subnetPep.id
