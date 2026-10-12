# Guía de demo 03 — Cuentas por Pagar y Tesorería, campo por campo

> **Propósito.** Que Eliam repita en su laptop, sin adivinar, el recorrido real
> *factura contra OC → autorización → pago → reflejo en Compras*: qué se escribe
> o se elige en **cada campo de cada pantalla**, con **qué valor exacto** y **por
> qué** (regla de negocio, dependencia o validación que lo exige).
>
> **Fuentes.** Recorrido real del 12-oct-2026 (`.verification/demo-tour/cxp-tesoreria/pasos.md`
> y sus capturas), código (`frontend/src/features/cxp`, `frontend/src/features/tesoreria`,
> `backend/src/CuentasPorPagar`, `backend/src/Tesoreria`, `backend/src/DatosMaestros`),
> `docs/modulos/cuentas-por-pagar/*`, `docs/modulos/tesoreria/*` y
> `docs/operacion/demo-geovany-usuarios-roles-y-datos.md`.
> Donde una razón no se pudo sustentar en código o docs se escribe
> **(razón no verificada)**. Lo que sale del código pero **no se ejecutó** en el
> recorrido se marca *(del código, no probado)*.

## 0. Quién hace esto y qué debe existir antes

**Quién:** **Geovany** (`geovanny@uzieltzaboutlook.onmicrosoft.com`, rol `super-admin`)
hace **todo** este bloque. Con ese rol no sale el aviso "Requiere el permiso X" en
ninguna pantalla de CxP ni de Tesorería (tiene los 273 permisos). La columna
"Permiso" de cada paso solo sirve si luego se repite con otro rol (`cxp-demo`,
`tesoreria-demo`).

**Entorno del recorrido:** web `http://localhost:5173`, API `http://localhost:5080`,
base `millet_demo`, Service Bus en modo `EmuladorLocal`, navegador a 1440x900, idioma es-MX.

| Prerrequisito | Valor en la demo | Si falta |
|---|---|---|
| Proveedor **Activo** con **5 documentos vigentes** y datos bancarios | `DEMO-PROV-ACT` (RFC `XAXX010101000`): banco `DEMO Banco ficticio`, CLABE `000000000000000000`, beneficiario `DEMO-PROV-ACT Suministros ficticios`, "Expediente completo" | Capturar factura: `PROVEEDOR_EN_REVISION` / `PROVEEDOR_NO_ACTIVO`. Pagar: los mismos dos códigos. Sin datos bancarios el pago se puede registrar pero el panel avisa "El proveedor no tiene datos bancarios capturados" |
| **OC Autorizada** del mismo proveedor, sucursal y moneda que la factura | `OC-MID2026-000007`, sucursal `MID · Planta México Centro`, MXN, 30 días de pago. Línea 1 `ART-CRT-01` 10 PZA a $250.00; línea 2 `ART-EST-01` 20 PZA a $35.00 | `OC_ESTADO_NO_FACTURABLE`, `OC_PROVEEDOR_MISMATCH`, `OC_SUCURSAL_DISTINTA`, `OC_MONEDA_DISTINTA` |
| **Recepción** registrada en Almacén contra esa OC | `M-ENT2026-000011`: 6 de 10 `ART-CRT-01`, $1,500.00. Ya llegó a CxP (tabla `cuentas_por_pagar.recepciones_oc_local`, ~5 s después de registrarse) | Lo **elegible a pagar** sale 0.00 y Tesorería no deja pagar (`PASIVO_NO_PAGABLE_CXP`) |
| **Periodo contable abierto** | Octubre 2026 (`contabilidad.periodos_contables`, estado 1). Debe cubrir la **fecha de contabilización**, **hoy** y la **fecha valor** del pago | `CXP_PERIODO_CERRADO` (CxP) o `MOV_PERIODO_CERRADO` (Tesorería) |
| **Cuenta bancaria MXN** activa de Tesorería | `DEMO-CTA-MXN` (número `DEMOCTAMXN`, saldo -4,704.38 MXN). También existe `DEMO-CTA-USD` | El selector de cuenta dice "Sin cuentas activas en MXN" y el botón queda deshabilitado |
| **Emulador de Service Bus** arriba | `tools/demo/demo.sh eventos`; revisar con `tools/demo/demo.sh estado` (todo "ok") | Recepción → CxP, CxP → Tesorería y Tesorería → CxP/Compras **no viajan** (el backend descarta el evento) |
| Archivo CFDI de ejemplo | `.verification/demo-tour/cxp-tesoreria/DEMO-7001.xml` (ver sección 10) | No hay UUID que vincular |

**Datos que ya existían (no hubo que crear nada por SQL):** proveedor, 5 documentos,
datos bancarios, cuenta `DEMO-CTA-MXN`, periodo octubre 2026, recepción `M-ENT2026-000011`,
6 facturas previas en la bandeja (PRB-2001, PRB-2002, PRB-1001, MOD-1001, MOD-1002, CC-88)
y 5 CFDI previos.

---

## Paso 1. Menús de Cuentas por Pagar y de Tesorería

**Quién / ruta:** Geovany. Icono **CxP** de la barra izquierda (abre `/cxp/facturas`) e
icono **Tesorería** (abre `/tesoreria/pagos`). Capturas: `01a-menu-cuentas-por-pagar-facturas.png`,
`01b-menu-tesoreria.png`. No hay campos.

| Menú | Entradas | Para qué se usa en esta guía |
|---|---|---|
| CxP > Operación | CFDIs recibidos, **Facturas**, Revisión por área, Notas de crédito, Anticipos, Notas de cargo, Comprobaciones, Viáticos, Tarjetas de crédito | CFDIs recibidos (paso 3.1) y Facturas (pasos 3.2 a 5) |
| CxP > Reportes | Auxiliar de proveedores, **Antigüedad de saldos**, **Cartera por proveedor**, Estados de cuenta TC | Paso 9 |
| CxP > Configuración | Retenciones por concepto, Aprobadores, Políticas de viáticos, Reposiciones de caja | No se usan |
| Tesorería > Operación | **Pagos a proveedor**, Pagos a cuenta, Corridas de pago, Depósitos por confirmar, **Movimientos bancarios**, Conciliación bancaria, REPP de proveedor, **Cuentas bancarias** | Pasos 2, 6, 7 y 8 |
| Tesorería > Reportes | Flujo de efectivo, Auxiliar de bancos | No se usan |

**Qué debe verse:** la bandeja de Facturas ya trae 6 facturas previas; la bandeja de pagos de
Tesorería sale vacía: "Sin pasivos pendientes de pago."

---

## Paso 2. Preparación: datos bancarios del proveedor y cuenta bancaria MXN

**Quién / ruta:** Geovany. Admin > Datos maestros > Proveedores > `DEMO-PROV-ACT`
(`/admin/datos-maestros/proveedores/5825c5b0-7f12-8069-a7f6-20e3e428f188`) y
Tesorería > Cuentas bancarias (`/tesoreria/cuentas`).
**En la demo no se guardó nada: ya existían.** Capturas: `02a`, `02b`, `02c`, `02d`.

### 2.1 Datos bancarios del proveedor (botón "Editar" de la sección "Datos bancarios")

Permiso: ver `datos_maestros.proveedores.bancarios-ver`; editar `…bancarios-editar`.
Sin `bancarios-ver` la sección no aparece (sin error). Captura `02b-proveedor-editar-datos-bancarios-sin-guardar.png` (solo el formulario).

| Campo | Qué poner (valor de la demo) | Por qué / qué pasa si falta o está mal |
|---|---|---|
| **Banco** | `DEMO Banco ficticio` (máx. 120) | Se muestra en la bandeja de pagos y en el panel de pago para que Tesorería sepa a dónde transfiere. Si se vacía, el formulario manda `limpiarBanco` y queda "—". Más de 120 o vacío con valor: `PROVEEDOR_BANCO_INVALIDO` |
| **Nueva CLABE** | Vacío = no cambiar. Valor actual: `000000000000000000` (18 ceros, **ficticia**). Para cambiarla, 18 dígitos | El campo **nunca** se rellena con la CLABE real (es dato sensible; llega enmascarada salvo permiso). El formulario exige `^\d{18}$` ("La CLABE debe tener 18 dígitos.") y el backend `PROVEEDOR_CLABE_INVALIDA`. **No valida dígito verificador** (por eso aceptó 18 ceros). Nótese que la CLABE de la *cuenta propia* de Tesorería sí valida el dígito de control (ver 2.2) |
| **Quitar la CLABE registrada (000000000000000000)** (casilla) | Sin marcar | Es la única forma de borrar la CLABE. Marcarla **y** teclear una CLABE nueva es inválido: "No puedes capturar una CLABE nueva y quitarla a la vez." |
| **Beneficiario** | `DEMO-PROV-ACT Suministros ficticios` (máx. 254) | Nombre del titular de la cuenta a quien se transfiere (puede diferir de la razón social). Vacío = se limpia. Más de 254: `PROVEEDOR_BENEFICIARIO_INVALIDO` |

