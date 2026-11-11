# Manual técnico de operación: FiscalAPI, CSD, series y Banxico

**Tarea:** M-411 · **Estado:** borrador para revisión · **Base:** `origin/develop` @ `e7f16b0f5` (11-nov-2026)
**Audiencia:** Sistemas / TI y Fiscal de Millet, y quien opera el ERP en Azure.

## 0. Cómo leer este manual

- Cada afirmación lleva entre paréntesis el archivo del repo donde se comprobó. Las rutas son relativas a la raíz del repositorio.
- Lo que no se pudo confirmar en el código o en la configuración del repo va marcado **«por confirmar»** y se concentra en la sección 8.
- No hay secretos, credenciales ni datos de Millet. Donde hace falta un secreto se nombra el lugar donde vive (Key Vault, ConfiguracionPac), nunca su valor.
- Complementa, no reemplaza, los runbooks por módulo: `docs/modulos/facturacion/08-operacion-y-runbook.md` y `docs/modulos/administracion/08-operacion-y-runbook.md`. La sección 9 lista dónde esos runbooks ya no coinciden con el código.

## 1. Mapa de piezas

| Pieza | Qué hace | Dónde está |
|---|---|---|
| **FiscalAPI** | Único PAC. Timbra, cancela y consulta estatus SAT | `docs/decisiones/0038-fiscalapi-pac-unico.md`; `backend/src/Integraciones.Fiscal/Domain/ProveedorPac.cs` |
| **ConfiguracionPac** | Una fila por empresa con la URL, la ApiKey cifrada, el CSD cifrado y las identidades de prueba | `backend/src/Integraciones.Fiscal/Domain/ConfiguracionPac.cs` |
| **Historial de CSD** | Alta, sustitución y revocación de certificados, con quién y cuándo | `backend/src/Integraciones.Fiscal/Domain/Certificados/CertificadoCsd.cs` |
| **Adaptador de timbrado** | Arma el CFDI, lo manda al PAC y clasifica la respuesta | `backend/src/Integraciones.Fiscal/Infrastructure/SdkAdapter/FiscalApiTimbradoAdapter.cs` |
| **Series y folios** | Configuración de series por tipo de documento y sucursal; reserva de folio | `backend/src/Administracion/Domain/Serie.cs`; `backend/src/Compartido/Application/Administracion/Series/` |
| **Tipo de cambio** | Catálogo `compartido.tipos_cambio`, alimentado por el job de Banxico o por captura manual | `backend/src/Compartido/Infrastructure/Workers/TipoCambioBanxicoWorker.cs`; `backend/src/Catalogos/Domain/EscaleraTipoCambio.cs` |

Pantallas: la configuración del PAC está en `/admin/integraciones/fiscal` (`frontend/src/routes/_app/admin/integraciones/fiscal/index.tsx`), las series en `/admin/series` (`frontend/src/routes/_app/admin/series/index.tsx`) y los tipos de cambio en `/admin/catalogos/monedas/$id` (`frontend/src/routes/_app/admin/catalogos/monedas/$id.tsx`).

## 2. FiscalAPI: llaves, ambientes y cambio de llaves

### 2.1 Qué llaves existen y dónde viven

| Llave | Alcance | Dónde vive | Cómo se cambia |
|---|---|---|---|
| **Tenant key** de FiscalAPI | Una por ambiente (todo Millet bajo un solo contrato) | Secreto `fiscalapi-tenant-key` del Key Vault, expuesto al App Service como `IntegracionesFiscal__Sdk__TenantKey` (`infra/modules/appservice.bicep`, líneas del bloque `fiscalapi-tenant-key`; `backend/src/Integraciones.Fiscal/Infrastructure/SdkAdapter/FiscalApiSdkAdapterOptions.cs`) | En Key Vault; el App Service la lee por referencia. El secreto se crea a mano en cada ambiente (`infra/modules/appservice.bicep`, comentario con `az keyvault secret set`). |
| **ApiKey** por empresa | Una por empresa | Tabla de `ConfiguracionPac`, cifrada (`ApiKeyCifrado`) con DataProtection (`backend/src/Integraciones.Fiscal/Domain/ConfiguracionPac.cs`; `docs/decisiones/0037-cifrado-de-secretos-operativos.md`) | Desde la pantalla de Configuración del PAC (sección 2.3) |
| **CSD** (.cer, .key y contraseña) | Una terna por empresa | Misma tabla, cifrado igual (`CsdCertificadoCifrado`, `CsdLlavePrivadaCifrada`, `CsdPasswordCifrado`) | Sección 3 |
| **DEK de DataProtection** | Una por ambiente | Key Vault, llave RSA `dataprotection-master-key`; el anillo de llaves está en el contenedor `dataprotection-keys` (`docs/decisiones/0037-cifrado-de-secretos-operativos.md`, sección Decisión y Notas de implementación) | Sección 2.4 |

