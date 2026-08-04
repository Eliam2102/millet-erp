// ============================================================================
// Módulo: Asignaciones RBAC
// Asigna roles a los grupos de Entra ID:
//   - Admins: Owner sobre el resource group + Key Vault Administrator
//   - Developers: Contributor sobre el resource group + Key Vault Secrets User
// ============================================================================

@description('Object ID del grupo de Admins.')
param adminsGroupObjectId string

@description('Object ID del grupo de Developers.')
param developersGroupObjectId string

@description('Nombre del Key Vault para asignaciones específicas.')
param keyVaultName string

// ----------------------------------------------------------------------------
// Definiciones de roles built-in de Azure
// ----------------------------------------------------------------------------
var ownerRoleId = '8e3af657-a8ff-443c-a75c-2fe8c4bcb635'
var contributorRoleId = 'b24988ac-6180-42a0-ab88-20f7382dd24c'
var keyVaultAdministratorRoleId = '00482a5a-887f-4fb3-b363-3b7fe8e74483'
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

// ----------------------------------------------------------------------------
// Owner sobre el resource group para Admins
// ----------------------------------------------------------------------------
resource adminsOwnerAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, adminsGroupObjectId, ownerRoleId)
  properties: {
    principalId: adminsGroupObjectId
    principalType: 'Group'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', ownerRoleId)
  }
}

// ----------------------------------------------------------------------------
// Contributor sobre el resource group para Developers
// ----------------------------------------------------------------------------
resource developersContributorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, developersGroupObjectId, contributorRoleId)
  properties: {
    principalId: developersGroupObjectId
    principalType: 'Group'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', contributorRoleId)
  }
}

// ----------------------------------------------------------------------------
// Key Vault Administrator sobre el Key Vault para Admins
// ----------------------------------------------------------------------------
resource keyVault 'Microsoft.KeyVault/vaults@2024-04-01-preview' existing = {
  name: keyVaultName
}

resource adminsKvAdminAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, adminsGroupObjectId, keyVaultAdministratorRoleId)
  properties: {
    principalId: adminsGroupObjectId
    principalType: 'Group'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultAdministratorRoleId)
  }
}

// ----------------------------------------------------------------------------
// Key Vault Secrets User sobre el Key Vault para Developers
// (Solo lectura de secretos, no puede crear ni modificar)
// ----------------------------------------------------------------------------
resource developersKvSecretsAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, developersGroupObjectId, keyVaultSecretsUserRoleId)
  properties: {
    principalId: developersGroupObjectId
    principalType: 'Group'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
  }
}
