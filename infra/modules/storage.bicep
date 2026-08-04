// ============================================================================
// Módulo: Storage Account
// Almacenamiento de XMLs CFDI, documentos adjuntos, blobs en general.
// Versionamiento y soft-delete habilitados desde el inicio.
// Contenedores iniciales: cfdi-xml, documents.
// ============================================================================

param location string
param projectName string
param environmentTier string
param regionCode string
param tags object

@description('SKU del Storage Account. LRS para dev, GRS para prod.')
@allowed([
  'Standard_LRS'
  'Standard_ZRS'
  'Standard_GRS'
  'Standard_GZRS'
])
param skuName string = 'Standard_LRS'

@description('Días de retención del soft-delete de blobs y contenedores.')
param softDeleteRetentionDays int = 30

resource storageAccount 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: toLower('st${projectName}${environmentTier}${regionCode}01')
  location: location
  tags: tags
  sku: {
    name: skuName
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
    encryption: {
      services: {
        blob: {
          enabled: true
          keyType: 'Account'
        }
        file: {
          enabled: true
          keyType: 'Account'
        }
      }
      keySource: 'Microsoft.Storage'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    isVersioningEnabled: true
    deleteRetentionPolicy: {
      enabled: true
      days: softDeleteRetentionDays
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: softDeleteRetentionDays
    }
  }
}

// Contenedor para XMLs de CFDI emitidos y recibidos.
// Por compliance fiscal estos archivos deben preservarse 5 años.
// La inmutabilidad se configurará por política cuando se ponga en producción.
resource cfdiContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'cfdi-xml'
  properties: {
    publicAccess: 'None'
    metadata: {
      purpose: 'CFDI XML files - 5 year retention required'
    }
  }
}

// Contenedor general para documentos adjuntos del sistema.
resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'documents'
  properties: {
    publicAccess: 'None'
    metadata: {
      purpose: 'General document attachments'
    }
  }
}

// PR-3 (ADR-0037): contenedor del ring de keys de ASP.NET DataProtection.
// El blob millet-erp.xml contiene la lista de keys versionadas que
// DataProtection rota cada 90 días. Cada key se envuelve con la DEK
// (dataprotection-master-key en KV) antes de persistirse.
// publicAccess: None — solo accesible por la Managed Identity del App
// Service (role Storage Blob Data Contributor sobre este container).
resource dataProtectionKeysContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'dataprotection-keys'
  properties: {
    publicAccess: 'None'
    metadata: {
      purpose: 'ASP.NET DataProtection key ring (rotates every 90d)'
    }
  }
}

output name string = storageAccount.name
output id string = storageAccount.id
output primaryEndpointBlob string = storageAccount.properties.primaryEndpoints.blob

@description('Connection string del Storage Account usando AccountKey. La consume el KV-secrets module para persistirla como secret "storage-connection-string"; el App Service la lee como KV ref para DataProtection.')
@secure()
output connectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
