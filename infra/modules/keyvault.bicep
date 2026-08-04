// ============================================================================
// Módulo: Key Vault
// Almacén central de secretos, certificados y connection strings.
// Usa autorización RBAC (no las viejas Access Policies).
// En dev no se habilita purge protection para permitir destruir/recrear.
// En prod la purge protection es obligatoria por compliance.
// ============================================================================

param location string
param projectName string
param environmentTier string
param regionCode string
param tags object

@description('Habilitar purge protection. Una vez habilitado NO se puede deshabilitar. Solo true en producción.')
param enablePurgeProtection bool = false

resource keyVault 'Microsoft.KeyVault/vaults@2024-04-01-preview' = {
  name: 'kv-${projectName}-${environmentTier}-${regionCode}-01'
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    // Requerido para que ARM resuelva referencias getSecret() de los
    // .bicepparam (postgresAdminPassword) al momento del deploy. OJO:
    // ARM evalúa esta propiedad ANTES de aplicar la plantilla, así que
    // en un vault ya existente debe flipearse una vez por CLI:
    //   az keyvault update -n <kv> --enabled-for-template-deployment true
    // Aquí queda codificada para anti-drift y para ambientes nuevos.
    enabledForTemplateDeployment: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: enablePurgeProtection ? true : null
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// ============================================================================
// PR-3 (ADR-0037): DEK del ASP.NET DataProtection. RSA 2048, operaciones
// limitadas a wrapKey/unwrapKey (no permite sign/decrypt directo — minimiza
// blast radius si la Managed Identity del App Service se compromete). El
// ring de keys de DataProtection (XML con varias keys versionadas) vive
// en Azure Blob Storage y se protege envolviéndolo con esta key.
//
// El URI versionado de esta key se inyecta al App Service como app setting
// DataProtection__KeyIdentifier. Cuando el operador rota la key (operación
// manual desde portal o `az keyvault key rotate`), el App Service NO necesita
// redeploy: DataProtection re-detecta versión activa al próximo refresh
// del ring. Las versiones viejas siguen siendo descifrables mientras la
// purge protection esté activa.
// ============================================================================
resource dataProtectionMasterKey 'Microsoft.KeyVault/vaults/keys@2024-04-01-preview' = {
  parent: keyVault
  name: 'dataprotection-master-key'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: ['wrapKey', 'unwrapKey']
  }
}

output name string = keyVault.name
output id string = keyVault.id
output uri string = keyVault.properties.vaultUri

@description('URI versionado de la DEK de DataProtection. Inyectado al App Service como DataProtection__KeyIdentifier.')
output dataProtectionKeyIdentifier string = dataProtectionMasterKey.properties.keyUriWithVersion