El SDK solo corre si `IntegracionesFiscal__Sdk__Disabled=false` (`infra/modules/appservice.bicep`; `docs/modulos/facturacion/08-operacion-y-runbook.md`, «Registro por ambiente»). Un ambiente sin credenciales falla visible: no emite UUID falsos (mismo runbook).

### 2.2 Ambientes: sandbox y producción

- La URL base solo puede ser `https://test.fiscalapi.com` o `https://live.fiscalapi.com`; cualquier otra se rechaza con `CONFIG_PAC_BASE_URL_INVALIDA` (`ConfiguracionPac.ValidarBaseUrl`, `backend/src/Integraciones.Fiscal/Domain/ConfiguracionPac.cs`).
- Las **identidades de prueba** (emisor y receptor sustitutos) solo se aceptan con la URL de pruebas; al pasar a la URL de producción se limpian. Así no se puede timbrar en producción con un RFC de prueba (`ConfiguracionPac.ConfigurarIdentidadesSandbox`, mismo archivo; `docs/modulos/facturacion/08-operacion-y-runbook.md`, «Onboarding del emisor»).
- **El CSD no se limpia al cambiar de ambiente.** Al pasar a producción hay que capturar el CSD real; un CSD de prueba en producción falla en el PAC (comentario de `ConfiguracionPac.ConfigurarCsd`, mismo archivo).
- El adaptador de estatus SAT se elige por ambiente en bicep (`Facturacion__ConsultaEstatusSat__Proveedor`); **no se cambia en el portal de Azure**, porque el siguiente despliegue lo borra (`docs/modulos/facturacion/08-operacion-y-runbook.md`, «Estatus SAT de los CFDI emitidos»).

### 2.3 Cambiar la ApiKey de una empresa (rotación)

Quién: usuario con `integraciones.fiscal.administrar` (`backend/src/Identidad/Domain/PermisosCanonicos.cs`; `backend/src/Api/Endpoints/IntegracionesFiscal/IntegracionesFiscalEndpoints.cs`, `PUT /api/v1/integraciones/fiscal/configuracion/{empresaId}/{proveedor}`).

1. Obtener la llave nueva en el panel de FiscalAPI. No se pega en chats, correos ni tickets.
2. Entrar a Administración › Integraciones › Fiscal › Configuración del PAC (`frontend/src/routes/_app/admin/integraciones/fiscal/index.tsx`).
3. Capturar la ApiKey nueva y guardar. Si se deja vacía, la llave no cambia (`GuardarConfiguracionPacHandler`, `backend/src/Integraciones.Fiscal/Application/Configuracion/GuardarConfiguracionPac/GuardarConfiguracionPacHandler.cs`).
4. El servidor **prueba la conexión con la llave nueva antes de guardar**. Si la prueba falla, no se guarda nada, queda la configuración anterior y se abre una incidencia; el mensaje empieza con «Pruebe la conexión antes de activar» (mismo handler, código `CONFIG_PAC_CONEXION_RECHAZADA`).
5. Si la llave es la misma que la guardada, no se re-cifra ni cambia la fecha de rotación (`ConfiguracionPac.RotarApiKey` y el hash de detección, `backend/src/Integraciones.Fiscal/Domain/ConfiguracionPac.cs`).
6. El resolver guarda la configuración en memoria **hasta 60 segundos**; tras guardar puede tardar ese tiempo en notarse en todas las instancias (`backend/src/Integraciones.Fiscal/Infrastructure/Cliente/ConfiguracionPacResolver.cs`, `CacheTtl`; que guardar invalide la caché de todas las instancias: **por confirmar**).
7. Verificación: ejecutar «Probar conexión» (`POST .../configuracion/{empresaId}/{proveedor}/test`, mismo archivo de endpoints) y revisar «Última prueba» (`GET .../pruebas/ultima`).

**Desactivar sin depender del PAC:** una configuración inactiva no hace la prueba de conexión, así que se puede desactivar aunque el PAC esté caído o la llave comprometida (comentario en `GuardarConfiguracionPacHandler`, paso «P01»).

### 2.4 Cambiar o recuperar la llave maestra (DEK)

Fuente única: `docs/decisiones/0037-cifrado-de-secretos-operativos.md`, secciones «Consecuencias» y «Runbook recovery».