| Botón | Qué hace |
|---|---|
| **Guardar cambios** | Guarda; toast "Datos bancarios actualizados". En la demo **no se pulsó** |
| **Cancelar** | Cierra el formulario sin guardar |

### 2.2 Cuenta bancaria MXN de Tesorería (solo si faltara; botón "Nueva cuenta")

Permiso `tesoreria.cuentas.ver` (ver) y `tesoreria.cuentas.administrar` (crear). Captura `02d-tesoreria-nueva-cuenta-formulario-sin-guardar.png` (solo el formulario). Basta la cuenta `DEMO-CTA-MXN`.

| Campo | Qué poner | Por qué / error |
|---|---|---|
| **Banco** | Texto libre, máx. 120 (el placeholder sugiere `BBVA México`) | Obligatorio (`CTA_BANCO_VACIO`) |
| **Número de cuenta** | Texto máx. 40 (placeholder `0123456789`) | Obligatorio; **inmutable** después: identifica la cuenta en toda la operación. Si se captura mal: desactivar y crear otra |
| **CLABE (opcional)** | Vacío o 18 dígitos **con dígito de control válido** | A diferencia de la CLABE del proveedor, aquí se valida el dígito de control al guardar |
| **Moneda (ISO 4217)** | `MXN` | Debe ser de 3 letras (`CTA_MONEDA_INVALIDA`). **Define la moneda de todos los pagos**: el pago exige moneda de cuenta = moneda del pasivo (RN-3) |
| **Cuenta contable (opcional)** | Placeholder `1102-001`; máx. 40 | Referencia para Contabilidad futura (TES-5) |
| **Perfil de extracto (opcional)** | Placeholder `bbva-csv`; máx. 40 | Parser del extracto para la conciliación bancaria; no hace falta para pagar |
| **Sucursal / Finalidad / Titular / Firmantes** | Texto libre (máx. 120 / 400 / 200 / 1000) | Inventario de la cuenta; opcionales |

| Botón | Qué hace |
|---|---|
| **Crear cuenta** | Crea la cuenta Activa. Toast "Cuenta bancaria creada" |
| **Cancelar** | Cierra |

Al **editar** una cuenta existente aparece además la sección "Saldo inicial" (Saldo al corte, Fecha de corte, Motivo y referencia): captura única. Si la cuenta tiene fecha de corte, todo movimiento debe ser **posterior** al corte (`MOV_ANTERIOR_AL_CORTE`). `DEMO-CTA-MXN` no dio este error con fecha valor 12/10/2026.

**Qué debe verse:** `02a`: proveedor "Activo", Datos bancarios completos, "Expediente completo" (5 documentos vigentes); `02c`: dos cuentas activas (`DEMO-CTA-MXN` -4,704.38 MXN y `DEMO-CTA-USD` 0.00 USD).

**Si sale error:** ninguno en rojo. La cuenta MXN con saldo negativo es normal en la demo (el sistema permite pagar sin fondos; ver "Problemas conocidos").

---

## Paso 3. Capturar la factura contra la OC

> **Por qué el formulario de factura no tiene campo de UUID:** el UUID viene del CFDI
> (timbre fiscal), no se teclea. Primero se **carga el XML** en "CFDIs recibidos" y luego la
> factura se **vincula** a ese CFDI; al vincularlo se copian el UUID, folio, serie, fechas,
> moneda, importes y tipo de cambio. Así el UUID es el del SAT y no puede escribirse mal.
> El backend además compara contra el XML guardado (ver 3.2, errores `CFDI_UUID_DISTINTO`, etc.).

### Paso 3.1 Cargar el CFDI (XML) del proveedor

**Quién / ruta:** Geovany. CxP > CFDIs recibidos (`/cxp/cfdis`) > botón **Cargar CFDI** (panel
"Cargar CFDI manualmente"). Permiso `cuentas_por_pagar.cfdis.cargar-manual` (+ `cfdis.leer`).
Capturas: `03a-cfdis-recibidos-antes.png` (5 CFDI previos), `03b-cargar-cfdi-xml-seleccionado.png`,
`03c-cfdi-DEMO-7001-por-procesar.png`.

| Campo | Qué poner (valor de la demo) | Por qué / qué pasa si falta o está mal |
|---|---|---|
| **XML del CFDI** * | Elegir `DEMO-7001.xml` (carpeta `.verification/demo-tour/cxp-tesoreria/`) | Obligatorio. Debe terminar en `.xml` ("El archivo debe tener extensión .xml." / "Selecciona el XML del CFDI."). Debe ser CFDI **4.0 timbrado**; el UUID, RFC emisor y totales se leen del XML. Errores del parser: `CFDI_XML_MALFORMADO`, `CFDI_ROOT_INVALIDO`, `CFDI_VERSION_NO_SOPORTADA`, `CFDI_SIN_TIMBRE`, `CFDI_UUID_VACIO`, `UUID_CFDI_FORMATO_INVALIDO` (8-4-4-4-12 hex), `RFC_FORMATO_INVALIDO` |
| **Representación impresa (PDF, opcional)** | Vacío | Solo para el botón "Ver PDF" después. Si se elige, debe terminar en `.pdf` |

| Botón | Qué hace |
|---|---|
| **Cargar CFDI** | Parsea, deduplica por UUID, guarda XML/PDF y crea el CFDI en estado **Por procesar**, canal **Carga manual**. Publica `cuentas_por_pagar.cfdi.ingresado.v1` hacia Almacén (enlace diferido de recepciones). Toast: "CFDI cargado. UUID 3B8E5C21-7A94-4D6F-8E10-5F2C9A4B6D73 quedó en Por procesar (canal Carga manual)." |
| **Cancelar** | Cierra sin cargar |

**Qué debe verse:** el CFDI `DEMO-7001` en la bandeja, estado "Por procesar", canal Carga manual
(`03c`). El toast tapa la barra superior unos 8 s; es normal.

**Si sale error:**

| Error | Causa |
|---|---|
| `CFDI_DUPLICADO` ("Ya existe un CFDI con UUID '…'") | **El UUID ya se cargó.** Pasa si se repite la carga del mismo XML. Solución: UUID nuevo (sección 11) |
| Toast "Sin permiso para cargar CFDIs" (403) | Falta `cuentas_por_pagar.cfdis.cargar-manual` (Geovany no la necesita pedir) |
| Otros `CFDI_*` / `RFC_*` / `UUID_*` | XML mal formado o sin timbre; ver tabla de arriba |

### Paso 3.2 Capturar la factura (panel "Capturar factura desde OC")

**Quién / ruta:** Geovany. CxP > Facturas (`/cxp/facturas`) > **Capturar factura**.
Permiso `cuentas_por_pagar.facturas.capturar` + `…facturas.gestionar-todas-sucursales`; el selector de
OC pide `compras.ordenes.leer` y `…leer-todas-sucursales`.
Capturas: `04a-facturas-bandeja-antes.png`, `04b-capturar-factura-sheet-vacio.png`, `04c-selector-cfdi-recibido.png`,
`04d-selector-oc-solo-autorizadas.png`, `04e-selector-linea-oc.png`, `04f-formulario-completo-antes-de-capturar.png`.

> **Panel que no se desplazaba (problema 1, corregido):** en `main` el panel "Capturar
> factura desde OC" no hacía scroll y a 1440x900 el botón "Capturar factura" quedaba fuera
> de pantalla. La rama `fix/demo-12oct-correcciones` lo corrige
> (`overflow-y-auto` en `CapturarFacturaSheet.tsx`). Si ejecutas esta rama no hace falta nada;
> si ejecutas `main` sin esta corrección, baja el zoom del navegador a **67 %** (Ctrl y el signo
> menos, 3 veces) o usa una ventana de al menos 1350 px de alto.

**Orden obligatorio de llenado** (porque unos campos se derivan de otros):
1) CFDI base → 2) Orden de compra → 3) Fecha de vencimiento → 4) Importar líneas → 5) Línea de OC → 6) Capturar.
Si se elige el CFDI **después** de teclear importes, el CFDI los sobrescribe; si se cambia la OC, **todas las líneas de OC se reinician a vacío** (las ligas viejas apuntarían a líneas de otra OC).

#### Columna izquierda: CFDI base

