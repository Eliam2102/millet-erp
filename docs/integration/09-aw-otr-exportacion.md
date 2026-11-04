# 09 · A+W OTR y exportación (flujo 3)

> **Propuesta para el contrato I13.4 · no aplicar sin acuerdo.**
> Propiedad de datos: **propuesta VILO, PEN-CE-025**, pendiente de validar con Millet y Jorge.
> Implementación local y Demo no acreditan conexión con OTR, UAT ni aceptación de Millet.

A+W OTR acomoda piezas y cajas en contenedores y define rutas de entrega (SRC-089 §1.0,
05-oct-2026). «OTR no es una compra» (respuesta Millet, 05-oct). No crea requisiciones,
OC ni proveedores. La compra extranjera conserva requisición → OC → expediente de importación.
La «Orden de Transporte» del glosario de CxP es un concepto distinto.

## Frontera y dependencias

El flujo 3 vive en `Integraciones.Aw/Infrastructure/Exportacion/`. Implementa el puerto
`IOtrEntradaReader` definido por Comercio Exterior. La dependencia es
`Integraciones.Aw → ComercioExterior`; CE no referencia A+W. No usa `AwSolicitudesWorker`,
`IngestaControl`, EDI, drop service ni consumidores de eventos propios en Service Bus.

**Modelo vigente (4-nov-2026).** Jorge (TI Millet, correo del 1-nov, punto 2): en A+W OTR no se
pueden agregar eventos ni personalizaciones, así que A+W no escribe en tablas puente ni lee lo que
el ERP deje. Por eso:

- El ERP **sólo lee** las tablas del módulo OTR, en modo de sólo lectura, con el usuario
  `millet_erp_lector_aw` (igual que producto terminado por `AWPROD_ERP`). El lector real va en
  A+W-QA-10; ver «Lectura real» abajo.
- Las tablas puente `aw_otr_envio`, `aw_otr_envio_linea` y `erp_otr_estado` **no existen en
  `MILLET_INTEGRACION`**. Los scripts 12 y 13 y el usuario `aw_otr` se retiraron.
- El ERP **no escribe nada hacia A+W**. Booking, BL, embarque real, estado aduanal, entrega y
  cierre se quedan en el expediente de exportación.

## Lectura real: tablas del módulo OTR (pendiente de Jorge)

Revisión del 4-nov con `millet_erp_lector_aw`: el usuario sólo ve las tablas que tiene concedidas
(11 en `MILMAIN`, 5 de `alcimdb` por `AWPROD_ERP`); ninguna es del módulo OTR. El encabezado del
pedido, que ya se puede leer, cubre parte de la regla R8:

| Dato R8 | Tabla y columna | Observación (pedidos 2026) |
|---|---|---|
| Pedido | `SYSADM.BW_AUFTR_KOPF` (número de pedido) | — |
| Cliente | `BW_AUFTR_KOPF` → `KU_KUNDEN` | Ya se lee para clientes |
| Ruta | `BW_AUFTR_KOPF.OR_TOUR` | MERIDA, FLORIDA, CANCUN, ONTARIO… |
| Línea y tránsito | `BW_AUFTR_KOPF.OR_ZOLLTOUR` | Exportación: p. ej. FLORIDA → «LINEA PENINSULAR, T/T 08» |
| Peso | `BW_AUFTR_KOPF.SU_GEWICHT`, `SU_GEWICHT_TARA` | Casi todos los pedidos |
| Fecha prevista / real | `BW_AUFTR_KOPF.DATUM_LIEFER_PLAN`, `DATUM_LIEFER_TAT` | Casi todos |
| Remisión | `BW_AUFTR_KOPF.NR_LIEFERSCHEIN` | — |
| Empaque | `BW_AUFTR_KOPF.OR_VERPACKUNG` | — |
| Camión | `BW_AUFTR_KOPF.OR_LKW` | Casi siempre `<indf>` |
| Dirección de entrega | Por confirmar | — |
| **Contenedor** | **No encontrado** | Pedir a Jorge |
| **Bultos** | **No encontrado** | Pedir a Jorge |
| **Envío o carga** (qué pedidos y posiciones van juntos) | **No encontrado** | Pedir a Jorge |

Falta de Jorge: base, instancia y tablas del módulo OTR donde están contenedor, bultos y la carga,
cómo se detecta un envío nuevo, modificado o cancelado, y `SELECT` sobre ellas para
`millet_erp_lector_aw`.

## Contrato de entrada (origen Demo)

Las tablas siguientes son el **origen Demo de ejemplo** (`tools/aw-origen-demo`) y el formato que
el lector real de A+W-QA-10 debe entregar al ERP. En real no se escribe `erp_resultado` en A+W.

A+W inserta cabecera y todas sus líneas **en una sola transacción**, de modo que el lector
nunca encuentre un envío publicado a medias. Cada versión es inmutable; los cambios se
insertan con una versión estrictamente mayor. Las cancelaciones son operaciones explícitas.

