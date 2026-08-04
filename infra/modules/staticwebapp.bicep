// ============================================================================
// Módulo: Azure Static Web Apps para el frontend SPA del Millet ERP.
// Recomendación: SKU Free para dev (100GB egress/mes, sobrado para uso interno).
// SPA fallback se configura via staticwebapp.config.json en el artefacto.
//
// SWA NO está disponible en mexicocentral. La región del SWA es independiente
// de la del backend — para Mexico, centralus es la más cercana soportada.
// Ver matrix: https://learn.microsoft.com/azure/static-web-apps/overview#regions
// ============================================================================

@description('Region del Static Web App. SWA tiene matriz limitada — centralus es la más cercana a mexicocentral.')
param location string = 'centralus'

param projectName string
param environmentTier string
param regionCode string
param tags object

@description('SKU del SWA. Free cubre 100GB egress/mes; Standard agrega custom auth + más staging environments.')
@allowed([
  'Free'
  'Standard'
])
param skuName string = 'Free'

// ============================================================================
// Static Web App
//
// provider='None' significa que el SWA NO se integra con un repo de GitHub
// nativamente. En su lugar, deployamos via Azure/static-web-apps-deploy@v1
// (action) usando el deployment token que emite el SWA. Esto nos da control
// fino sobre el pipeline (mismo workflow que el backend) y evita la
// integración OAuth con GitHub que el SWA propone.
// ============================================================================

resource swa 'Microsoft.Web/staticSites@2024-04-01' = {
  name: 'swa-${projectName}-${environmentTier}-${regionCode}-01'
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuName
  }
  properties: {
    provider: 'None'
    // Bicep no puede setear repositoryUrl/branch sin OAuth con GitHub. Si
    // se desean preview environments por PR, el operador conecta el repo
    // post-deploy desde el portal (acción manual one-shot).
  }
}

// ============================================================================
// Outputs
// ============================================================================

output name string = swa.name
output id string = swa.id

// El defaultHostname tiene el formato `<random>.azurestaticapps.net`. Bicep
// lo lee del recurso. Se usa para configurar CORS en el backend.
output defaultHostname string = swa.properties.defaultHostname