- **Rotar la versión de la llave** en Key Vault: el anillo soporta varias llaves activas durante la transición; las columnas de Postgres no se re-cifran de golpe.
- **Si se compromete la llave:** rotar versión nueva y marcar la vieja como `Disabled`. Lo cifrado con la vieja se vuelve ilegible, así que **hay que volver a capturar todas las credenciales** (ApiKey y CSD de cada empresa).
- **Si se borra el blob del anillo** (`millet-erp.xml`): restaurarlo con la restauración de blobs del contenedor (el soft-delete debe estar activo). Mientras tanto los secretos son ilegibles y los workers quedan en espera.
- **Si se borra la llave del Key Vault:** se recupera con `az keyvault key recover --name dataprotection-master-key` dentro de los 90 días de soft-delete.
- **Si se pierde el Key Vault completo,** todo lo cifrado es irrecuperable (ADR-0037).

## 3. CSD: captura, renovación, revocación y avisos

### 3.1 Qué valida el sistema al capturar un CSD

El guardado valida la terna **antes** de cifrarla y persistirla (`backend/src/Integraciones.Fiscal/Infrastructure/Cifrado/CsdValidador.cs`). En orden:

| Validación | Código si falla |
|---|---|
| El .cer y el .key vienen en base64 válido | `CONFIG_PAC_CSD_BASE64_INVALIDO` |
| El .cer es un certificado X.509 legible | `CONFIG_PAC_CSD_CERTIFICADO_INVALIDO` |
| Es un CSD y no una e.firma (el sujeto trae unidad organizativa) | `CONFIG_PAC_CSD_ES_FIEL` |
| La contraseña abre la llave privada | `CONFIG_PAC_CSD_PASSWORD_INCORRECTA` |
| La llave corresponde al certificado | `CONFIG_PAC_CSD_NO_CORRESPONDEN` |
| El certificado está vigente a la fecha de captura | `CONFIG_PAC_CSD_VENCIDO` |
| El certificado **no** fue revocado antes en el historial de la empresa | `CONFIG_PAC_CSD_REVOCADO` (`backend/src/Integraciones.Fiscal/Application/Certificados/HistorialCsd.cs`) |

Las tres piezas (.cer, .key y contraseña) viajan juntas; si falta una, `CONFIG_PAC_CSD_INCOMPLETO` (`ConfiguracionPac.ConfigurarCsd`).

### 3.2 Renovar el CSD (antes o el día del vencimiento)

Quién: `integraciones.fiscal.administrar`. Tiempo en que debe hacerse: antes del vencimiento; **desde el vencimiento el sistema ya no timbra** (sección 3.4).

1. Tramitar el CSD nuevo con el SAT (fuera del ERP). Conservar el acuse como evidencia.
2. Entrar a Administración › Integraciones › Fiscal › Configuración del PAC › sección «CSD del emisor» (`docs/modulos/facturacion/08-operacion-y-runbook.md`, «Onboarding del emisor»).
3. Capturar .cer, .key y contraseña del CSD nuevo y guardar. Debe pasar las validaciones de la sección 3.1.
4. Efecto: el CSD nuevo queda **Vigente** en el historial y el anterior queda **Sustituido**, con fecha y usuario (`HistorialCsd.RegistrarAsync`, `backend/src/Integraciones.Fiscal/Application/Certificados/HistorialCsd.cs`; `CertificadoCsd.Sustituir`).
5. Adjuntar la evidencia (acuse del SAT o solicitud de renovación) al certificado con el mecanismo genérico de adjuntos, propietario `certificado_csd` (comentario de clase en `backend/src/Integraciones.Fiscal/Domain/Certificados/CertificadoCsd.cs`; la pantalla exacta para adjuntar: **por confirmar**).
6. Verificar: consultar el historial (`GET .../configuracion/{empresaId}/{proveedor}/certificados`) y timbrar un comprobante de prueba. Si el timbrado falla, ver la sección 5.

Si se captura la **misma terna** que ya estaba guardada, no pasa nada (idempotente por hash; `ConfiguracionPac.ConfigurarCsd`).

### 3.3 Revocar el CSD (comprometido o revocado por el SAT)

1. Quién: `integraciones.fiscal.administrar`. Operación: `POST /api/v1/integraciones/fiscal/configuracion/{empresaId}/{proveedor}/csd/revocar` con un **motivo obligatorio** (`backend/src/Api/Endpoints/IntegracionesFiscal/IntegracionesFiscalEndpoints.cs`; `backend/src/Integraciones.Fiscal/Application/Certificados/RevocarCsdCommand.cs`; el botón de pantalla: **por confirmar**).
2. Efecto inmediato: el certificado queda **Revocado** en el historial, se invalida la caché del resolver y el adaptador deja de timbrar con él aunque no haya vencido (`RevocarCsdCommand.cs`; `CertificadoCsd.Revocar`).
3. Capturar el CSD nuevo (sección 3.2). Al capturar uno distinto se limpia la marca de revocado de la configuración (`ConfiguracionPac.ConfigurarCsd`).
4. Un certificado revocado **no se puede volver a capturar** (`CONFIG_PAC_CSD_REVOCADO`).