`dbo.aw_otr_envio` (SQL Server; en Demo cambian UUID/timestamp a `uuid`/`timestamptz`):

| Columna | Tipo | Quién escribe / significado |
|---|---|---|
| `otr_envio_id` | nvarchar(100), requerido | A+W; identificador estable del envío |
| `version` | bigint > 0 | A+W; creciente por envío; PK compuesta `(otr_envio_id, version)` |
| `cliente_numero_aw`, `sucursal_clave_aw` | nvarchar(50), requeridos | A+W; referencias propias. ERP resuelve cliente por número A+W y sucursal por clave A+W con los puertos existentes de Facturación. |
| `cliente` | nvarchar(200), requerido | A+W; nombre informativo, no crea un cliente |
| `direccion_entrega` | nvarchar(1000), requerido | A+W; dirección informativa |
| `producto` | nvarchar(500), opcional | A+W; descripción informativa |
| `numero_contenedor`, `ruta` | nvarchar(200), opcionales | A+W; datos de transporte |
| `bultos` | int >= 0, opcional | A+W |
| `peso_kg` | decimal(18,6) >= 0, opcional | A+W |
| `fecha_carga_prevista` | date, opcional | A+W |
| `booking`, `bl_guia` | nvarchar(200), opcionales | A+W sólo informa una propuesta; son **propiedad ERP**, las diferencias generan conflicto |
| `operacion` | nvarchar(20), requerida | A+W: `Alta`, `Modificacion`, `Cancelacion` |
| `creada_at` | datetimeoffset(3) | Default de la tabla al insertar A+W |
| `erp_resultado` | nvarchar(30), opcional | **Sólo ERP**: NULL pendiente, `Aplicada`, `Aplicada (idempotente)`, `Error`, `Rechazada` |
| `erp_motivo` | nvarchar(500), opcional | **Sólo ERP**: código de rechazo |
| `erp_procesada_at` | datetimeoffset(3), opcional | **Sólo ERP**: fecha de resultado |

`dbo.aw_otr_envio_linea`, insertada por A+W, sólo SELECT para ERP:

| Columna | Tipo | Significado |
|---|---|---|
| `otr_envio_id`, `version` | nvarchar(100), bigint | FK a cabecera |
| `numero_pedido` | nvarchar(50) | Número A+W, no ID interno ERP |
| `posicion` | int > 0 | Posición del pedido |
| `cantidad` | decimal(18,6) > 0 | Cantidad propuesta |
| `unidad` | nvarchar(20) | Debe coincidir con la unidad de la posición del ERP |

PK de línea: `(otr_envio_id, version, numero_pedido, posicion)`.
El índice de barrido selecciona `erp_resultado IS NULL` o `Error`, orden por id y versión. Cada fila tiene control local de intentos: un error técnico informa `CE_OTR_ERROR_TECNICO` y mensaje recortado a 500 caracteres, sin detener el lote; al alcanzar `MaxIntentos` queda `Rechazada`. Los rechazos de negocio son definitivos.

## Aplicación, vínculo y conflictos

- La identidad local es única por `(empresa, id de origen)`. Versión menor o misma versión con el mismo hash: `Aplicada (idempotente)`. Misma versión con hash distinto: `Rechazada`, `CE_OTR_VERSION_CONTENIDO_DISTINTO`; conserva el contenido previamente aplicado. El SHA-256 corresponde al contenido estructurado persistido.
- Cliente o sucursal sin equivalencia: el envío queda por vincular con `CE_OTR_CLIENTE_DESCONOCIDO` o `CE_OTR_SUCURSAL_DESCONOCIDA`; no se crean datos maestros. Se requiere corregir el catálogo y publicar una nueva versión.
- Envío sin expediente: bandeja «Envíos OTR por vincular». CE lo vincula a una exportación
  abierta del mismo cliente y sucursal o abre una nueva con DDP propuesto y validación de
  responsable/moneda. Un vínculo existente no se puede mover a otro expediente.
- La consulta `IPedidosEmbarcablesReadPort.BuscarPorNumeroAsync` conserva Facturación como
  fuente. Pedido ausente: `CE_OTR_PEDIDO_INEXISTENTE`, línea Pendiente. Más de un pedido con el mismo número A+W: `CE_OTR_PEDIDO_AMBIGUO`, línea Pendiente. K3–K5 conservan
  sus códigos (`CE_CANTIDAD_EXCEDE_PEDIDO`, `CE_PEDIDO_NO_ASIGNABLE`,
  `CE_SUCURSAL_DISTINTA`, `CE_PEDIDO_CON_EXPEDIENTE`). Unidad diferente:
  `CE_OTR_UNIDAD_DISTINTA`. No se provisionan pedidos ni datos maestros.
- Una partida propuesta reserva cantidad bajo K3 y queda `RequiereRevision` en CE hasta
  su confirmación explícita. Una nueva versión conserva las asignaciones anteriores y las
  pone en revisión; no duplica cantidades. La nueva línea queda Pendiente con
  `CE_OTR_PARTIDA_EN_REVISION` mientras exista asignación anterior de esa posición.