| Campo | Qué poner (valor de la demo) | Por qué / qué pasa si falta o está mal |
|---|---|---|
| **CFDI base** (selector "Vincular CFDI recibido…") | `DEMO7001 · 3B8E5C21… · XAXX010101000` (`04c`) | Trae el UUID y autollena Serie `DEMO`, Folio `7001`, Fecha documento 2026-10-12, Moneda, Subtotal 1500, IVA 240, Total 1740, Tipo de cambio. El selector lista solo CFDI **Ingreso** en **Por procesar** (en la demo salen también PRB2001 y PRB1002: **no** elegirlos). El backend exige además que el RFC emisor del CFDI = RFC del proveedor de la OC, misma moneda y estado Por procesar: si no, `CFDI_NO_UTILIZABLE`. Sin CFDI se puede capturar a mano, pero entonces no hay UUID, ni comparación con el XML ni importación de líneas: **para la demo siempre vincular** |
| Metadatos (UUID, Folio, Fecha CFDI, Total CFDI) | Solo lectura: `3B8E5C21-7A94-4D6F-8E10-5F2C9A4B6D73`, `DEMO-7001`, `2026-10-12`, `1740.00 MXN` | Sirven para verificar que se eligió el CFDI correcto |
| Botones **XML** / **Ver PDF** | Opcionales | Descargan el XML guardado / abren el PDF si se subió |

#### Columna derecha: cabecera

| Campo | Qué poner (valor de la demo) | Por qué / qué pasa si falta o está mal |
|---|---|---|
| **Orden de compra** * | `OC-MID2026-000007` (`04d`) | El selector solo lista OC **Autorizadas** del proveedor (aquí 000007 y 000005). La OC es dueña de proveedor y sucursal. Vacío: "Selecciona una orden de compra." Estados admitidos por el backend: Autorizada, EnRecepción, Recibida, EnFacturación; otro: `OC_ESTADO_NO_FACTURABLE`. OC inexistente: `OC_NO_ENCONTRADA` |
| **Proveedor (de la OC)** | Solo lectura: `DEMO-PROV-ACT Suministros ficticios` | Derivado de la OC; el usuario no lo teclea. El backend lo revalida (`OC_PROVEEDOR_MISMATCH`). Proveedor en revisión: `PROVEEDOR_EN_REVISION`; inactivo: `PROVEEDOR_NO_ACTIVO` |
| **Sucursal (de la OC)** | Solo lectura: `MID · Planta México Centro` | Derivada de la OC. `OC_SUCURSAL_DISTINTA` ("La sucursal de la factura debe coincidir con la OC.") es red de seguridad: con el formulario normal no salta |
| **Moneda** * | `MXN` (3 letras, se pone en mayúsculas) | Debe coincidir con la OC (`OC_MONEDA_DISTINTA`: "…para conciliar sus precios") y con el CFDI (`CFDI_NO_UTILIZABLE`). Distinta de 3 letras: "Código ISO 3 caracteres (MXN/USD/EUR)." |
| **Serie** | `DEMO` (autollenada) | Opcional (máx. 20). Se muestra como `DEMO-7001` en CxP |
| **Folio** | `7001` (autollenado) | Opcional (máx. 50). Tesorería muestra solo `7001` (sin serie) |
| **Fecha documento** * | `12/10/2026` (la del CFDI, autollenada) | Fecha del comprobante. La vencimiento no puede ser anterior a esta |
| **Fecha contabilización** * | `12/10/2026` (hoy, por defecto) | **Debe caer en un periodo contable abierto**; si no: `CXP_PERIODO_CERRADO` ("El periodo contable de dd/MM/yyyy está cerrado, no abierto o no existe. Solicita a Contabilidad su apertura o reapertura…"). Se revalida al autorizar |
| **Fecha vencimiento** * | **`11/11/2026`** (cambiar: el panel trae "hoy") | 30 días después de la fecha documento = condiciones de pago de la OC (`30 días`, visible en la OC). Es la fecha que Tesorería muestra y filtra ("Vence"). **Regla dura:** no puede ser anterior a la fecha del documento (`FACTURA_VENCIMIENTO_INVALIDO`). Que sean 30 días no lo impone el código (solo la OC lo sugiere) |
| **Tipo de cambio** | Vacío (placeholder `1.0000`) | La factura es MXN; el CFDI no trae `TipoCambio`. El backend compara `TipoCambio` del XML contra el capturado tratando "vacío" como 1: si no coinciden, `CFDI_TIPO_CAMBIO_DISTINTO`. No teclear nada |

#### Importes (se autollenan del CFDI; verificar, no cambiar)

| Campo | Qué poner | Por qué / qué pasa si falta o está mal |
|---|---|---|
| **Subtotal** * | `1500` | = 6 PZA x $250.00. Debe coincidir con el XML (`SubTotal`) y con la suma de importes de las líneas; si no, la factura **se cancela sola** (ver más abajo) |
| **Descuentos** | `0` | El CFDI no trae descuento |
| **IVA trasladado** | `240` | 16 % sobre 1500 (`TotalImpuestosTrasladados` del XML) |
| **Retenciones** | `0` | El CFDI no trae `TotalImpuestosRetenidos` |
| **Total** * | `1740` | Regla: Total = Subtotal − Descuentos + IVA − Retenciones (1500 − 0 + 240 − 0). Debe ser > 0 ("Total debe ser > 0.") y coincidir con el XML. La diferencia máxima aceptada es la **tolerancia** (general 0.99 MXN, sin tolerancia por proveedor en la demo) |

Si el total, subtotal, traslados, retenciones o descuentos difieren del XML más allá de la tolerancia, o el total no cuadra con la fórmula, el backend **no da error 4xx**: crea la factura y la **cancela de inmediato** con motivo "Rechazada por tolerancia" (toast amarillo "Factura cancelada por diferencia en la conciliación"). El CFDI vuelve a quedar utilizable para recapturar.

#### Datos del pasivo

| Campo | Qué poner | Por qué |
|---|---|---|
| **Obra** | Vacío | Opcional (máx. 120). Si la OC trae obra, **la de la OC prevalece**. Vacío: el detalle muestra "[OBRA POR CONFIRMAR]". No bloquea |
| **Concepto fiscal de retención** | `Sin concepto: por confirmar` (default) | El proveedor de la demo no retiene. Con "Sin concepto" el detalle muestra "Por confirmar: selecciona el concepto fiscal para comparar retenciones. Supuesto SAT, valida Fiscal (D03)." **No bloquea la autorización**. Si se elige un concepto y las retenciones difieren del catálogo, sale una nota ámbar ("Las retenciones difieren del catálogo. Revisa con Fiscal; puedes continuar la captura.") |
| Botón **Usar retención propuesta** | No aparece | Solo aparece sin CFDI base y con concepto elegido |

#### Líneas (1 en la demo)

| Campo | Qué poner (valor de la demo) | Por qué / qué pasa si falta o está mal |
|---|---|---|
| Botón **Importar 1 línea(s) del CFDI** | Pulsarlo una vez (toast "1 línea(s) importadas del CFDI") | Solo aparece con CFDI vinculado. Copia Descripción, Cantidad, Unidad SAT, P. Unitario, Importe y Descuento del `cfdi:Concepto`. **El backend compara cada línea contra el XML en el mismo orden y con el mismo número de líneas**; cantidad y unidad deben ser **iguales** y precio/importe dentro de tolerancia. No agregar ni quitar líneas |
| **Artículo (opcional)** | Vacío | Vínculo al catálogo de artículos. Si se elige uno, debe ser **el mismo artículo de la línea de OC** (si no, rechazo "el artículo no coincide con la línea de OC") |
| **Descripción** * | `Refacciones criticas para linea de produccion` (importada) | Obligatoria, máx. 500 en pantalla (1000 en backend) |
| **Cantidad** * | **`6`** | **= lo recibido en Almacén** (6 de 10). Debe ser > 0 y **no mayor a lo pendiente de facturar de la línea de OC** (10 − lo ya facturado). Si lo supera, rechazo "Línea 1: cantidad facturada … mayor a la pendiente … de la OC …" |
| **Unidad SAT** * | `H87` (importada del XML) | Clave SAT de "Pieza". Debe coincidir con el XML. Vacía: "Clave de unidad obligatoria." |
| **P. Unitario** * | `250` | **= precio de la OC** ($250.00). Diferencia de precio contra la OC dentro de tolerancia pasa; fuera de tolerancia: rechazo "Precio distinto en la línea 1: OC … ; factura … corrige o cancela la OC." |
| **Importe** | `1500` | Debe ser ≈ Cantidad x P. Unitario (6 x 250); si no: rechazo "la línea 1: el importe no cuadra con cantidad × precio unitario." |
| **Descuento** | Vacío | Opcional |
| **Línea de OC (obligatoria)** * | `Pos. 1 · ART-CRT-01 · Refacciones críticas para línea de producción · Cant. 10 PZA · pendiente 10` (`04e`) | Es el **three-way match por línea**: liga lo facturado a la línea de OC; de ahí salen el acumulado facturado que ve Compras y lo **elegible a pagar**. Vacía: "Selecciona la línea de OC que corresponde a este concepto." Se deshabilita ("Elige primero…") hasta elegir OC. Línea de otra OC: `LINEA_OC_NO_PERTENECE` |
| Botones **Agregar línea** / **Quitar** | No usar | Agregar una línea extra rompe la comparación línea a línea contra el XML. Mínimo 1 línea ("Agrega al menos una línea."), máximo 200 |