### 3.4 Qué pasa si el CSD vence o está revocado

Antes de llamar al PAC, el adaptador rechaza el timbrado con `CSD_REVOCADO` o `CSD_VENCIDO` y un mensaje que indica capturar un CSD vigente y reintentar (`FiscalApiTimbradoAdapter.BloqueoCsd`, `backend/src/Integraciones.Fiscal/Infrastructure/SdkAdapter/FiscalApiTimbradoAdapter.cs`). Sin CSD capturado, `CSD_NO_CONFIGURADO`. En los tres casos **no se consume timbre** (rechazo previo al PAC) y el comprobante queda en `TimbradoFallido`; tras capturar el CSD correcto se usa «Reintentar timbrado» (sección 5.2).

Estado derivado del CSD: Vigente, Próximo a vencer (últimos 30 días), Vencido o Revocado (`ConfiguracionPac.ObtenerEstadoCsd`). Cualquier usuario autenticado puede consultar el estado (sin datos sensibles) en `GET /api/v1/integraciones/fiscal/csd/estado`, que alimenta el aviso dentro del ERP (`IntegracionesFiscalEndpoints.cs`, `EstadoCsdEmpresa`).

### 3.5 Avisos de vencimiento

- Un worker corre cada 24 horas por omisión y avisa a **30, 15 y 5 días** antes y **el día del vencimiento**, una sola vez por certificado y umbral. Si se cruzan varios umbrales (por ejemplo, el worker estuvo apagado), avisa solo el más urgente (`backend/src/Integraciones.Fiscal/Domain/Certificados/AlertaCsd.cs`, `PoliticaAlertasCsd`; `backend/src/Integraciones.Fiscal/Infrastructure/Workers/AlertasCsdWorker.cs`).
- Destinatarios por omisión: roles `contador-general` y `super-admin` (supuesto declarado en el código; `backend/src/Integraciones.Fiscal/Application/Certificados/EvaluarAlertasCsdCommand.cs`, `AlertasCsdOptions`).
- **Hoy el aviso solo se escribe en el log**; el correo llega «con el servicio de avisos» (`backend/src/Integraciones.Fiscal/Infrastructure/Avisos/LogAvisoCsdAdapter.cs`; registro en `backend/src/Integraciones.Fiscal/Infrastructure/DependencyInjection.cs`). No hay otro adaptador de correo registrado para este aviso en `backend/src/Api/Program.cs`. **Consecuencia operativa:** hay que revisar el estado del CSD en el ERP o en el log, o llevar un recordatorio propio de la fecha de vencimiento, hasta que el correo se conecte.
- Se apaga con `IntegracionesFiscal:AlertasCsd:Disabled` (`EvaluarAlertasCsdCommand.cs`).

## 4. Series y folios

### 4.1 Cómo funcionan

- Una **serie** se define por empresa, tipo de documento y, opcionalmente, sucursal. Tipos: Orden de compra, CFDI, Nota de crédito, Póliza, Factura de anticipo, Venta directa y Exportación (`backend/src/Administracion/Domain/TipoDocumentoSerie.cs`).
- El folio interno se forma con prefijo, sufijo opcional y consecutivo de 6 dígitos; las series con reinicio anual o mensual agregan el periodo (`backend/src/Administracion/Domain/Serie.cs`, comentario de clase).
- **Las series fiscales** (CFDI, nota de crédito y los demás tipos que `Serie.EsFiscal` cubre) **no reinician**: mantienen continuidad (`SERIE_FISCAL_SIN_REINICIO`; `Serie.cs` y `backend/src/Compartido/Application/Administracion/Series/ActualizarSerieCommand.cs`).
- Solo puede haber **una serie fiscal activa** por empresa, sucursal y tipo (`SERIE_ACTIVA_DUPLICADA`, `backend/src/Compartido/Application/Administracion/Series/CrearSerieCommand.cs`).
- La **venta directa** solo existe por sucursal y no cae a una serie global (`Serie.RequiereSucursal`, `SERIE_VD_REQUIERE_SUCURSAL`).
- Qué serie usa cada documento al emitir: factura de venta → CFDI, venta directa manual → Venta directa, exportación con CCE → Exportación; anticipos → Factura de anticipo; notas de crédito → Nota de crédito; carta porte → CFDI (`backend/src/Facturacion/Application/Facturas/EmitirFacturaVenta/EmitirFacturaVentaHandler.cs`, selección de tipo; `backend/src/Facturacion/Application/Anticipos/EmitirFacturaAnticipo/EmitirFacturaAnticipoHandler.cs`; `backend/src/Facturacion/Application/CartaPorte/EmitirCartaPorte/EmitirCartaPorteHandler.cs`).
- **El folio que va en el XML lo calcula FiscalAPI** (consecutivo por RFC emisor, no por serie); al PAC viaja solo la serie. El folio interno del ERP es la referencia en bandejas y reportes, y el del PAC se guarda aparte; tendrá huecos dentro de una serie y es lo esperado (`docs/modulos/facturacion/08-operacion-y-runbook.md`, «Folio del CFDI»; **por confirmar** contra el comportamiento actual del SDK).
- Permiso para crear, editar y desactivar series: `admin.series.gestionar` (`backend/src/Identidad/Domain/PermisosCanonicos.cs`). Pantalla: `/admin/series`.

