// ============================================================================
// Módulo: Alertas de presupuesto
// Crea un presupuesto mensual con alertas a 50%, 80%, 100% (real)
// y 90% (proyectado). Las alertas llegan por correo a las direcciones indicadas.
// ============================================================================

@description('Nombre del presupuesto.')
param budgetName string

@description('Monto mensual en USD.')
param amount int

@description('Direcciones de correo para alertas.')
param alertEmails array

@description('Fecha de inicio del presupuesto (formato YYYY-MM-DD, primer día del mes).')
param startDate string

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: budgetName
  properties: {
    timePeriod: {
      startDate: '${startDate}T00:00:00Z'
    }
    timeGrain: 'Monthly'
    amount: amount
    category: 'Cost'
    notifications: {
      Actual_50_Percent: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 50
        contactEmails: alertEmails
        thresholdType: 'Actual'
      }
      Actual_80_Percent: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        contactEmails: alertEmails
        thresholdType: 'Actual'
      }
      Actual_100_Percent: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        contactEmails: alertEmails
        thresholdType: 'Actual'
      }
      Forecast_90_Percent: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 90
        contactEmails: alertEmails
        thresholdType: 'Forecasted'
      }
    }
  }
}
