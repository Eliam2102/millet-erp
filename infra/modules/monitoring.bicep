// ============================================================================
// Módulo: Observabilidad
// Log Analytics Workspace + Application Insights (workspace-based).
// En desarrollo se aplica un cap diario de 1 GB para controlar costos.
// ============================================================================

param location string
param projectName string
param environmentTier string
param regionCode string
param tags object

@description('Días de retención de logs.')
param logRetentionDays int = 30

@description('Cap diario de ingesta en GB. -1 significa sin límite. En dev se sugiere 1 GB.')
param cappedDailyQuotaGb int = 1

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${projectName}-${environmentTier}-${regionCode}-01'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: logRetentionDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    workspaceCapping: {
      dailyQuotaGb: cappedDailyQuotaGb
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${projectName}-${environmentTier}-${regionCode}-01'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

output logAnalyticsId string = logAnalytics.id
output logAnalyticsName string = logAnalytics.name
output appInsightsName string = appInsights.name
output appInsightsId string = appInsights.id
output appInsightsConnectionString string = appInsights.properties.ConnectionString