> **Aviso conocido (problema 2):** en `04f` aparece un texto rojo "Selecciona la línea de OC que corresponde a este concepto." con borde rojo punteado **aunque la línea ya está elegida**. No impide capturar. Ignorarlo.

#### Notas de crédito

| Campo | Qué poner | Por qué |
|---|---|---|
| **Adjuntar nota de crédito…** | Vacío | Solo para NC (CFDI de egreso, relación 01 con esta factura, mismo proveedor, receptor y tipo de cambio). No se usa en la demo. Errores si se usa: `NC_REQUIERE_CFDI`, `NC_RELACION_INVALIDA`, `NC_IMPORTES_INVALIDOS`, `NC_EXCEDE_FACTURA`, `NC_DUPLICADA`, `NC_DATOS_DISTINTOS` |

#### Botones

| Botón | Qué hace (estado resultante) |
|---|---|
| **Capturar factura** | Crea la factura en estado **Capturada** y navega a `/cxp/facturas/01a12faa-d62f-7ded-9fe1-0afd90d8a551`. Toast verde "Factura capturada". Publica `cuentas_por_pagar.factura.registrada.v1` (Compras actualiza "Facturación") |
| **Cancelar** (pie del panel) | Cierra sin crear nada |

**Qué debe verse:** toast verde y pantalla de detalle de la factura `DEMO-7001`.
Reglas comprobadas: no saltaron `OC_SUCURSAL_DISTINTA` ni `OC_MONEDA_DISTINTA`.

**Si sale error** (toast rojo con el título del Problem Details; el detalle trae el mensaje; los errores de campo se pintan bajo el campo):

| Código | Causa |
|---|---|
| `FACTURA_DUPLICADA` ("El CFDI ya tiene una factura vigente.") | Ya hay una factura no cancelada con ese UUID |
| `CXP_UUID_DUPLICADO` ("El UUID ya está consumido por una factura, una NC o un anticipo.") | El UUID ya se usó en otra factura, NC o anticipo. **Es la deduplicación por UUID** |
| `CFDI_UUID_DISTINTO` | El UUID de la factura no coincide con el del XML guardado |
| `CFDI_NO_UTILIZABLE` | El CFDI no está Por procesar, no es Ingreso, su RFC emisor no es el del proveedor de la OC, o su moneda no es la de la factura |
| `CFDI_TIPO_CAMBIO_DISTINTO` | Tipo de cambio capturado distinto al del XML |
| `OC_*` / `PROVEEDOR_*` | Ver tabla de cabecera |
| `CXP_PERIODO_CERRADO` | La fecha de contabilización cae en un periodo cerrado, no abierto o inexistente |
| `FACTURA_VENCIMIENTO_INVALIDO`, `FACTURA_TOTAL_INVALIDO` | Vencimiento anterior al documento / total ≤ 0 |
| Toast amarillo "Factura cancelada por diferencia en la conciliación" | Conciliación fuera de tolerancia (no es error HTTP; la factura queda **Cancelada**, "Rechazada por tolerancia"). Ver los mensajes del motivo justo debajo |

*Mensajes de rechazo por conciliación (del código, `ConciliacionFacturaOc`):* "El total no cuadra con subtotal − descuentos + traslados − retenciones."; "El subtotal no cuadra con la suma de los importes de las líneas."; "El {subtotal/descuento/traslados/retenciones/total} capturado (…) no coincide con el XML (…)."; "La cantidad de líneas no coincide con el XML ligado."; "La línea N: cantidad, unidad, precio, importe o descuento no coincide con el XML."; "La línea N no tiene una línea de OC vinculada."; "Línea N: cantidad facturada … mayor a la pendiente …"; "Precio distinto en la línea N…"; "Línea N: base después de descuentos y NC … distinta de lo pactado …".

---

## Paso 4. Detalle de la factura: conciliación, elegible a pagar y estado

**Quién / ruta:** Geovany. `/cxp/facturas/01a12faa-d62f-7ded-9fe1-0afd90d8a551`. Permiso
`cuentas_por_pagar.facturas.leer` + `…leer-todas-sucursales` (sin el bypass, la lista sale vacía y por id da 403).
Capturas: `05a`, `05b`, `05c`, `05d`.

**Campos editables en esta pantalla** (sección "Datos del pasivo"; no se tocaron en la demo):

| Campo | Qué poner | Por qué |
|---|---|---|
| **Obra** | Vacío | Mismo criterio que en la captura |
| **Fecha de contabilización** | `12/10/2026` (sin cambiar) | Cambiarla dispara la verificación de periodo abierto para la fecha vieja **y** la nueva (`CXP_PERIODO_CERRADO`). Solo editable en estado Capturada |
| **Concepto fiscal de retención** | `Sin concepto: por confirmar` | Ver captura. Texto de ayuda: "Supuesto SAT, valida Fiscal (D03). Contabilidad administra la apertura y reapertura de periodos." |
| Botón **Guardar datos del pasivo** | No pulsar | Guarda esos tres campos |

**Botones de la barra de acciones (factura Capturada):**

| Botón | Qué hace / estado resultante | Usar en la demo |
|---|---|---|
| **Aplicar anticipo** | Descuenta un anticipo del proveedor del saldo | No |
| **Aplicar NC** | Descuenta una nota de crédito del proveedor | No |
| **Autorizar** | Pasa a **Autorizada** y manda el pasivo a Tesorería (paso 5) | **Sí** |
| **Enviar a revisión** | Pasa a **En revisión** (pide Motivo y Área revisora); si ya estaba Autorizada, **retira el pasivo de la bandeja de Tesorería** (`pasivo.retirado-de-pago.v1`) | No |
| **Adjuntar evidencia** | Sube evidencia de autorización (solo Capturada) | No |
| **Cancelar** | Pide Motivo (+ texto si "Otro", mínimo 5 caracteres); estado **Cancelada**. Solo desde Capturada o En revisión (`FACTURA_AUTORIZADA_NO_CANCELABLE`, `FACTURA_PAGADA_NO_CANCELABLE`) | No |
| **Ver trazabilidad** | Cadena RQ → OC → factura. Requiere `compras.ordenes.leer` + los 4 bypass de sucursal; si falta uno, 403 | Sí, opcional |

**Qué debe verse:**

| Pantalla | Lo que se ve |
|---|---|
| `05a` | Folio `DEMO-7001`, estado **Capturada**, etiqueta **Sin diferencia** (conciliación dentro de tolerancia), Fecha documento 12/10/2026, Contabilización 12/10/2026, **Vence 11/11/2026**; proveedor, sucursal `MID · Planta México Centro`, OC `OC-MID2026-000007` (enlace), UUID `3B8E5C21-…`, moneda MXN |
| `05b` Totales | Subtotal 1500.00; IVA trasladado 240.00; Retenciones 0.00; **Elegible para pago 1740.00 MXN**; Retenido por falta de recepción 0.00; Total 1740.00; Pagado 0.00; **Saldo pendiente 1740.00 MXN** |
| `05c` | Adjuntos vacío; Líneas (1): `Refacciones criticas para linea de produccion`, 6.0000 H87, 250.0000, 1500.00; Evidencias vacías |
| `05d` Ver trazabilidad | Origen: OC `OC-MID2026-000007` (Autorizada) con Requisición `MID2026-010008` (EnSurtido) → Actual: Factura `DEMO-7001` (Capturada) |

Avisos ámbar normales: "Por confirmar: selecciona el concepto fiscal…" y "Obra: [OBRA POR CONFIRMAR]". **No bloquean** la autorización.

**Por qué "Elegible para pago" es 1,740.00:** se recibieron 6 de las 6 piezas facturadas (6 x $250 + IVA = 1,740). Ver la sección "Cómo se calcula lo elegible".

**Si sale error:** `Ver trazabilidad` da 403 si falta cualquiera de los permisos de alcance (no aplica a Geovany). Si "Elegible para pago" sale 0.00 → falta la recepción en CxP (Service Bus caído o recepción no registrada).

---

## Paso 5. Autorizar la factura

**Quién / ruta:** Geovany. Botón **Autorizar** del detalle. Permiso `cuentas_por_pagar.facturas.autorizar`.
**No hay campos, ni diálogo de confirmación, ni evidencia obligatoria, ni revisión previa** (no hizo falta "Enviar a revisión").

| Botón | Qué hace (estado resultante) |
|---|---|
| **Autorizar** (un solo clic) | Estado **Capturada → Autorizada**. Toast verde "Factura autorizada". Desaparecen Autorizar, Adjuntar evidencia y Cancelar (quedan Aplicar anticipo, Aplicar NC, Enviar a revisión). **No hay vuelta atrás desde esta pantalla**: una factura autorizada ya no se puede cancelar (`FACTURA_AUTORIZADA_NO_CANCELABLE`); solo "Enviar a revisión" o la solicitud de cancelación desde Tesorería |

