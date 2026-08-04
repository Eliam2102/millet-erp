// ============================================================================
// Módulo: Azure SignalR Service
// Real-time push del backend al navegador (ADR-0001: cada módulo expone
// uno o más Hubs; los Hubs aceptan el JWT del API). Modo Default (no
// Serverless): la app .NET ejecuta la lógica del Hub y el SignalR Service
// hace de backplane gestionado.
//
// SKU:
//   - dev: Free_F1 (20 conexiones concurrentes, 20K mensajes/día, sin SLA)
//   - prod: Standard_S1 (1000 conexiones por unit, 99.9% SLA, ~$50/mes)
//
// Region: SignalR Service NO está disponible en mexicocentral. Por eso este
// módulo recibe su propio param `location` (no usa el global). main.bicep
// pasa `signalrLocation` (default southcentralus, ~50 ms desde MX). Como
// SignalR es un relay sin almacenamiento persistente de datos del cliente,
// esta excepción no rompe data residency del ERP. Ver ADR-0001 (notas de
// implementación) para el racional completo.
// ============================================================================

@description('Región del recurso. mexicocentral NO está soportado; usar una región cercana (southcentralus por default).')
param location string
param projectName string
param environmentTier string
param regionCode string
param tags object

@description('SKU del SignalR Service.')
@allowed([
  'Free_F1'
  'Standard_S1'
  'Premium_P1'
])
param skuName string = 'Free_F1'

@description('Capacidad (units). 1 unit = 1000 conexiones concurrentes en Standard. Free siempre 1.')
param capacity int = 1

resource signalr 'Microsoft.SignalRService/signalR@2024-03-01' = {
  name: 'signalr-${projectName}-${environmentTier}-${regionCode}-01'
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: split(skuName, '_')[0] // "Free", "Standard" o "Premium"
    capacity: capacity
  }
  kind: 'SignalR'
  properties: {
    features: [
      {
        flag: 'ServiceMode'
        value: 'Default'
      }
      {
        flag: 'EnableConnectivityLogs'
        value: 'true'
      }
    ]
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
    tls: {
      clientCertEnabled: false
    }
  }
}

// ============================================================================
// Outputs
// ============================================================================

output name string = signalr.name
output id string = signalr.id
output hostName string = signalr.properties.hostName

@secure()
output primaryConnectionString string = signalr.listKeys().primaryConnectionString