### 4.2 Crear una serie nueva (alta de sucursal o de tipo de documento)

1. Administración › Series › «Nueva serie».
2. Capturar empresa, sucursal, tipo, prefijo, sufijo opcional y **folio inicial** (positivo; se usa para continuar la numeración del sistema anterior) (`Serie.cs`, `SERIE_FOLIO_INICIAL_INVALIDO`).
3. Si la serie es fiscal, el reinicio debe ser «sin reinicio».
4. Guardar. Si ya existe una serie fiscal activa para esa sucursal y tipo, el sistema pide desactivarla antes (`SERIE_ACTIVA_DUPLICADA`).

### 4.3 Cambiar una serie en uso (reemplazo)

- Una serie fiscal **que ya reservó folios no se edita** en prefijo, sufijo ni reinicio: el sistema responde `SERIE_USADA_INMUTABLE` y pide **desactivarla y crear otra** para no alterar su historia (`ActualizarSerieCommand.cs`).
- Pasos: (1) desactivar la serie vigente; (2) crear la serie nueva con el folio inicial que corresponda; (3) emitir un comprobante de prueba y verificar el folio. Entre el paso 1 y el 2 **no se puede emitir** de ese tipo en esa sucursal (`SERIE_NO_CONFIGURADA`), así que se hacen seguidos y fuera de horas de facturación.
- Si una serie fiscal heredada tenía reinicio, no se reserva folio hasta que Fiscal concilie su continuidad y configure un reemplazo (`SERIE_FISCAL_CONTINUIDAD_PENDIENTE`, `ReservarFolioCommand.cs`).

### 4.4 Errores de series al emitir

| Mensaje o código | Qué significa | Qué hacer |
|---|---|---|
| `SERIE_NO_CONFIGURADA` | No hay serie activa para ese documento y sucursal emisora | Crear o activar la serie en Administración › Series (`backend/src/Compartido/Application/Administracion/Series/ReservarFolioCommand.cs`) |
| `SERIE_VENTA_DIRECTA_NO_CONFIGURADA` | Falta la serie de venta directa de la sucursal | Crearla en Administración › Series (mismo archivo) |
| `SERIE_AMBIGUA` | Hay varias series activas candidatas | Corregir el catálogo dejando una sola activa (mismo archivo) |
| `SERIE_FISCAL_CONTINUIDAD_PENDIENTE` | Serie fiscal heredada con reinicio | Fiscal concilia y configura reemplazo (mismo archivo) |
| Folio duplicado | Dos documentos con el mismo folio | No corregir el folio a mano; seguir `docs/modulos/administracion/08-operacion-y-runbook.md` §3.6 |

## 5. Cuando falla el PAC (FiscalAPI)

### 5.1 Cómo se clasifica un fallo

El adaptador devuelve uno de tres resultados (`FiscalApiTimbradoAdapter.TimbrarAsync`, `backend/src/Integraciones.Fiscal/Infrastructure/SdkAdapter/FiscalApiTimbradoAdapter.cs`):

| Resultado | Cuándo | ¿Se gastó timbre? |
|---|---|---|
| **Fallido** con código `CSD_*` | CSD ausente, vencido o revocado (rechazo previo al PAC) | No |
| **Fallido** con `CFDI40xxx` u otro código del SAT/PAC | El PAC o el SAT rechazaron el documento | No |
| **Fallido** con `PAC_SIN_SALDO` | El proveedor no tiene timbres: «avisa a Sistemas para que recargue los timbres» | No |
| **En proceso** con `PAC_SIN_RESPUESTA` o `PAC_RESPUESTA_INCOMPLETA` | Tiempo agotado o respuesta sin timbre: **ambiguo, el timbre pudo haberse generado** | **Posiblemente** |

### 5.2 Qué hacer según el síntoma