**Qué debe verse:** `06a-factura-autorizada-detalle.png` (Autorizada; Saldo 1740.00; Elegible 1740.00) y
`06b-bandeja-facturas-DEMO-7001-autorizada.png` (bandeja: `DEMO-7001`, $1,740.00, saldo $1,740.00, Autorizada).
**Efecto entre módulos:** CxP publica `cuentas_por_pagar.factura.autorizada.v1` (informativo para Compras y Contabilidad) y
`cuentas_por_pagar.pasivo.autorizado-para-pago.v1` (para Tesorería). Outbox publicado en **~3 s**.

**Si sale error:**

| Código | Causa |
|---|---|
| `FACTURA_NO_AUTORIZABLE` | Solo Capturada o En revisión pueden autorizarse |
| `FACTURA_FALTA_REPP` | Estaba en revisión por falta de complemento de pago y aún no se registra el REPP |
| `FACTURA_GASTO_INTERNO_NO_AUTORIZABLE` | La factura documenta caja chica o viáticos ya pagados (evita doble pago). No aplica a la demo |
| `CXP_PERIODO_CERRADO` | El cambio a Autorizada se valida contra el periodo de la fecha de contabilización |
| Concurrencia (409) | La factura cambió mientras estaba abierta; recargar |

---

## Paso 6. Tesorería: el pasivo autorizado aparece en la bandeja de pagos

**Quién / ruta:** Geovany. Tesorería > Pagos a proveedor (`/tesoreria/pagos`). Permiso
`tesoreria.pasivos.ver` + `tesoreria.documentos.leer-todas-sucursales` (sin el bypass la bandeja sale **vacía**).
Capturas: `07a-tesoreria-pagos-pasivo-autorizado.png`, `07b-tesoreria-pagos-pasivo-seleccionado.png`.

**Espera:** ~5 s después de autorizar (al recargar ya estaba). El pasivo llega por el evento
`pasivo.autorizado-para-pago.v1`; Tesorería **solo paga lo que CxP ya decidió** (regla RN-1).

| Campo / control | Qué poner | Por qué |
|---|---|---|
| **Vence desde** / **Vence hasta** | Vacíos | Filtros por fecha de vencimiento. Si se llenan y no incluyen 11/11/2026, la fila no aparece |
| **Casilla de la fila** | Marcar la fila de `DEMO-PROV-ACT` | Habilita **Registrar pago (1)**. Se pueden marcar varias facturas, pero **del mismo proveedor y moneda** (`PAGO_MULTIPROVEEDOR`, `PAGO_CROSS_MONEDA`) |

| Botón | Qué hace |
|---|---|
| **Registrar pago (N)** | Deshabilitado con 0 marcadas (`Registrar pago (0)`); con la casilla marcada abre el panel del paso 7 |
| **Solicitar cancelación** (enlace de la fila) | Pide a CxP retirar el pasivo (evento `tesoreria.cancelacion-pasivo.solicitada.v1`); CxP la manda a revisión. Permiso `tesoreria.pasivos.solicitar-cancelacion`. **No pulsar** en la demo |

**Qué debe verse (`07a`):** una fila: Proveedor `DEMO-PROV-ACT Suministros ficticios` / `DEMO-PROV-ACT`, Factura `7001`, Vence 11/11/2026, **Saldo 1,740.00 MXN**, CLABE `000000000000000000` (completa porque Geovany tiene el permiso; sin él saldría enmascarada), enlace "Solicitar cancelación". El **Saldo** que muestra la bandeja es lo **elegible pendiente** que calculó CxP, no el total de la factura.
Aviso: la columna Factura muestra solo el folio `7001`, no `DEMO-7001` (problema 6).

**Si sale error / la fila no aparece:**

| Síntoma | Causa |
|---|---|
| Bandeja vacía | Evento aún no llegó (esperar 5 a 10 s y recargar), emulador de Service Bus caído, o falta `tesoreria.documentos.leer-todas-sucursales` |
| Fila con saldo 0 o menor al esperado | Falta recepción: lo elegible depende de lo recibido |

---

## Paso 7. Registrar el pago

**Quién / ruta:** Geovany. `/tesoreria/pagos` > marcar la fila > **Registrar pago (1)** (panel "Registrar pago a proveedor").
Permiso `tesoreria.pagos.aplicar` + `tesoreria.documentos.gestionar-todas-sucursales`; sin `tesoreria.cuentas.ver` falla el selector de cuenta.
Capturas: `07c-registrar-pago-formulario.png`, `07d-registrar-pago-selector-cuenta.png`, `07e-registrar-pago-selector-concepto-flujo.png`, `07f-registrar-pago-formulario-lleno.png`, `07g-pago-registrado-bandeja.png`.

> **El sistema no mueve dinero.** La transferencia se ejecuta en la banca; aquí solo se registra el
> hecho bancario (texto del propio panel). Por eso la referencia bancaria es lo que luego identifica el pago.

| Campo | Qué poner (valor de la demo) | Por qué / qué pasa si falta o está mal |
|---|---|---|
| **Tabla: Factura / Vence / Saldo** | Solo lectura: `7001`, `11/11/2026`, `1,740.00 MXN` | Resumen del pasivo seleccionado |
| **Importe a pagar** | **`1740`** (todo el saldo; viene precargado) | Debe ser > 0 y **≤ Saldo** (el campo se pinta en rojo si no y el botón se bloquea). El backend vuelve a validar contra lo **elegible** de CxP descontando pagos previos: si lo supera, `PAGO_EXCEDE_ELEGIBLE` ("Solo X es elegible para pago de la factura …. El resto está retenido hasta recibir la mercancía; registra como máximo ese importe."). Con elegible 0: `PASIVO_NO_PAGABLE_CXP` ("CxP no autoriza un importe pagable para esta factura…"). Se permite **pago parcial** (menor al saldo): el resto queda en la bandeja |
| Línea "Datos bancarios del proveedor" | Solo lectura: `DEMO Banco ficticio · 000000000000000000 · DEMO-PROV-ACT Suministros ficticios` | Para teclear la transferencia en la banca. Si el proveedor no tiene datos, aviso ámbar "El proveedor no tiene datos bancarios capturados en Datos Maestros" (no bloquea) |
| **Cuenta de egreso (MXN)** * | `DEMO-CTA-MXN · DEMOCTAMXN · -4,704.38 MXN` (`07d`; **única opción**) | Solo lista cuentas **activas de la misma moneda** que el pasivo (RN-3: moneda del movimiento = moneda de la cuenta; no hay pago cross-moneda en el MVP). Sin cuenta, el botón queda deshabilitado. Errores backend: `CTA_NO_ENCONTRADA`, `MOV_CUENTA_INACTIVA`, `PAGO_CROSS_MONEDA` |
| **Concepto de flujo de efectivo** | `Operación · Pago a proveedor (ejemplo por validar)` (`07e`: 13 opciones) | Clasifica el egreso para el reporte de **Flujo de efectivo** (Operación / Inversión / Financiamiento). **Es opcional en el código** (ni el panel ni el backend lo exigen; solo valida que esté activo: `MOV_CONCEPTO_INVALIDO`). Se elige para que el movimiento quede bien clasificado. Todas las opciones dicen "(ejemplo por validar)" porque el catálogo está pendiente de que Tesorería lo valide (docs Tesorería §5.2) |
| **Fecha valor** | `12/10/2026` (precargada con hoy) | Fecha bancaria del movimiento. **Debe caer en periodo contable abierto** (`MOV_PERIODO_CERRADO`: "El período 2026/10 está cerrado.") y ser **posterior al corte de saldo inicial** de la cuenta si lo tiene (`MOV_ANTERIOR_AL_CORTE`) |
| **Referencia bancaria** | `SPEI-DEMO-0001` (máx. 120) | **Es la única identidad visible del pago** (el pago no tiene folio propio). Opcional, pero sin ella el movimiento sale con referencia "—" |
| **Total a pagar** | Solo lectura: `1,740.00 MXN` | Suma de los importes; coincide con el movimiento |

| Botón | Qué hace (estado resultante) |
|---|---|
| **Registrar pago** | Crea el **movimiento bancario de Egreso** (Aplicación **Aplicado**, Conciliación **No conciliado**), descuenta el saldo del pasivo en Tesorería y publica `tesoreria.pago-factura-proveedor.aplicado.v1` **por factura** (RN-4). Toast verde "Pago registrado. 1,740.00 MXN aplicado a 1 pasivo(s). CxP recibirá el evento." El panel se cierra y la bandeja queda en "Sin pasivos pendientes de pago." (`07g`) |
| **Cancelar** | Cierra el panel y **borra** cuenta, referencia y fecha capturadas |

