// ============================================================================
// Parámetros para ambiente de PRODUCCIÓN
// 
// ESTE ARCHIVO ES UN ESQUELETO. NO desplegar aún.
// Antes de desplegar producción se deben confirmar:
//   1. Suscripción de Azure del cliente activa (vía partner CSP o directa)
//   2. Object IDs de los grupos en el tenant del CLIENTE (no el tuyo)
//   3. Aprobación formal del costo mensual estimado (700-900 USD)
//   4. Verificar disponibilidad de servicios y zonas en mexicocentral
//
// ============================================================================

using '../main.bicep'

param environmentTier = 'prod'
param location = 'mexicocentral'
param projectName = 'millet'
param regionCode = 'mxc'

// IDs de los grupos del tenant del CLIENTE - PENDIENTE
param adminsGroupObjectId = '__PENDIENTE__'
param developersGroupObjectId = '__PENDIENTE__'
param adminsGroupDisplayName = 'MILLET-ERP-Admins-Prod'

// Información de gobierno
param ownerEmail = '__PENDIENTE__'
param costCenter = '__PENDIENTE__'

// Presupuesto productivo: rango aprobado 700-900 USD/mes
param budgetAmount = 900
param budgetAlertEmails = [
  '__PENDIENTE__'
]

param postgresAdminUsername = 'pgadmin'
// Mismo patrón getSecret() que dev.bicepparam. La suscripción del CLIENTE
// está PENDIENTE: mientras no se complete, cualquier intento de deploy
// falla en la resolución de parámetros — fail-loud deliberado. El
// placeholder anterior ('__PASS_BY_CLI__') pasaba la validación y un
// deploy sin override reseteaba el password real de pgadmin (incidente
// dev 2026-07-07). Bootstrap de prod: primer deploy con
// `--parameters postgresAdminPassword=...` (el KV aún no existe), cargar
// el secreto `postgres-admin-password` en el KV y completar esta línea.
param postgresAdminPassword = az.getSecret(
  '__PENDIENTE__',
  'rg-millet-prod-mxc-01',
  'kv-millet-prod-mxc-01',
  'postgres-admin-password')