| Síntoma | Qué hacer | Fuente |
|---|---|---|
| `CFDI40xxx` (datos fiscales) | Corregir el dato y usar **Reintentar timbrado** en el detalle. Mismo comprobante y mismo folio interno | `docs/modulos/facturacion/08-operacion-y-runbook.md`, «Semántica de errores del timbrado» |
| `CSD_NO_CONFIGURADO`, `CSD_VENCIDO`, `CSD_REVOCADO` | Capturar o renovar el CSD (sección 3) y reintentar | `FiscalApiTimbradoAdapter.cs` (`BloqueoCsd`) |
| `PAC_SIN_SALDO` | Sistemas recarga los timbres en FiscalAPI; luego reintentar | `FiscalApiTimbradoAdapter.cs` (`MensajeSinSaldo`) |
| **En proceso** | **No reintentar a mano.** El worker lo revisa cada 5 minutos y, pasados 30 minutos, lo marca `PAC_TIMEOUT` | `backend/src/Facturacion/Infrastructure/Workers/TimbradoPendienteWorker.cs` (`IntervalSeconds` 300, `UmbralMinutos` 30) |
| `PAC_TIMEOUT`, `PAC_SIN_RESPUESTA`, `PAC_RESPUESTA_INCOMPLETA` | **Antes de reintentar,** buscar en el panel de FiscalAPI un timbre con la misma serie y folio. Si existe, adoptarlo con soporte; reintentar sin revisar **duplicaría el CFDI ante el SAT**. El worker **nunca** re-timbra solo | `TimbradoPendienteWorker.cs` (comentario de clase); `docs/modulos/facturacion/08-operacion-y-runbook.md` |
| El documento ya no se emitirá | **Descartar** en el detalle: pasa a `Descartada`, quema el folio y libera el pedido | `docs/modulos/facturacion/08-operacion-y-runbook.md` |

Reglas del reintento: permiso `facturacion.comprobantes.reintentar-timbrado` (`backend/src/Identidad/Domain/PermisosCanonicos.cs`); aplica a factura, anticipo, nota de crédito, REP y carta porte en `TimbradoFallido`; dos reintentos simultáneos no generan dos UUID (el segundo recibe `REINTENTO_EN_CURSO`) (`docs/modulos/facturacion/08-operacion-y-runbook.md`, «Concurrencia del reintento»). La solicitud queda guardada **antes** de llamar al PAC, así que una caída del proceso no pierde el comprobante (`backend/src/Facturacion/Application/Timbrado/TimbradoDurable.cs`).

### 5.3 Bitácora de contingencias y aviso a Fiscal

- Cada intento fallido por causa **externa** se registra como contingencia: tipo **Certificado** si el código empieza con `CSD_`, tipo **PAC** si empieza con `PAC_` o es un error HTTP de servidor, tiempo agotado, límite de uso o autenticación (401/403). Los rechazos del propio documento (`CFDI40xxx`, receptor, catálogos) **no** son contingencia: se corrigen (`backend/src/Facturacion/Application/Timbrado/Contingencias/ContingenciasTimbrado.cs`, `Clasificar`).
- Pantalla: Facturación › Contingencias (`frontend/src/routes/_app/facturacion/contingencias.tsx`).
- Un worker avisa por correo a Fiscal las contingencias abiertas **cuando su mes ya cerró**, una vez por contingencia, con un correo por empresa; sin correos configurados solo queda en el log (`backend/src/Facturacion/Infrastructure/Workers/ContingenciasAlertaWorker.cs`; de dónde salen los correos de Fiscal: **por confirmar**).

### 5.4 Pasos para una caída general del PAC

1. Confirmar la caída en el estado de FiscalAPI y con «Probar conexión» (`POST .../configuracion/{empresaId}/{proveedor}/test`).
2. **No** desactivar el timbrado ni reintentar en masa. Los comprobantes quedan en `TimbradoFallido` o en proceso y se atienden con la tabla 5.2.
3. Si la caída es larga, registrar la contingencia (queda sola en la bitácora) y avisar a Fiscal antes del cierre del mes.
4. Al regresar el servicio, reintentar primero los `TimbradoFallido` sin ambigüedad y después los `PAC_TIMEOUT` **uno por uno, tras revisar el panel de FiscalAPI**.
5. Verificar en Facturación › Reportes › Cierre del día con «Verificar estatus SAT» que los CFDI del día estén «Vigente» (`docs/modulos/facturacion/08-operacion-y-runbook.md`).

El consumo de timbres y el saldo del PAC **no** se monitorean desde el ERP; solo se detecta cuando un timbrado falla con `PAC_SIN_SALDO` (no se encontró un monitor de saldo en `backend/src`; **por confirmar**).

## 6. Banxico y tipo de cambio

### 6.1 Qué hace el sistema

