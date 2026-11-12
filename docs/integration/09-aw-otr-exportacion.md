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

## Lectura real: tablas del módulo OTR

La base **`OTR`** está en la misma instancia que `MILMAIN` (SER-DATA\AWBUSINESS). TI confirmó (correo 5-nov)
`SELECT` para `millet_erp_lector_aw` sobre tres tablas de esquema `SYSADM`; las tablas por pedido siguen
saliendo de `MILMAIN.SYSADM.BW_AUFTR_KOPF`. Verificado con `SELECT` real el 5-nov:

| Tabla | Qué trae | Uso en el ERP |
|---|---|---|
| `OTR.SYSADM.OTR_ROUTES` (5,079 rutas) | `ROUTE_ID`, `ROUTE_NAME`, `ROUTE_STATUS`, `DELIVERY_DATE`, `DATETIME_UPDATE` | Ruta, fecha prevista, estatus y versión |
| `OTR.SYSADM.OTR_CONTAINERS_ACTIONS` | Una fila por pirámide de carga (`CONTAINER_ID`, tipo «PIRAMIDE A.2»; **no** es contenedor marítimo), destino (`LOCATION_NAME1`, `LOCATION_ADDRESS_TEXT`), `DATETIME_UPDATE` | Dirección de entrega, versión. `CUSTOMER_CODE`, `DOCUMENT_NUMBER` e `INVOICE_NUMBER` vienen vacíos en las 48,326 filas |
| `OTR.SYSADM.OTR_CONTAINER_POSITIONS` (430,253 posiciones) | `ROUTE_ID`, `CONTAINER_ID`, `DOCUMENT_ID` (texto = `BW_AUFTR_KOPF.ID`), `POS_NR`, `POS_QTY`, `POS_TOTAL_WEIGHT`, `DATETIME_UPDATE` | Líneas (pedido, posición, cantidad), piezas y peso |
| `MILMAIN.SYSADM.BW_AUFTR_KOPF` | `ID`, `AH_IDENT` (cliente A+W), `OR_AVBEREICH` (clave de sucursal), `STATUS` | Cliente y sucursal del envío; pedido existente |

Booking y BL **no existen en OTR**: los captura Comercio Exterior en el ERP y no se mandan a OTR.
Los renglones de `ROWSTATUS` valen siempre 0 y `REJECTED` siempre 0: no distinguen nada.

### Lector real (A+W-QA-10)

`OtrTablasAdapter` (origen Real) hace sólo `SELECT`, con nombres de tres partes (`OTR.SYSADM.*`, `MILMAIN.SYSADM.*`) porque
las dos bases viven en la misma instancia: **no usa `OPENQUERY`** (sí lo usa producto terminado, que va a otra instancia por
`AWPROD_ERP`). Usa la conexión de sólo lectura `ConnectionStrings:AwClientesDb` (`millet_erp_lector_aw`, TLS verificado); ya no usa
`AwIntegracionDb`. No escribe nada en A+W: `MarcarResultadoAsync` no hace nada en Real.

**Un envío = una pirámide** (`ROUTE_ID` + `CONTAINER_ID`); `IdOrigen = "<ROUTE_ID>-<CONTAINER_ID>"`.

| Campo del contrato | Origen en OTR |
|---|---|
| `cliente_numero_aw` | `BW_AUFTR_KOPF.AH_IDENT` de los pedidos de la pirámide |
| `sucursal_clave_aw` | `BW_AUFTR_KOPF.OR_AVBEREICH` |
| `cliente` / `direccion_entrega` | `LOCATION_NAME1` / `LOCATION_ADDRESS_TEXT` (informativos) |
| `numero_contenedor` / `ruta` | `CONTAINER_ID` / `ROUTE_NAME` |
| `bultos` | `SUM(POS_QTY)` (piezas de la pirámide) — supuesto, por confirmar con Comercio Exterior |
| `peso_kg` | `SUM(POS_TOTAL_WEIGHT)` (no incluye la tara de la pirámide) |
| `fecha_carga_prevista` | `OTR_ROUTES.DELIVERY_DATE` |
| líneas | por `(DOCUMENT_ID → numero_pedido, POS_NR → posicion)` con `SUM(POS_QTY)`; unidad `UnidadLinea` |
| `version` | máx. `DATETIME_UPDATE` de la ruta, la pirámide y sus posiciones, en milisegundos Unix |

