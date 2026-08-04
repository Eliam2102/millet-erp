// ============================================================================
// Parámetros para ambiente de DESARROLLO
//
// postgresAdminPassword se resuelve vía az.getSecret() contra el secreto
// `postgres-admin-password` del Key Vault del ambiente — NO hace falta
// pasarla por CLI. Incidente 2026-07-07: el placeholder anterior
// ('__PASS_BY_CLI__') se desplegó sin el override y reseteó el password
// real de pgadmin, tumbando la app (28P01 en loop). what-if NO detecta
// cambios de password (administratorLoginPassword es write-only).
//
// BOOTSTRAP de un ambiente nuevo (el KV todavía no existe): el getSecret
// no puede resolverse; pasar el override por CLI en el primer deploy:
//   --parameters postgresAdminPassword='<PASSWORD_SEGURA>'
// y después cargar el secreto en KV (ver infra/README.md).
// ============================================================================

using '../main.bicep'

param environmentTier = 'dev'
param location = 'mexicocentral'
param projectName = 'millet'
param regionCode = 'mxc'

// IDs de los grupos de Entra ID (creados manualmente en el portal)
param adminsGroupObjectId = '631cc482-2bf1-45a2-92bd-e739299e7db4'
param developersGroupObjectId = '66fa615b-9938-4ead-8d14-09d486460df3'
param adminsGroupDisplayName = 'MILLET-ERP-Admins'

// Información de gobierno
param ownerEmail = 'eduardo.paredes@tiglass.net'
param costCenter = 'client-millet'

// Presupuesto y alertas. Ajustar el monto según comodidad.
param budgetAmount = 150
param budgetAlertEmails = [
  'eduardo.paredes@tiglass.net'
]
// FIJA (no tocar): Azure NO permite actualizar startDate de un budget
// existente ("Start date of budgets cannot be updated"). El default de
// main.bicep (mes corriente vía utcNow) rompía el deploy en cualquier mes
// distinto al de creación — run 28751516542. Debe coincidir con el budget
// ya desplegado en dev (creado 2026-05).
param budgetStartDate = '2026-05-01'

// PostgreSQL: la password se resuelve desde Key Vault al desplegar (ver
// header). Requiere enabledForTemplateDeployment en el vault y que quien
// despliega tenga Microsoft.KeyVault/vaults/deploy/action (Owner/Contributor
// del RG lo incluyen).
param postgresAdminUsername = 'pgadmin'
param postgresAdminPassword = az.getSecret(
  'd820f0b5-f33a-40b2-be1b-057c650af28e',
  'rg-millet-dev-mxc-01',
  'kv-millet-dev-mxc-01',
  'postgres-admin-password')

// === Bootstrap del SuperAdmin (ADR-0007) ===
// El oid del SuperAdmin, JWT signing key y datos de Entra ID viven en KV
// — operador los setea manualmente (ver infra/README.md sección
// "Auth secrets en Key Vault"). Aquí van los datos NO sensibles de la
// empresa inicial. Si Rfc queda vacío, el bootstrap solo crea Usuario+Rol
// sin asignación a empresa (operador la crea después).
param empresaInicialRfc = 'TIG890101AAA'
param empresaInicialRazonSocial = 'Tiglass S.A. de C.V.'
param empresaInicialRegimenFiscal = '601'
param empresaInicialNombreComercial = 'Tiglass'

// === Integración A+W (módulo Millet.Integraciones.Aw) ===
// El appid (client ID) del service principal `millet-glass-agent-spn-dev`
// creado en Entra ID el 2026-05-15 vía `az ad app create`. Valor PÚBLICO
// (no es secret; el client secret correspondiente vive en KV como
// `glass-agent-spn-client-secret`). En prod tendrá su propio appid.
param glassAgentSpnClientId = '990f1714-c0df-4dd8-aef8-429bc50b2f90'

// Ingesta de pedidos A+W → Facturación (ADR-0048, flujo 2): empresa que
// procesa el AwSolicitudesWorker en dev = la empresa seed de Tiglass.
// NO es secreto (GUID interno). En prod se setea al go-live (M3).
param awSolicitudesEmpresaId = '00000003-0000-0000-0000-000000000001'

// SDK FiscalAPI encendido en dev (FAC-DET): TenantKey SANDBOX en el KV dev
// (secreto operator-managed `fiscalapi-tenant-key`); la ApiKey sk_test y la
// BaseUrl https://test.fiscalapi.com van por empresa en ConfiguracionPac
// (ventana Integraciones Fiscal). Prod queda con el default true hasta
// tener su propio tenant productivo en su propio KV.
param integracionesFiscalSdkDisabled = false
