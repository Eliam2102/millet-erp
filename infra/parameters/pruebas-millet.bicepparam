// ============================================================================
// Parámetros del ambiente de PRUEBAS Y VALIDACIÓN en la suscripción de Millet
//
// Suscripción "Millet ERP", tenant de Millet (millet.com.mx). Contratada por
// Millet con su proveedor de Microsoft (correo de Jorge Toache, 15-oct-2026).
// VILO entra como invitado con rol Owner restringido: no puede nombrar Owners
// ni asignar roles de administración de accesos. Por eso aquí no hay grupos
// de Entra y Key Vault Administrator se asigna a usuarios concretos.
//
// Tier `qa` (ASPNETCORE_ENVIRONMENT=Staging, SKUs básicos). Los nombres
// quedan `*-millet-qa-mxc-01` y no chocan con el ambiente `dev` de otra
// suscripción (Key Vault, Storage, ACR y PostgreSQL tienen nombre global).
//
// Password de PostgreSQL: se lee de la variable de entorno PG_ADMIN_PASSWORD
// de quien despliega. En el primer despliegue se genera y se guarda en el
// secreto `postgres-admin-password` del Key Vault; en los siguientes se lee
// de ahí ANTES de desplegar (si cambia, el despliegue resetea el password de
// pgadmin y la app deja de conectar; ver incidente 2026-07-07 en dev).
// ============================================================================

using '../main.bicep'

param environmentTier = 'qa'
param location = 'mexicocentral'
param projectName = 'millet'
param regionCode = 'mxc'

// Sin grupos de Entra en el tenant de Millet (ver encabezado).
param adminsGroupObjectId = ''
param developersGroupObjectId = ''
param adminsGroupDisplayName = ''
// Object IDs (en el tenant de Millet) de las cuentas invitadas de VILO que
// administran el Key Vault. Se completan con `az ad signed-in-user show`.
param keyVaultAdminUserObjectIds = [
  '54b89336-d53c-4477-bab2-13e627b410ed' // eliam@vilostudio.ai (invitado)
]

// Información de gobierno
param ownerEmail = 'eliam@vilostudio.ai'
param costCenter = 'client-millet'

// Presupuesto: la suscripción es de proveedor (CSP) y Cost Management no
// está disponible desde ella (401 al crear el budget, 18-oct-2026). El
// presupuesto y la alerta los confirma Millet con su proveedor (Jorge,
// 15-oct). Los valores quedan por si el proveedor habilita Cost Management.
param deployBudget = false
param budgetAmount = 150
param budgetAlertEmails = [
  'eliam@vilostudio.ai'
  'jtoache@millet.com.mx'
]
// FIJA después del primer despliegue: Azure no permite cambiar startDate.
param budgetStartDate = '2026-10-01'

param postgresAdminUsername = 'pgadmin'
param postgresAdminPassword = readEnvironmentVariable('PG_ADMIN_PASSWORD')

// Empresa inicial: datos fiscales de Millet (3.2 Insumos Fase1.xlsx, hoja
// P0-10, clave 1100; nombre comercial confirmado por Eliam el 20-oct-2026).
// Sin empresa inicial el SuperAdmin (Jorge Toache, Object ID en Key Vault
// `auth-initial-admin-oid`) queda sin asignación y sin permisos, y no puede
// dar de alta la empresa desde el ERP. El domicilio se completa en el ERP.
param empresaInicialRfc = 'MIN120224147'
param empresaInicialRazonSocial = 'M INDUSTRIA SA DE C V'
param empresaInicialRegimenFiscal = '601'
param empresaInicialNombreComercial = 'Millet Industria de Vidrio'

// A+W: el servidor Windows con Hybrid Connection Manager aún no existe.
// Los workers quedan apagados (EmpresaId vacío) hasta conectarlo.
param glassAgentSpnClientId = ''
param awSolicitudesEmpresaId = ''

// FiscalAPI apagado hasta tener el tenant de Millet cargado en su Key Vault.
param integracionesFiscalSdkDisabled = true