**Protección contra doble pago:** la petición lleva una clave ligada al contenido. Si se reintenta
el mismo pago por un timeout, el backend devuelve la respuesta ya guardada en lugar de registrar un
segundo desembolso. Si se corrige algún dato, la clave cambia (es otro pago).

**Qué debe verse (estado "Pagado"):**

| Pantalla | Lo que se ve |
|---|---|
| `08a` Tesorería > Movimientos bancarios (`/tesoreria/movimientos`) | Fila 12/10/2026, `DEMO-CTA-MXN`, **Egreso**, 1,740.00 MXN, referencia `SPEI-DEMO-0001`, Aplicación **Aplicado**, Conciliación **No conciliado** (el id del movimiento es `01a12fad-142a-784a-8d65-28c27b52c24f`) |
| `08b` Tesorería > Cuentas bancarias | Saldo de `DEMO-CTA-MXN`: de -4,704.38 a **-6,444.38 MXN** |

**Si sale error:**

| Código | Causa |
|---|---|
| `MOV_PERIODO_CERRADO` | Fecha valor en periodo cerrado/inexistente |
| `PAGO_PASIVO_NO_AUTORIZADO` | El pasivo no está en la bandeja (no llegó el evento o se retiró) |
| `PASIVO_NO_AUTORIZADO_CXP` | CxP retiró la autorización (la mandó a revisión o cancelación); debe autorizarse de nuevo |
| `PASIVO_NO_PAGABLE_CXP` | Elegible 0 en CxP (sin recepción) |
| `PAGO_EXCEDE_ELEGIBLE` | Importe mayor a lo elegible menos lo ya pagado |
| `PAGO_MULTIPROVEEDOR` / `PAGO_CROSS_MONEDA` / `PAGO_MEZCLA_INTERNOS` | Se mezclaron proveedores, monedas o pasivos internos con facturas |
| `PROVEEDOR_EN_REVISION` / `PROVEEDOR_NO_ACTIVO` | El proveedor no puede recibir pagos |
| `MOV_ANTERIOR_AL_CORTE` | Fecha valor ≤ fecha de corte del saldo inicial de la cuenta |

---

## Paso 8. De regreso: estado en CxP y en Compras, trazabilidad completa

**Quién / ruta:** Geovany. `/cxp/facturas/01a12faa-…` y `/compras/ordenes/01a12ebb-73a4-738f-96a4-f0220183c9a1`. No hay campos; es solo verificación.
Capturas: `08c`, `09a`, `09b`, `09c`.

| Pantalla | Qué debe verse | Cuándo |
|---|---|---|
| CxP, factura (`08c`) | Estado **Pagada**; Elegible para pago 0.00 MXN; Total 1740.00; **Pagado 1740.00 MXN**; **Saldo pendiente 0.00 MXN**; barra de acciones vacía. El CFDI `DEMO-7001` en `/cxp/cfdis` pasa a "Convertido en pasivo" | Casi inmediato tras el pago (CxP consume `tesoreria.pago-factura-proveedor.aplicado.v1`) |
| Compras, OC (`09a`) | OC Autorizada (v7). Sub-estados **Recepción parcial (20 %)**, **Facturación parcial (20 %)** y **Pago parcial** (sin porcentaje: el dato de avance de pago no lo expone la OC). Los porcentajes son por cantidades (6 de 30 piezas). En el recorrido original, hecho con `main`, decían "~50 %" fijo y la fila Recepción quedaba tapada por la barra superior; ambos defectos están corregidos en la rama `fix/demo-12oct-correcciones` | **< 1 minuto** (no hizo falta esperar 60 s) |
| Compras, OC > pestaña Líneas (`09b`) | Línea 1 `ART-CRT-01`: Cantidad 10.00, **Recibido 6.00, Por surtir 4.00**; línea 2 `ART-EST-01`: Cantidad 20.00, Recibido 0.00, Por surtir 20.00. La tabla **no** tiene columnas de facturado ni pagado | — |
| Compras, botón **Trazabilidad** (`09c`) | Origen Requisición `MID2026-010008` (EnSurtido) → Actual OC `OC-MID2026-000007` (Autorizada) → Derivados: Recepción `M-ENT2026-000011` (Registrado, 12/10/2026), Factura de proveedor `DEMO-7001` (Pagada, 12/10/2026) y, debajo, **Pago a proveedor `SPEI-DEMO-0001` (Aplicado, 12/10/2026)** | Igual |

**Si no se actualiza Compras:** el emulador de Service Bus no está arriba (`tools/demo/demo.sh estado`); esperar ~1 min. La trazabilidad da 403 si falta `almacen.entradas.leer-todas-sucursales`, `cuentas_por_pagar.facturas.leer-todas-sucursales` o `tesoreria.documentos.leer-todas-sucursales` (no aplica a Geovany).

---

## Paso 9. Extras: proveedor en revisión, revisión por área y reportes

### 9.1 Proveedor en revisión (`DEMO-PROV-REV`)

| Qué se hace | Qué debe verse |
|---|---|
| Compras > Órdenes de compra > **Nueva**; buscar `DEMO-PROV` en el selector de proveedor (`10a`, sin guardar) | Solo ofrece `DEMO-PROV-ACT`; el proveedor en revisión **simplemente no aparece** (sin mensaje de bloqueo). No se creó ninguna OC |
| `/admin/datos-maestros/proveedores/9feafc36-352d-c287-a50b-3e74f2c912dc` (`10b`) | Estado "En revisión" y franja ámbar: "Proveedor en revisión por Cuentas por Pagar. Expediente incompleto: 2 documentos faltantes. Las órdenes de compra, captura de facturas y programación de pagos están bloqueadas hasta que CxP valide el expediente." Botones **Rechazar** y **Validar proveedor** (este último **deshabilitado** por el expediente incompleto; para habilitarlo hay que cargar los 2 documentos que faltan, de 5). **No se forzó nada** |

*Por qué:* un proveedor En revisión o Inactivo no admite OC, captura de facturas ni pagos (`PROVEEDOR_EN_REVISION`, `PROVEEDOR_NO_ACTIVO` en CxP y Tesorería).

### 9.2 Revisión por área (`/cxp/revision`)
Exige elegir un **Área revisora** y no hay datos; se descartó. No mostrar en la demo.

### 9.3 Reportes de CxP (Reportes > Cartera por proveedor / Antigüedad de saldos)

| Campo | Qué poner | Por qué |
|---|---|---|
| **Fecha de corte** | Vacío (la fecha de hoy; en el recorrido salió 2026-10-12) | Corte del reporte |
| **Proveedor** | **`DEMO-PROV-ACT`** (obligatorio *de facto* en la demo) | Sin proveedor el reporte falla por **422 `CXP_HISTORICO_POR_CONFIRMAR`** (ver "Problemas conocidos") |
| **Sucursal** | Vacío ("Todas las sucursales") | Opcional |
| **Solo en revisión** (casilla; solo en Cartera) | Sin marcar | Filtra solo las facturas en revisión |

| Botón | Qué hace |
|---|---|
| **Ejecutar** | Genera el reporte con los filtros. Luego: **Excel**, **PDF**, **Imprimir** |

**Qué debe verse:** Cartera (`11c`): `DEMO-PROV-ACT Suministros ficticios`, RFC `XAXX010101000`, MXN, En revisión "No", 0-30 días **264**, Saldo **264**, **1** factura (es la `PRB-1001`, que sigue abierta). `DEMO-7001` **ya no aparece** porque está pagada. Antigüedad (`11d`): mismos importes. Sin filtro de proveedor: `11b` (error).

---

## Cómo se calcula lo elegible a pagar (recibido vs facturado vs pagado)

Tres cosas distintas, **tres cierres distintos**: se puede **facturar** hasta lo pendiente de la OC, pero solo se puede **pagar** lo que ya se **recibió** (three-way match entre OC, recepción y factura).

| Concepto | De dónde sale | En la demo |
|---|---|---|
| **Pedido** | Línea de OC (Compras) | 10 PZA de `ART-CRT-01` |
| **Recibido** | `CantidadRecibida` de la línea de OC (Almacén, menos devoluciones a proveedor y cancelaciones) | 6 |
| **Facturado** | Cantidad de las líneas de factura vigentes (no Cancelada) ligadas a la línea de OC. Se permite hasta `pendiente = cantidad OC − ya facturado` | 6 |
| **Pagado** | `ImportePagado` en CxP / pagos no revertidos en Tesorería | 0 → 1,740 |

**Fórmula (`ElegibilidadPago.Calcular` + `ElegibilidadFacturaService`):**