- Un worker descarga el tipo de cambio **al arrancar y cada hora** por omisión (`backend/src/Compartido/Infrastructure/Banxico/BanxicoOptions.cs`, `Interval`; `backend/src/Compartido/Infrastructure/Workers/TipoCambioBanxicoWorker.cs`). Es idempotente: correr de más no duplica ni pisa capturas manuales.
- Descarga el FIX del dólar (serie SIE `SF43718`) y los valores del euro (`SF46410`) y del dólar canadiense (`SF60632`), pide los últimos 15 días y rellena hasta 7 días hacia atrás si el job no corrió (`backend/src/Compartido/Application/Catalogos/Monedas/DescargarTipoCambioBanxicoCommand.cs`, `SeriesBanxico`).
- **Regla de la «escalera»:** una operación en la fecha X usa el valor que el DOF publicó el día hábil bancario inmediato anterior a X; sábados, domingos y festivos repiten el último valor. Los días hábiles bancarios salen de las fechas que tienen FIX, no del calendario laboral. La descarga llega hasta el último día calculable (mañana después de las 12:00) para que una factura temprano no se quede sin TC. Nunca se inventa un valor (`backend/src/Catalogos/Domain/EscaleraTipoCambio.cs`; `DescargarTipoCambioBanxicoCommand.cs`).
- Se guardan 4 decimales (`EscaleraTipoCambio.Decimales`).
- El origen de cada valor queda en el catálogo: Banxico o Manual (`backend/src/Catalogos/Domain/OrigenTipoCambio.cs`).

### 6.2 Dónde vive el token de Banxico

- Sección `IntegracionesBanxico` (`BanxicoOptions.SectionName`). El token vive en Key Vault como `IntegracionesBanxico--Token`; en local, en user-secrets; nunca en appsettings (comentario de `BanxicoOptions.cs`).
- **Sin token el worker no corre** y lo dice al arrancar en el log: «Sin IntegracionesBanxico:Token la descarga del tipo de cambio de Banxico no corre; los TC se capturan a mano» (`TipoCambioBanxicoWorker.cs`).
- **No se encontró en `infra/` el cableado del secreto del token hacia el App Service** (la búsqueda de `Banxico` en `infra/modules/appservice.bicep` y `infra/parameters/*.bicepparam` no trae resultados), a diferencia del tenant key de FiscalAPI. Cómo llega el token a cada ambiente: **por confirmar**.
- Para cambiar el token: actualizar el secreto en Key Vault y reiniciar o esperar a que el App Service relea la referencia (el momento exacto en que la relee: **por confirmar**).

### 6.3 Cuando falla Banxico

1. **Qué ocurre solo:** el worker registra el error, reintenta en el siguiente ciclo (cada hora) y manda **un aviso al día** al Contador General por tipo de aviso (falla, o conflicto con un valor manual) (`TipoCambioBanxicoWorker.cs`, `_ultimaAlertaFalla` y `_ultimaAlertaConflicto`).
2. **Hoy ese aviso solo va al log**, no a correo (`backend/src/Compartido/Infrastructure/Banxico/AlertaTipoCambioLogAdapter.cs`; registro en `backend/src/Api/Program.cs`, `IAlertaTipoCambioPort`).
3. **Qué hacer:** si es la mañana y aún no hay TC del día, capturarlo a mano: Administración › Catálogos › Monedas › moneda › pestaña «Tipos de cambio», fecha y valor en pesos, permiso `catalogos.tipos-cambio.gestionar` (`backend/src/Api/Endpoints/Catalogos/MonedasEndpoints.cs`, `POST /api/v1/catalogos/monedas/{id}/tipos-cambio`; `backend/src/Identidad/Domain/PermisosCanonicos.cs`; `docs/modulos/administracion/08-operacion-y-runbook.md` §2.6).
4. **Sin TC no se factura en moneda extranjera:** la factura se rechaza antes de reservar folio o llamar al PAC con `TIPO_CAMBIO_NO_REGISTRADO` (`backend/src/Facturacion/Application/Facturas/TipoCambioFactura.cs`).
5. **Conflicto:** si hay un valor manual distinto al de Banxico para una fecha, el job **no lo sobrescribe** y avisa; Contabilidad decide cuál queda (`DescargarTipoCambioBanxicoCommand.cs`, campo `Conflictos`).
6. Cuando Banxico vuelve, el siguiente ciclo rellena lo que falte; no hace falta correr nada a mano. Un disparo manual de la descarga desde pantalla o API: **por confirmar** (en `backend/src` el comando solo lo llama el worker).

### 6.4 Comercio exterior: el TC del complemento no se captura

