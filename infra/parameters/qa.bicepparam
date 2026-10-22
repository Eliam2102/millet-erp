// ============================================================================
// Parámetros para ambiente de PRUEBAS (qa) — canal A+W real LISTO pero APAGADO
//
// Copia de dev.bicepparam. Todos los flujos A+W arrancan en OFF; se encienden
// cambiando solo estos valores (ver bloque "Integración A+W" al final).
//
// PENDIENTES (marcados '__PEDIR_A_ELIAM__'; el deploy debe fallar fuerte si
// quedan sin reemplazar):
//   - id de la suscripción en az.getSecret() (postgres-admin-password)
//   - adminsGroupObjectId / developersGroupObjectId (grupos Entra del ambiente)
//   - ownerEmail y budgetAlertEmails
//   - glassAgentSpnClientId (App Registration del ambiente)
//
// Secretos operator-managed en el KV qa (kv-millet-qa-mxc-01), ver
// infra/modules/keyvault-secrets.bicep: aw-integracion-connection-string,
// aw-clientes-connection-string, aw-productos-connection-string,
// aw-cambios-connection-string, aw-sql-connection-string,
// aw-sql-server-certificate (PEM público de SER-DATA).
// Todas las connection strings: Server=aw-sql-onprem,1433;...;Encrypt=True;TrustServerCertificate=False
//
// BOOTSTRAP: igual que dev (ver dev.bicepparam y infra/README.md).
// ============================================================================

using '../main.bicep'

param environmentTier = 'qa'
param location = 'mexicocentral'
param projectName = 'millet'
param regionCode = 'mxc'

param adminsGroupObjectId = '__PEDIR_A_ELIAM__'
param developersGroupObjectId = '__PEDIR_A_ELIAM__'
param adminsGroupDisplayName = 'MILLET-ERP-Admins'

param ownerEmail = '__PEDIR_A_ELIAM__'
param costCenter = 'client-millet'

param budgetAmount = 150
param budgetAlertEmails = [
  '__PEDIR_A_ELIAM__'
]
// Fija: Azure no permite cambiar startDate de un budget existente.
param budgetStartDate = '2026-10-01'

param postgresAdminUsername = 'pgadmin'
param postgresAdminPassword = az.getSecret(
  '__PEDIR_A_ELIAM__',
  'rg-millet-qa-mxc-01',
  'kv-millet-qa-mxc-01',
  'postgres-admin-password')

param empresaInicialRfc = 'TIG890101AAA'
param empresaInicialRazonSocial = 'Tiglass S.A. de C.V.'
param empresaInicialRegimenFiscal = '601'
param empresaInicialNombreComercial = 'Tiglass'

// === Integración A+W: canal real listo, TODO APAGADO ===
param glassAgentSpnClientId = '__PEDIR_A_ELIAM__'

// Vacío = worker de ingesta de pedidos apagado (candado #2 del runbook).
param awSolicitudesEmpresaId = ''

// SQL Server de A+W: la HC usa el host sintético aw-sql-onprem (default de
// hybridConnections.bicep); awOnPremHostname solo apunta el drop service
// (SER-DATA, default de main.bicep).
param awClientesOrigen = 'Simulado'
// Canal demo (A+W-QA-3): lectura/aplicación encendidas para que Millet pruebe con el origen Demo.
// Con Origen='Simulado' el canal real no lee nada; el Demo responde AW_DEMO_NO_CONFIGURADA
// hasta que exista el secreto aw-origen-pg-connection-string.
param awClientesLecturaHabilitada = true
param awClientesAplicacionHabilitada = true
param awClientesProgramacionHabilitada = false
param awProductosOrigenHabilitado = true
param awOrigenDemoPermitido = true
param awProductosOrigen = 'Simulado'
param awCambiosHabilitado = false
param awDocumentSyncHabilitado = false
param awLateReconciliationHabilitado = false

// SDK FiscalAPI apagado (NoOp) hasta tener tenant sandbox en el KV qa.
param integracionesFiscalSdkDisabled = true