| Paso | Cálculo |
|---|---|
| Neto de la factura | `Total − NC aplicadas − cargos aplicados` (1,740) |
| Base total | Σ (cantidad facturada x precio neto de la OC) por línea (6 x 250 = 1,500) |
| Base recibida | Σ (mín(cantidad facturada, recibido disponible tras facturas previas de la misma OC) x precio neto) (6 x 250 = 1,500) |
| **Elegible total** | `redondear(neto x mín(1, base recibida / base total), 2)` = 1,740 x 1 = **1,740.00** |
| **Elegible pendiente** (lo que ve Tesorería como Saldo) | `mín(neto − anticipos − pagado, elegible − anticipos − pagado)` |
| **Retenido por falta de recepción** | `neto − anticipos − pagado − elegible pendiente` |

**Ejemplo derivado del código, no probado en el recorrido:** si en vez de 6 se facturaran las 10 PZA (10 x 250 + IVA = 2,900), con 6 recibidas el elegible sería `2,900 x (1,500/2,500) = 1,740.00` y "Retenido por falta de recepción" 1,160.00; Tesorería solo dejaría pagar 1,740.00 y daría `PAGO_EXCEDE_ELEGIBLE` por más.

**Cuando llega una recepción tardía** (otra recepción de la misma OC): CxP vuelve a publicar `pasivo.autorizado-para-pago.v1` de las facturas **Autorizadas** de esa OC y el saldo pagable sube.
Facturas anteriores de la misma OC consumen primero el recibido (se ordenan por fecha de creación).

**Reglas ortogonales de la factura** (no son lo elegible): tolerancia general 0.99 MXN y ninguna por proveedor (`datos_maestros`); periodo abierto; proveedor Activo.

---

## Qué evento mueve cada estado (Almacén → CxP → Tesorería → Compras)

Todos viajan por Outbox + Azure Service Bus (emulador local en la demo). Idempotentes. Sin emulador **no se actualiza nada** entre módulos.

| # | Acción del usuario | Evento (nombre de cable) | Publica → Consume | Estado que mueve | Tiempo observado |
|---|---|---|---|---|---|
| 1 | Registrar recepción en Almacén | `almacen.oc_recepcion.registrada.v1` | Almacén → **CxP** (tabla `recepciones_oc_local`) y → **Compras** | CxP: habilita lo **elegible**; Compras: sub-estado **Recepción**, Recibido / Por surtir | CxP ~5 s |
| 2 | Cargar XML (paso 3.1) | `cuentas_por_pagar.cfdi.ingresado.v1` | CxP → Almacén | Enlace diferido de recepciones variante A (no afecta este recorrido) | — |
| 3 | Capturar factura (3.2) | `cuentas_por_pagar.factura.registrada.v1` | CxP → **Compras** (y Almacén en variante B) | Compras: sub-estado **Facturación** (acumulado calculado por CxP) | < 1 min |
| 3b | Captura rechazada por tolerancia | `cuentas_por_pagar.factura.rechazada-por-tolerancia.v1` | CxP → Compras | Factura **Cancelada**; Compras corrige la OC | — |
| 4 | **Autorizar** (paso 5) | `cuentas_por_pagar.factura.autorizada.v1` | CxP → Compras / Contabilidad (informativo) | Factura **Autorizada** | ~3 s |
| 4b | Autorizar (mismo clic) | `cuentas_por_pagar.pasivo.autorizado-para-pago.v1` | CxP → **Tesorería** | Aparece en la bandeja de **Pagos a proveedor** con Saldo = elegible pendiente | ~5 s |
| 5 | **Registrar pago** (paso 7) | `tesoreria.pago-factura-proveedor.aplicado.v1` (uno **por factura**) | Tesorería → **CxP** | Factura **Pagada** (Pagado 1,740; Saldo 0) | segundos |
| 6 | (consecuencia del 5) | `cuentas_por_pagar.factura.pago-aplicado.v1` (acumulado por OC ya calculado por CxP) | CxP → **Compras** | Compras: sub-estado **Pago**; cierre/reapertura automática de la OC cuando los 3 sub-estados llegan a Completa | < 1 min |
| — | *Si se hubiera enviado a revisión o cancelado* | `cuentas_por_pagar.pasivo.retirado-de-pago.v1` | CxP → Tesorería | El pasivo sale de la bandeja; pagar da `PASIVO_NO_AUTORIZADO_CXP` | — |
| — | *Solicitar cancelación en Tesorería* | `tesoreria.cancelacion-pasivo.solicitada.v1` | Tesorería → CxP | CxP la manda a revisión | — |
| — | *Reversa de pago (no se hace en la demo)* | `tesoreria.pago-factura-proveedor.revertido.v1` | Tesorería → CxP | Pago revertido con contramovimiento; el pasivo vuelve a la bandeja | — |

Nombres canónicos de la triada (CLAUDE.md): `OcRecepcionRegistradaEvent`, `FacturaProveedorRegistradaEvent`, `FacturaProveedorAutorizadaEvent`, `PasivoAutorizadoParaPagoEvent`, `PagoFacturaProveedorEvent`. **El cierre de la OC lo calcula Compras** combinando Recepción + Facturación + Pago; en la demo queda abierta porque todo está en "parcial".

---

## Problemas conocidos y cómo esquivarlos en la demo

| # | Problema | Cómo esquivarlo |
|---|---|---|
| 1 | **El panel "Capturar factura desde OC" no se desplaza a 1440x900** y el botón "Capturar factura" queda inalcanzable (el formulario mide ~1,270 px; el botón estaba en y=1220). Causa probable: `frontend/src/features/cxp/components/CapturarFacturaSheet.tsx`, `<form className="grid grid-cols-1 gap-4 px-4 lg:grid-cols-[260px_1fr]">` sin scroll interno | **Corregido** en la rama `fix/demo-12oct-correcciones` . Solo si ejecutas `main` sin ese commit: zoom del navegador a 67 % o ventana de ≥ 1350 px de alto |
| 2 | Texto rojo "Selecciona la línea de OC que corresponde a este concepto." sigue visible tras elegir la línea | Ignorarlo; no impide capturar |
| 3 | **Reportes Cartera y Antigüedad: 422 `CXP_HISTORICO_POR_CONFIRMAR`** sin filtro de proveedor. Mensaje: "No se pudo cargar el reporte. Por confirmar: hay aplicaciones anteriores a P8 sin fecha reconstruible…". Causa: la factura `MOD-1002` (Herrajes Industriales del Bajío) tiene `importe_pagado = 3,873.2400` pero ningún renglón de pago en `cuentas_por_pagar.movimientos_pasivo` (dato sembrado antes; `SaldosHistoricos.VerificarHistorial`) | **Siempre filtrar por proveedor `DEMO-PROV-ACT`**. Antes de la demo conviene reparar o retirar ese dato (no se hizo: no se fuerzan pagos ni saldos por SQL) |
| 4 | ~~En el detalle de OC la fila "Recepción" quedaba oculta bajo la barra superior y decía "~50 %" fijo~~ **Corregido** en la rama : ahora muestra el avance real y la fila se ve completa | Solo si ejecutas `main` sin las correcciones de esta rama: usar la pestaña **Líneas** (`09b`) y la **Trazabilidad** (`09c`) |
| 5 | **"Autorizar" actúa de inmediato**, sin diálogo ni evidencia y sin vuelta atrás desde esa pantalla | Avisarlo antes de pulsarlo |
| 6 | Tesorería muestra el folio sin serie (`7001`, no `DEMO-7001`) y el pago **no tiene folio propio** (solo la referencia tecleada) | Identificar el pago por `SPEI-DEMO-0001` |
| 7 | `DEMO-CTA-MXN` con **saldo negativo** (-4,704.38 → -6,444.38); el sistema permite pagar sin fondos. Los 13 conceptos de flujo dicen "(ejemplo por validar)" | Opcional (no hecho): Tesorería > Movimientos bancarios > **Registrar ingreso** para dejar saldo positivo |
| 8 | La CLABE ficticia de 18 ceros se acepta: no hay validación de dígito verificador en la CLABE del proveedor | No bloquea |
| 9 | Los toasts verdes tapan la barra superior (Sucursal, Nuevo) ~8 s | Esperar antes de capturar pantalla |
| 10 | El XML trae `MetodoPago="PPD"` (pago diferido): exige complemento de pago (REPP) del proveedor y, si pasan más de 5 días hábiles tras pagar sin REPP, la tarea `RevisarFaltaRepp` manda la factura a revisión con motivo `FALTA_REPP` *(del código; no se observó, la demo dura un día; que esa tarea esté programada en demo, no verificado)* | No afecta el recorrido del mismo día. No hace falta tocar el XML |

Errores HTTP del API durante todo el recorrido: solo los dos 422 del problema 3. Ningún aviso "Requiere el permiso X". No se probaron Luis Ibarra (`cxp-demo`) ni Dorio (`tesoreria-demo`): este bloque lo hace Geovany.

---

## 10. El CFDI de ejemplo (`DEMO-7001.xml`): qué atributos importan y por qué