- El `TipoCambioUSD` del Complemento de Comercio Exterior debe coincidir con el publicado en el DOF (validación CCE121 del SAT, artículo 20 del CFF). El sistema lo toma del catálogo para la fecha de emisión y **rechaza un valor distinto capturado a mano** con `CCE_TC_DOF_NO_CORRESPONDE`; sin valor en el catálogo, `CCE_TC_DOF_NO_REGISTRADO` (`backend/src/Facturacion/Application/Facturas/TipoCambioCce.cs`; `backend/src/Facturacion/Application/Catalogos/TipoCambioDofQuery.cs`).
- En un reintento o pedimento, el TC sale del catálogo del **día del intento**, no del día original (`TipoCambioCce.cs`).
- Hay una rama abierta para ajustar el TC del DOF del complemento (`fix/FAC-cce-tc-dof-catalogo`, según la coordinación del 10-nov). Este manual describe `develop` al 11-nov y puede cambiar cuando esa rama entre (**por confirmar**).

### 6.5 Tipo de cambio hacia A+W

- Si A+W debe aplicar el TC a las **08:00** y no lo aplicó o lo rechazó, y si el TC de mañana no está publicado a las **18:00**, el worker alerta; la publicación tardía o fallida después de la hora límite también sale **por correo** a los destinatarios del parámetro `AvisoTipoCambioAwParametro`, editable en Administración › Parámetros sin desplegar (`backend/src/Integraciones.Aw/Infrastructure/TipoCambio/Workers/AwTipoCambioWorker.cs`; `backend/src/Integraciones.Aw/Application/TipoCambio/AwTipoCambioOptions.cs`; `backend/src/Administracion/Domain/AvisoTipoCambioAwParametro.cs`).
- Monedas publicadas por omisión: USD, EUR y CAD. Está **apagado por omisión** (`Habilitado = false`) hasta el visto bueno de TI (`AwTipoCambioOptions.cs`). Contrato: `docs/integration/12-tipo-cambio-aw.md`.

## 7. Revisión periódica (lista corta)

| Frecuencia | Revisión | Dónde |
|---|---|---|
| Diario | Hay TC del día para USD, EUR y CAD en `/admin/catalogos/monedas` | sección 6.3 |
| Diario | Contingencias abiertas en Facturación › Contingencias | sección 5.3 |
| Semanal | Estado del CSD (Vigente / Próximo a vencer) por empresa | sección 3.4 |
| Mensual | Saldo de timbres en el panel de FiscalAPI (el ERP no lo monitorea) | sección 5.4 |
| Antes del cierre | Cierre del día › «Verificar estatus SAT» sin diferencias | `docs/modulos/facturacion/08-operacion-y-runbook.md` |
| Al día siguiente de una caída del PAC | Comprobantes en `PAC_TIMEOUT` revisados uno por uno | sección 5.2 |

## 8. Por confirmar

1. Pantalla y botón exactos para **revocar** un CSD y para **adjuntar la evidencia** de una renovación (hay endpoint y propietario de adjuntos; no se revisó el frontend).
2. Si guardar la ApiKey o el CSD invalida la caché del resolver en **todas** las instancias o solo en la que atiende la petición (el TTL es de 60 segundos).
3. Cómo se entrega el token de Banxico (`IntegracionesBanxico--Token`) a cada ambiente: no hay cableado en `infra/`.
4. Quién recibe el **correo** del aviso de CSD, el de falla de Banxico y el de contingencias de Fiscal: los dos primeros hoy solo van al log; de los correos de Fiscal no se verificó su origen de configuración.
5. Si existe un disparo manual de la descarga de Banxico desde la interfaz (solo se encontró el worker).
6. Comportamiento vigente del folio del PAC (si FiscalAPI sigue asignándolo por RFC) y huecos esperados.
7. Monitoreo del saldo de timbres: no se encontró en el código.
8. El efecto de la rama del TC del DOF del complemento de comercio exterior sobre la sección 6.4 cuando se integre.
9. Procedimiento de Millet para tramitar el CSD ante el SAT y quién lo custodia (fuera del código).

## 9. Diferencias encontradas con los runbooks existentes

| Documento | Dice | El código muestra |
|---|---|---|
| `docs/modulos/administracion/08-operacion-y-runbook.md` §1.5 y §2.6 | La descarga automática DOF/Banxico es deuda futura y el origen siempre es Manual | Existe `TipoCambioBanxicoWorker` con origen Banxico y escalera (`backend/src/Compartido/Infrastructure/Workers/TipoCambioBanxicoWorker.cs`; `backend/src/Catalogos/Domain/OrigenTipoCambio.cs`). Conviene actualizar ese runbook. |
| `docs/modulos/administracion/08-operacion-y-runbook.md` §1.7 | Las series de CFDI son anuales, con prefijo `F-{SUCURSAL}` | Las series fiscales no admiten reinicio anual ni mensual (`backend/src/Administracion/Domain/Serie.cs`; `ActualizarSerieCommand.cs`). |

---
*Revisión: borrador 11-nov-2026. No sustituye a la ficha de alta del emisor que Fiscal de Millet conserve.*
