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
// aw-cambios-connection-string, almacenes-app-connection-string, aw-sql-connection-string,
// aw-sql-server-certificate (PEM público de SER-DATA),
// mail-entra-facturas-client-secret (buzón facturas@millet.com.mx; vence ~9-feb-2027).
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

// M-045b: empresa inicial (id fijo del bootstrap) = worker de ingesta encendido contra MILLET_INTEGRACION_DEV
// (secreto operator-managed aw-integracion-connection-string). Vacío lo apaga (candado #2 del runbook).
param awSolicitudesEmpresaId = '00000003-0000-0000-0000-000000000001'
// M-184 / M-454: lector directo de notas de crédito de A+W (millet_erp_lector_aw, secreto aw-clientes-connection-string).
param awNotasCreditoLecturaHabilitada = true

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

// SDK FiscalAPI encendido: timbrado contra el sandbox de FiscalAPI (Oleada 1).
// Requiere el secreto `fiscalapi-tenant-key` (tenant sandbox) en el KV qa y la
// ApiKey sk_test capturada en Integraciones Fiscal (BaseUrl https://test.fiscalapi.com).
param integracionesFiscalSdkDisabled = false

// M-185: el monitor del cierre del día consulta el estatus SAT real vía
// FiscalAPI (sandbox en este ambiente). Requiere la consulta habilitada en la
// cuenta de FiscalAPI; un error se cuenta «con error» y no cambia el estatus.
param consultaEstatusSatProveedor = 'FiscalApi'

// === Ajustes de la app que estaban puestos a mano en el portal (12-nov-2026) ===
// Valores copiados del App Service app-millet-qa-mxc-01. Un deploy de infraestructura reemplaza todos
// los ajustes de la app, así que viven aquí. Secretos solo como referencia @Microsoft.KeyVault(...).
param appSettingsExtra = {
  Almacen__Avisos__Finanzas__0: 'gpantoja@millet.com.mx'
  Almacen__Avisos__JefeAlmacen__0: 'eesparza@millet.com.mx'
  Auth__EntraId__ClientSecret: '@Microsoft.KeyVault(VaultName=kv-millet-qa-mxc-01;SecretName=auth-entra-api-client-secret)'
  Auth__EntraId__SenderEmail: 'erpmillet@millet.com.mx'
  Auth__ServicePrincipalsJson: '[{"Name":"Reabastecimiento Automatico","AppId":"a0000000-0000-4000-8000-0000000000a1","ObjectId":"b0000000-0000-4000-8000-0000000000b1","EmpresaRfc":"MIN120224147","Permisos":["compras.requisiciones.crear","compras.requisiciones.seleccionar-requisitante"],"Notes":"SP interno: creador de RQ de reorden y de la app de Almacenes. Sin credenciales."}]'
  Correo__ModoPruebas__Activo: 'true'
  Correo__ModoPruebas__BuzonesPrueba__0: 'operaciones@vilostudio.ai'
  Correo__ModoPruebas__BuzonesPrueba__1: 'geovany@vilostudio.ai'
  Correo__ModoPruebas__BuzonesPrueba__2: 'jorge.toache@millet.com.mx'
  Correo__ModoPruebas__DominiosPermitidos__0: 'vilostudio.ai'
  Correo__Proveedor: 'Graph'
  CuentasPorPagar__AvisosRevision__Compras__0: 'compras@millet.com.mx'
  CuentasPorPagar__AvisosRevision__ContadorGeneral__0: 'contadorgeneral@millet.com.mx'
  CuentasPorPagar__AvisosRevision__Proveedor: 'Graph'
  CuentasPorPagar__Mailbox__ClientId: '298417fb-31e9-4489-b8b8-364f74f1baea'
  CuentasPorPagar__Mailbox__ClientSecret: '@Microsoft.KeyVault(VaultName=kv-millet-qa-mxc-01;SecretName=mail-entra-facturas-client-secret)'
  CuentasPorPagar__Mailbox__FailedFolderName: 'Con error'
  CuentasPorPagar__Mailbox__MailboxUpn: 'facturas@millet.com.mx'
  CuentasPorPagar__Mailbox__ProcessedFolderName: 'Procesadas'
  CuentasPorPagar__Mailbox__TenantId: '@Microsoft.KeyVault(VaultName=kv-millet-qa-mxc-01;SecretName=auth-entra-tenant-id)'
  CuentasPorPagar__PortalProveedores__Proveedor: 'Graph'
  // RFC receptor del CFDI del proveedor → empresa. MIN120224147 es el RFC vigente en qa
  // (el mismo de Auth__ServicePrincipalsJson). `empresaInicialRfc` quedó solo como bootstrap.
  CuentasPorPagar__Workers__RfcsReceptores__MIN120224147: '00000003-0000-0000-0000-000000000001'
  Facturacion__Avisos__Contingencias__Fiscal__0: 'mcolin@millet.com.mx'
  Facturacion__Workers__AwSolicitudes__Disabled: 'true'
  Integraciones__AlmCompra__Habilitado: 'true'
  Integraciones__AlmCompra__Transporte: 'Sql'
  Integraciones__AlmMensaje__CargaInicialHabilitada: 'true'
  Integraciones__AlmMensaje__Habilitado: 'false'
  Integraciones__AlmMensaje__Origen: 'Sql'
  IntegracionesAw__Clientes__UnificacionAutomaticaHabilitada: 'true'
  IntegracionesAw__Exportacion__BatchSize: '200'
  IntegracionesAw__Exportacion__ContratoI134Firmado: 'true'
  IntegracionesAw__Exportacion__EmpresaId: '00000003-0000-0000-0000-000000000001'
  IntegracionesAw__Exportacion__EntradaHabilitada: 'true'
  IntegracionesAw__Exportacion__EntregaDesde: '2026-11-01'
  IntegracionesAw__PiezasPt__ContratoI134Firmado: 'true'
  IntegracionesAw__PiezasPt__Desde: '1-nov-2026'
  IntegracionesAw__PiezasPt__EmpresaId: '00000003-0000-0000-0000-000000000001'
  IntegracionesAw__PiezasPt__EntradaHabilitada: 'true'
  IntegracionesAw__Retorno__Habilitado: 'false'
  Integraciones__CostoPromedio__Habilitado: 'true'
  Integraciones__CostoPromedio__Transporte: 'Sql'
  // Umbral general de ENSAYO de Compras (ComprasTestSeedHostedService) para que la RQ no quede en
  // SinPolitica mientras Millet no entrega montos (HMONTOS). No corre en Production.
  Seed__UmbralEnsayo__Habilitado: 'true'
}