Archivo: `.verification/demo-tour/cxp-tesoreria/DEMO-7001.xml`, hecho a mano con la plantilla de los CFDI demo ya cargados. **Sin sello real**: `SelloCFD="x"` y `SelloSAT="x"`; el parser no verifica sellos ni XSD ("confía en que el XML que llega ya fue timbrado", los verifica el SAT).

| Dónde en el XML | Atributo | Valor en la demo | Por qué importa |
|---|---|---|---|
| `cfdi:Comprobante` | `Version` | `4.0` | **Solo se soporta 4.0** (`CFDI_VERSION_NO_SOPORTADA`) |
| `cfdi:Comprobante` | `TipoDeComprobante` | `I` (**Ingreso**) | Una factura de proveedor es CFDI de Ingreso. El selector de factura y el backend exigen tipo Ingreso (`CFDI_NO_UTILIZABLE`). `E` (Egreso) = nota de crédito; `P` = complemento de pago |
| `cfdi:Comprobante` | `Serie` / `Folio` | `DEMO` / `7001` | Pasan a Serie y Folio de la factura (`DEMO-7001`) |
| `cfdi:Comprobante` | `Fecha` | `2026-10-12T08:00:00` | Fecha documento. Formato `yyyy-MM-ddTHH:mm:ss` (`CFDI_FECHA_INVALIDA`) |
| `cfdi:Comprobante` | `Moneda` | `MXN` | Debe ser igual a la moneda de la OC y de la factura (`OC_MONEDA_DISTINTA`, `CFDI_NO_UTILIZABLE`). Si falta, el parser asume MXN |
| `cfdi:Comprobante` | `TipoCambio` | **no se pone** (MXN) | Solo se pone para moneda extranjera. El backend exige que el TC de la factura = el del XML (`CFDI_TIPO_CAMBIO_DISTINTO`); vacío cuenta como 1 |
| `cfdi:Comprobante` | `SubTotal` / `Total` | `1500.00` / `1740.00` | Se comparan contra lo capturado (tolerancia) y se autollenan. Total = SubTotal + IVA − retenciones − descuento |
| `cfdi:Comprobante` | `MetodoPago` | `PPD` | Se guarda en la factura (ver problema 10) |
| `cfdi:Comprobante` | `FormaPago`, `Exportacion`, `LugarExpedicion` | `99`, `01`, `42501` | **(razón no verificada)**: el parser no los lee; son los de los CFDI demo previos |
| `cfdi:Emisor` | **`Rfc`** | **`XAXX010101000`** | **Debe ser igual al RFC del proveedor de la OC** (`DEMO-PROV-ACT`). Si no, `CFDI_NO_UTILIZABLE`. Formato SAT de 12 (moral) o 13 (física) caracteres (`RFC_FORMATO_INVALIDO`) |
| `cfdi:Emisor` | `Nombre` / `RegimenFiscal` | `DEMO-PROV-ACT Suministros ficticios` / `601` | Informativos |
| `cfdi:Receptor` | `Rfc` | `TMP010101AAA` | La carga manual **no lo compara con la empresa**; una NC adjunta sí debe tener el mismo receptor que la factura (`NC_DATOS_DISTINTOS`). Por qué ese valor: **(razón no verificada)**, es el de los CFDI demo previos |
| `cfdi:Concepto` | `NoIdentificacion`, `Cantidad`, `ClaveUnidad`, `ValorUnitario`, `Importe` | `ART-CRT-01`, `6.00`, `H87`, `250.00`, `1500.00` | Se importan a la línea de la factura. Cantidad y unidad **deben coincidir exactamente**; precio e importe dentro de tolerancia. `6.00` = lo recibido; `250.00` = precio de la OC |
| `cfdi:Impuestos` | `TotalImpuestosTrasladados` | `240.00` | IVA 16 % (Traslado `Impuesto="002"`, `TasaOCuota="0.160000"`, `Base="1500.00"`) |
| `cfdi:Impuestos` | `TotalImpuestosRetenidos` | no se pone | Retenciones = 0 |
| **`tfd:TimbreFiscalDigital`** | **`UUID`** | **`3B8E5C21-7A94-4D6F-8E10-5F2C9A4B6D73`** | **Es el identificador fiscal único.** Formato GUID 8-4-4-4-12 hex (`UUID_CFDI_FORMATO_INVALIDO`). Se **normaliza a mayúsculas** y se **deduplica**: un UUID ya cargado da `CFDI_DUPLICADO`; uno ya usado en factura/NC/anticipo da `CXP_UUID_DUPLICADO`. Distinto al de la recepción |
| `tfd:TimbreFiscalDigital` | `FechaTimbrado`, `RfcProvCertif`, `NoCertificadoSAT` | `2026-10-12T08:00:05`, `AAA010101AAA`, … | Sin uso; presentes para que el XML luzca timbrado |

**Por qué el formulario de factura no pide UUID:** el UUID **no es un dato de captura sino un dato fiscal del XML**. La factura se liga al CFDI ya cargado (`cfdiRecibidoId` + `uuidCfdi` se copian al vincularlo), el backend relee el XML guardado y verifica que el UUID, los importes y las líneas coinciden (`CFDI_UUID_DISTINTO`), y el UUID queda consumido para siempre (anti doble pago y anti doble contabilización).

---

## 11. Valores alternativos: qué puede cambiar Eliam y qué NO

### Se puede cambiar sin romper nada

| Valor | Alternativa válida | Condición |
|---|---|---|
| **UUID del CFDI** | Cualquier GUID nuevo en mayúsculas (8-4-4-4-12 hex), p. ej. `4C9F7A10-2B3D-4E5F-9A61-7D8E0B1C2A34` | **Obligatorio cambiarlo si se repite la captura sobre la misma base** (se deduplica por UUID) |
| **Serie y folio** (`DEMO` / `7001`) | Folio nuevo (`7002`, …) | Son informativos, pero conviene que sean únicos para no confundir facturas en las bandejas; el folio del XML = el de la factura |
| **Fecha del CFDI** | Hoy u otra fecha en periodo abierto | La **fecha de contabilización** del panel es "hoy": **debe caer en un periodo abierto en la laptop de Eliam** (si su reloj marca otro mes: `CXP_PERIODO_CERRADO`); el **vencimiento** debe ser ≥ fecha documento |
| **Fecha de vencimiento** | Cualquier fecha ≥ fecha documento | La de la demo (30 días) viene de las condiciones de la OC, no del código |
| **Referencia bancaria** | `SPEI-DEMO-0002`, etc. (máx. 120) | Solo identifica el pago |
| **Fecha valor** | Hoy u otra fecha en periodo abierto y posterior al corte de saldo inicial | — |
| **Concepto de flujo** | Cualquiera activo, o ninguno | Es opcional; afecta solo el reporte de Flujo de efectivo |
| **Importe a pagar** | Menor al saldo (pago parcial) | Debe ser > 0 y ≤ lo elegible; el resto queda en la bandeja |
| **Obra** y **Concepto fiscal de retención** | Vacíos o cualquier concepto del catálogo | No bloquean; con concepto puede aparecer la nota ámbar de retenciones |
| **Cantidad / precio / total** | Otra cantidad **menor o igual a lo pendiente de la línea de OC**, a **precio de OC**, con **IVA 16 %**, **y el XML con exactamente los mismos números** | Si se factura más de lo recibido, la diferencia queda "Retenida por falta de recepción" (ver fórmula) |

### NO cambiar (rompe el recorrido)

| Valor | Por qué |
|---|---|
| **RFC emisor del XML** (`XAXX010101000`) | Debe ser el RFC del proveedor de la OC (`CFDI_NO_UTILIZABLE`) |
| **Moneda** (`MXN`) | Debe ser la de la OC, del CFDI y de la cuenta de egreso (`OC_MONEDA_DISTINTA`, `PAGO_CROSS_MONEDA`) |
| **OC** (`OC-MID2026-000007`) y **proveedor** (`DEMO-PROV-ACT`) | Autorizada, con recepción, mismo proveedor/sucursal/moneda. Otra OC sin recepción deja lo elegible en 0 |
| **Tipo de comprobante** (`I`) y **versión** (`4.0`) | Solo Ingreso y 4.0 |
| **Totales del XML vs. capturados** | Deben cuadrar con la tolerancia (0.99 MXN); si no, la factura se cancela sola |
| **Número de líneas, cantidad y unidad** del XML | Se comparan uno a uno contra la factura |
| **Periodo contable** | Debe estar abierto para la fecha de contabilización y la fecha valor |
| **Cuenta de egreso** | Debe ser MXN y activa |
| **No editar el XML después de cargarlo** | El backend relee el XML guardado y lo compara con el UUID y el RFC |
| **Orden de pasos** | Recepción → cargar XML → capturar → autorizar → esperar ~5 s → pagar. Pagar antes de que llegue el evento: `PAGO_PASIVO_NO_AUTORIZADO` |