- OTR controla número, ruta, bultos, peso y carga prevista; ERP controla booking, BL,
  pedimento, embarque real e hitos. Sello permanece capturado por ERP. Una edición manual
  de un campo OTR guarda actor y fecha; las diferencias posteriores conservan el ERP y
  registran ambos valores, versión OTR y recepción. Las asignaciones de piezas editadas
  manualmente también guardan protección por pedido/posición. Si difieren de lo recibido,
  quedan Pendientes con `CE_OTR_CONFLICTO_PARTIDA`. «Usar OTR» reemplaza esa asignación
  bajo K3–K5 y la deja en revisión; una factura vigente impide retirar la partida. «Conservar
  ERP» mantiene las cantidades manuales y audita la decisión.
- Una versión nueva cierra conflictos abiertos anteriores como `Superado`, con `SuperadoPorVersion`; dejan de contar como pendientes y conservan su evidencia.
- Resolver exige `comercio_exterior.otr.gestionar`, versión vigente y motivo de 5–500
  caracteres. «Usar OTR» aplica el valor con las validaciones de CE-02; «Conservar ERP»
  conserva la protección manual. Se auditan actor, fecha, decisión y motivo. Una decisión
  sobre un valor ERP ya modificado o versión antigua se rechaza
  (`CE_OTR_CONFLICTO_DESACTUALIZADO`); no hay sobrescritura silenciosa.
- Cancelación de origen: vínculo Cancelado, partidas en revisión; pedidos y facturas se
  conservan. Un id cancelado no se reactiva (`CE_OTR_CANCELADO`); se requiere un nuevo id.
- Cierre financiero sigue bloqueado por CE-03 (`CE_CIERRE_FINANCIERO_PENDIENTE`). El
  cierre no se informa a A+W (ver «Regreso a A+W: no aplica»).

## Regreso a A+W: no aplica

Hasta el 4-nov el ERP generaba salidas (`otr_salidas`) y las entregaba a `erp_otr_estado` para que
A+W las acusara. Se retiró: A+W no lee esa tabla. La tabla `comercio_exterior.otr_salidas` se
conserva sólo como historial y ya no crece. Si Millet necesita ver el booking en OTR, iría como un
tipo nuevo en `aw_retorno`, que aplica el servicio de Windows de TI (fuera de alcance hoy).

La bandeja del lanzador contiene sólo los envíos por vincular.

## Encendido, Demo y operación

`IntegracionesAw:Exportacion`:

| Clave | Omisión | Regla |
|---|---|---|
| `EntradaHabilitada` | false | Independiente de flujos 1/2 |
| `EmpresaId` | null | Vacío o Guid.Empty: no procesa ni abre conexiones |
| `IntervalSeconds` | 120 | > 0; espera inicial antes del primer barrido |
| `BatchSize` | 50 | 1–1000 |
| `MaxIntentos` | 5 | > 0, errores técnicos de entrada |
| `ContratoI134Firmado` | false | Candado real adicional; activar sólo con evidencia de I13.4 firmado |

Real se registra únicamente con contrato firmado, conexión `AwIntegracionDb` y
`EntradaHabilitada`; hoy apunta al modelo de tablas puente y queda apagado hasta A+W-QA-10. `Demo` usa la misma selección `AwOrigenSesion`, registro keyed `Demo`
y `AwOrigenPgDb`; requiere `IntegracionesAw:OrigenDemo:Permitido`, y nunca en Production.
No hay autenticación simulada: HTTP conserva EntraId + JWT del API y filtro territorial CE.
Ver requiere `expedientes.leer`; gestionar usa `otr.gestionar` y alcance CE por sucursal.

Los scripts 12 (tablas puente) y 13 (permisos y usuario `aw_otr`) se retiraron el 4-nov: Jorge no
los corrió y el modelo no aplica. Para OTR no se crea nada en `MILLET_INTEGRACION`.

Demo sintética: `tools/aw-origen-demo/schema.sql` y `otr.sql`. Después de preparar el origen,
`aw-origen-demo.sh otr` carga dos envíos y la versión 2 del primero (peso 100 → 125).
Use `OTR_SUCURSAL_CLAVE_AW` y `OTR_CLIENTE_NUMERO_AW` si difieren de las referencias A+W del catálogo DEMO. El pedido
`DEMO-PED-EXP-P1` ya tiene cantidades asignadas en CE-02: se respetan K3–K5, y la línea
permanece pendiente hasta corregir/liberar su asignación previa. El segundo usa un pedido
inexistente a propósito. No se eliminan resultados ni controles ERP al resembrar.

Frecuencia real A+W, equivalencias cliente/sucursal, tolerancias de volumen, retención,
responsables de recuperación y firma I13.4: **Por confirmar con Millet y Jorge**.