`DOCUMENT_ID` se convierte con `TRY_CONVERT(int, …)` (TI usa `CONVERT(INT, …)` en `CONSULTA_PEDIDOS_ESCAN_OTR_ALCIM`). Cuentan sólo las
posiciones cuyo pedido existe y tiene `STATUS` menor a `EstatusPedidoMenorA` (900, la condición de la consulta de TI).
Con datos del 5-nov: 27,934 envíos con ruta distinta de 50; la consulta tarda unos 3 s.

**Qué ya leyó el ERP.** Como no se puede marcar nada en A+W, el ERP decide con su control propio: un envío con versión menor o igual a la
aplicada (`otr_envios.VersionAplicada`) o con esa versión rechazada (`otr_entrada_control`) no se vuelve a procesar; los errores técnicos
se reintentan hasta `MaxIntentos`. Un cambio llega como una versión mayor (`DATETIME_UPDATE` más reciente). **Límites:** (1) una posición
que se *borra* en OTR no cambia ningún `DATETIME_UPDATE` y no se detecta; (2) si se vuelve a guardar la ruta, todas sus pirámides suben de
versión aunque su contenido sea el mismo; (3) 45 pirámides mezclan clientes y 523 mezclan sucursales: se rechazan con
`CE_OTR_VARIOS_CLIENTES` / `CE_OTR_VARIAS_SUCURSALES` (no se escoge una al azar); decisión pendiente con Comercio Exterior.

**Estatus de ruta (`ROUTE_STATUS`) y cancelación.** Lista de TI (Jorge, 5-nov) y regla de Comercio Exterior (Guadalupe Polanco, 6-nov):

| Estatus | Significado | Tratamiento en el ERP |
|---|---|---|
| 1 | Creada | Se lee (no se excluye en silencio). Pregunta abierta a Comercio Exterior: ¿una ruta recién creada ya debe entrar al ERP o sólo desde 50/150? |
| 50 | Edición | Excluida (`EstatusRutaExcluidos`) |
| 100, 150, 200, 250, 350, 500, 800 | Etapas del flujo (150 = organizado, 200 = liberado) | Se leen |
| 850/851, 860-880 | Etapas posteriores | Se leen |
| 890 | **Cancelado** | Genera `Cancelacion` (`EstatusRutaCancelada`, por omisión `890`) |
| 900, 950, 1000 | Etapas finales | Se leen |

Regla del 890 (Guadalupe Polanco, 6-nov): en OTR no se modifica un envío cancelado, se cancela y se crea uno nuevo; el ERP trata la cancelación y el alta nueva como envíos distintos. No existe diccionario de OTR; booking y BL viven sólo en el ERP (Janette Niquete). `EstatusRutaExcluidos` (por omisión `50`) y `EstatusRutaCancelada` (por omisión `890`) siguen siendo opciones. Una ruta que sale del conjunto leído (por ejemplo, vuelve a 50) no se interpreta como cancelación.

**Supuestos por validar en UAT con Comercio Exterior:** `bultos` = `SUM(POS_QTY)` y unidad de línea `H87` no están confirmados por Millet.

## Contrato de entrada (origen Demo)

Las tablas siguientes son el **origen Demo de ejemplo** (`tools/aw-origen-demo`) y el formato interno que
el lector Real arma a partir de las tablas de OTR (ver arriba). En real no se escribe `erp_resultado` en A+W.

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

La bandeja del lanzador inicia en Por vincular y permite consultar también Vinculados y Cancelados.

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
| `EstatusRutaExcluidos` | `50` | Real: estatus de ruta que no se leen (coma). 50 = edición (TI) |
| `EstatusRutaCancelada` | `890` | Real: estatus que cancelan el envío. 890 = Cancelado (TI 5-nov, Comercio Exterior 6-nov) |
| `EstatusPedidoMenorA` | 900 | Real: sólo posiciones cuyo pedido A+W tenga `STATUS` menor |
| `UnidadLinea` | `H87` | Real (supuesto por validar en UAT): unidad de las líneas; debe coincidir con la del pedido en el ERP |
| `EntregaDesde` | vacío | Real: sólo rutas con `DELIVERY_DATE` igual o posterior (`yyyy-MM-dd`) |

