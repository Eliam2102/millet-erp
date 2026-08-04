// ============================================================================
// Módulo: Alertas de monitoreo (Azure Monitor)
// Action Group con email + metric alerts sobre Azure SignalR Service.
// CollaborationHub Sprint 3, ADR-0019.
//
// Alcance Phase 1: solo alertas de plataforma SignalR. Las alertas de
// nivel aplicación (rate de IDEMPOTENCY_BODY_MISMATCH, p95 Autorizar,
// outbox.pending) viven como queries KQL en `dashboards-compras.md` y
// se elevarán a alertas Bicep cuando el equipo de operación las
// revise (dashboards-compras.md §6 las lista como pendientes).
// ============================================================================

@description('Nombre corto del proyecto (3-6 caracteres).')
param projectName string

@description('Ambiente (dev | qa | prod).')
param environmentTier string

@description('Código de región usado en nombres.')
param regionCode string

@description('Tags estándar del proyecto.')
param tags object

@description('Email del responsable que recibe las alertas.')
param notificationEmail string

@description('Resource ID del Azure SignalR Service a monitorear.')
param signalrId string

// ============================================================================
// Action Group — destino común de las alertas
// ============================================================================
// 'Global' es la región requerida por Microsoft.Insights/actionGroups; el
// recurso es regional-agnostic. El groupShortName se limita a 12 chars.

resource actionGroup 'Microsoft.Insights/actionGroups@2023-09-01-preview' = {
  name: 'ag-${projectName}-${environmentTier}-${regionCode}-01'
  location: 'Global'
  tags: tags
  properties: {
    enabled: true
    groupShortName: take('${projectName}${environmentTier}', 12)
    emailReceivers: [
      {
        name: 'owner-email'
        emailAddress: notificationEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

// ============================================================================
// Alerta: errores del sistema SignalR
// ============================================================================
// SystemErrors es el contador de fallas internas del servicio (no errores
// de cliente). Cualquier valor > 0 es indicativo de problema de plataforma.
// Sensibilidad alta: 5 en 5 minutos dispara alerta crítica.

resource alertSystemErrors 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-signalr-system-errors-${environmentTier}'
  location: 'global'
  tags: tags
  properties: {
    description: 'Azure SignalR Service está reportando errores del lado servidor. Hub probablemente caído.'
    severity: 1 // Critical
    enabled: true
    scopes: [
      signalrId
    ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    targetResourceType: 'Microsoft.SignalRService/signalR'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'SystemErrorsHigh'
          metricNamespace: 'Microsoft.SignalRService/signalR'
          metricName: 'SystemErrors'
          operator: 'GreaterThan'
          threshold: 5
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    autoMitigate: true
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

// ============================================================================
// Alerta: server load alto
// ============================================================================
// ServerLoad es CPU/memoria del nodo del SignalR Service (porcentaje 0-100).
// Sostenido > 80% indica capacidad insuficiente — escalar (Premium) o
// investigar tráfico anómalo. Severity 2 (no crítico, pero requiere acción).

resource alertServerLoad 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-signalr-server-load-${environmentTier}'
  location: 'global'
  tags: tags
  properties: {
    description: 'Azure SignalR Service con load > 80% sostenido — riesgo de degradación.'
    severity: 2 // Error
    enabled: true
    scopes: [
      signalrId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    targetResourceType: 'Microsoft.SignalRService/signalR'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'ServerLoadHigh'
          metricNamespace: 'Microsoft.SignalRService/signalR'
          metricName: 'ServerLoad'
          operator: 'GreaterThan'
          threshold: 80
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    autoMitigate: true
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

// ============================================================================
// Outputs
// ============================================================================

output actionGroupId string = actionGroup.id
output actionGroupName string = actionGroup.name