Real se registra únicamente con contrato firmado, conexión `AwClientesDb` y
`EntradaHabilitada`; lee en sólo lectura (ver «Lector real») y se enciende en pruebas con las opciones `IntegracionesAw__Exportacion__*`. `Demo` usa la misma selección `AwOrigenSesion`, registro keyed `Demo`
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

## Sucursal por clave A+W (alias) y dirección de entrega

- `compartido.sucursales.clave_aw` admite varias claves separadas por coma (la primera es la principal, el resto alias, máx. 40 caracteres en total). Se edita donde ya se edita `ClaveAw` (crear/actualizar sucursal). Una clave no puede repetirse entre sucursales. Respuesta de TI (30-oct): Conkal = `CONKAL,PERIFERICO,OBRAS`; Circuito = `CIRCUITO`; Chichí Suárez = `COTTOLENGO`; Cancún = `CANCUN`.
- `REPOSICIONES`, `REEMPLAZOS`, `REPOSICIÓN CON COSTO`, `RETRABAJO`, `MUESTRAS` **no son sucursales**: no se configuran como alias y el envío queda `PorVincular` con `CE_OTR_SUCURSAL_DESCONOCIDA`. Pendiente de decisión de administración: heredar la sucursal del pedido original.
- La dirección de entrega sale de `LOCATION_ADDRESS_TEXT` y, si viene vacía, de `LOCATION_ADDRESS`. Antes solo se usaba la primera y, al venir vacía, el envío se rechazaba con `CE_OTR_DATOS_INVALIDOS`.


### Revalidar una equivalencia de sucursal en un envío ya leído

La consulta de la bandeja sin expediente devuelve los tres estados OTR; conserva el aislamiento por empresa y sucursal. Las pestañas filtran esos estados en el cliente. Con `expedienteId`, sólo devuelve los envíos de ese expediente y exige acceso a su sucursal.

`GET /api/v1/comercio-exterior/otr/bandeja` es la lectura ligera del lanzador: devuelve sólo cabeceras, versiones ERP/A+W, estado, motivo y datos de cliente/entrega/contenedor. No carga ni transfiere partidas o conflictos históricos, tampoco las líneas dentro del contenido de origen. Conserva el permiso de lectura y los mismos filtros de empresa/sucursal que la consulta completa. La consulta `GET /otr?expedienteId=...` conserva el detalle completo y sus controles de acceso; vinculación y revalidación siguen enviando la versión ERP de la cabecera mediante `If-Match`. Publicar la API antes del frontend que consume la ruta nueva.

`POST /api/v1/comercio-exterior/otr/{id}/revalidar-sucursal` vuelve a resolver la clave A+W guardada contra el catálogo de sucursales activas. Usa el permiso de gestión OTR, el alcance corporativo de gestión para el registro sin sucursal, `Idempotency-Key` y `If-Match` con la **versión ERP**, no la marca de tiempo A+W. También comprueba el acceso de gestión a la sucursal resuelta.

Sólo se aplica a un envío `PorVincular`, con cliente reconocido, sucursal vacía y sin expediente ni contenedor. No recibe una sucursal elegida por el solicitante. Sin equivalencia activa se rechaza y conserva el bloqueo. Los vinculados, cancelados o ya resueltos no se reasignan.

La revalidación actualiza la sucursal local y reevalúa el motivo de las partidas vigentes. Conserva la versión fuente, el contenido y su hash, los IDs/cantidades de las partidas, los conflictos y el control de lectura. El cambio local incrementa la versión ERP y queda en la auditoría existente. No crea un expediente, no vincula ni confirma partidas y no escribe en A+W.

Es una acción explícita: abrir la bandeja, reiniciar el worker o desplegar el código no revalida automáticamente el histórico. Antes de recuperar un lote en QA, obtener una vista previa de sólo lectura con IDs, versiones ERP, clave A+W y equivalencia propuesta, autorizar su aplicación y volver a comprobar las versiones; no borrar controles ni inventar una nueva versión de origen.
