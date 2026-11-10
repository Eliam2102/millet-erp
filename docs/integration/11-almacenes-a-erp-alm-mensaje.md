# 11 · Almacenes → ERP (`alm_mensaje`) · alineación con TI del 5-nov

> **Contrato v8 (8-nov) manda sobre las columnas «supuestas» y el reproceso que describe el resto de este documento.** Tablas creadas por TI en la base de la app (`AlmacenRQ_MP`; pruebas `AlmacenRQ_MP_ENSAYO`, misma instancia que MILMAIN), login `millet_erp_integracion`: `alm_mensaje` SELECT + UPDATE solo de `acuse_estado`, `acuse_folio`, `acuse_texto`, `acuse_fecha` (DENY DELETE). Columnas: `id` (BIGINT identity), `mensaje_id` (≤ 64), `intento` (único junto con `mensaje_id`), `tipo`, `referencia`, `fecha`, `sucursal`, `tipo_almacen`, `linea`, `contenido` (JSON), `fecha_escrito` y `acuse_estado` (`RECIBIDO`/`DUPLICADO`/`ERROR`), `acuse_folio` (obligatorio en `REQUISICION_ENVIADA`: el folio del ERP), `acuse_texto` (≤ 1000, el porqué en ERROR), `acuse_fecha`. **Reenvío:** renglón NUEVO con el mismo `mensaje_id` e `intento`+1 (nadie borra); vale el de intento más alto. El ERP lee cada 10 s y acusa cada intento en su renglón. Reglas del lector SQL: solo el último intento de cada mensaje; el orden de una referencia es el del **primer** renglón del mensaje (un reenvío conserva su lugar); un mensaje en ERROR frena a los siguientes de su referencia **solo mientras su último intento siga en ERROR**. Idempotencia: el mismo `mensajeId` ya aplicado → `DUPLICADO` con el mismo folio; un mensaje que quedó en ERROR/EN_REVISION se vuelve a evaluar al llegar su reenvío (nunca se acusa DUPLICADO sobre un ERROR). `tipo_almacen`/`linea`/`sucursal` de las columnas son informativos: el ERP sigue resolviendo por el contenido. Nuevo en v8: `ENTRADA_ALMACEN` (ver «Entrada»); M-446 agrega la línea de producción por almacén (llave `sucursal + tipo + línea`, ver «Mapeo de almacén»), `CAMBIO_ALMACEN` y los avisos `REQUISICION_CANCELADA`/`REQUISICION_REDUCIDA`. No implementado en este flujo: `alm_carga_inicial` (SELECT + UPDATE de `acuse_estado` `APLICADO`/`ERROR`, `acuse_texto`, `acuse_fecha`); queda como trabajo aparte. **Sin probar contra la base real.**

Estado: construido contra origen **simulado** (el adaptador SQL ya sigue el DDL v8, sin probar). Fuentes: contrato v6 (§7.1, §8.3, §8.5), acuerdo 28-oct, correos de Jorge del 29/30-oct, 1-nov y **5-nov 16:49Z** (respuesta íntegra en la bóveda: `Documentos/2026-11-05 correos Millet/`). Complementa a `10-compras-a-almacenes-alm-compra.md` (el sentido contrario).

## Transporte (reemplaza la sección B9 de v6)
- Ya **no hay POST**, ni `X-Mensaje-Id`, ni respuesta en 30 s, ni reintentos a 1/5/15/60 min. El intercambio es por las tablas `alm_mensaje` (la escribe la app; la lee el ERP) y `alm_compra` (la escribe el ERP; ver doc 10). Un usuario SQL por lado con permisos por columna: cada lado escribe lo suyo y ninguno borra.
- El ERP lee `alm_mensaje` cada **10 s** (`Integraciones:AlmMensaje:IntervaloSegundos`, configurable). TI pidió la frecuencia para fijar cuándo dan un mensaje por atorado.
- Lector: `AlmMensajeLector` + `AlmMensajeWorker` (Almacen). **Apagado por omisión** (`Habilitado=false`). Origen detrás de `IAlmMensajeOrigen`: `Simulado` (por omisión, en memoria) o `Sql` (`ConnectionStrings:AlmacenesAppDb`, TLS verificado). Config: `Integraciones:AlmMensaje:{Habilitado=false, Origen=Simulado|Sql, IntervaloSegundos=10, Lote=100, MaxIntentos=5}`.
- El endpoint HTTP `POST /api/v1/integraciones/almacenes/mensajes` queda solo como transporte retirado (404 sin llave configurada): comparte manejador (`AlmacenesAppProcesador`) con el lector, así ambos acusan igual y las pruebas de integración lo siguen usando.

## Columnas de `alm_mensaje` (SUPUESTAS, del correo de Jorge del 29-oct; ajustar al DDL definitivo)
| Columna | Quién escribe | Uso |
|---|---|---|
| `secuencia` | app | llave del renglón y orden |
| `mensaje_id` (único), `tipo`, `version`, `fecha`, `referencia` | app | sobre v1 |
| `sobre` | app | sobre completo en JSON |
| `acuse_estado` (`RECIBIDO`/`DUPLICADO`/`ERROR`), `acuse_folio`, `acuse_error`, `acuse_fecha` | ERP | acuse en el mismo renglón |

Reglas del lector: orden estricto por `secuencia`; el acuse solo se escribe si el renglón sigue sin acuse (dos lectores no se pisan); un **ERROR frena** el resto de su `referencia` hasta que la app reprocese el renglón (borra el acuse y el mismo sobre vuelve a ser pendiente); una falla del ERP (base caída, excepción) no escribe acuse y se reintenta en el siguiente ciclo, y tras `MaxIntentos` seguidos se acusa ERROR genérico para no atorar la fila. El sobre nunca se registra en logs.

## Acuses
`RECIBIDO`, `DUPLICADO` y `ERROR`, cada uno con folio del ERP y texto de error. Un mismo `mensajeId` dos veces → `DUPLICADO` con el mismo folio y sin aplicar nada.
- **Salida sin existencia (cambio 5-nov):** antes se aceptaba (RECIBIDO) y quedaba en revisión. Ahora el acuse es **ERROR** con folio `REV-{mensajeId}` y el motivo («Sin existencia suficiente (…)»); el propio mensaje queda marcado `EN_REVISION` en la bandeja del ERP y los siguientes de la misma referencia esperan. Si la app lo reprocesa (mismo `mensajeId`), se evalúa de nuevo; ya con existencia descuenta y sale de la bandeja.
- Los ERROR de datos (artículo, medida o sucursal inexistente, campo inválido) llevan el mismo folio de revisión (antes no tenían).

## Salidas: AJUSTE_SALIDA, ROTURA y SALIDA_MANUAL
- **Costo (contrato v9 del 10-nov y respuesta de Fernando Alejos del 9-nov, M-447):** toda salida, incluida SALIDA_MANUAL (captura, cancelación de entrada o devolución de venta), **sale al costo promedio del ERP** del saldo que toca. El `costoM2` (vidrio, con medida) o `costoPieza` (por cantidad) que trae SALIDA_MANUAL es el promedio de A+W y para el ERP es **informativo**: se valida su forma (con medida solo `costoPieza` → ERROR; sin medida solo `costoM2` → ERROR) pero no se usa. El ERP conserva el último promedio aunque la existencia llegue a cero. Ya no existe el `costo` total del grupo ni `origenCosto`; si llegara el `costo` viejo se ignora.
- **Moneda (M-447):** `datos.moneda`, si viene, debe ser `PESOSMX` (A+W distingue mayúsculas; se acepta también `MXN`). Otra → ERROR «moneda no soportada» con folio `REV-`, sin mover saldo.
- **`motivoClave` (v9):** clave fija del motivo (salidas: FALTANTE, CONSUMO_INTERNO, MUESTRA, PRESTAMO, CANCELA_ENTRADA). Se guarda al final del motivo del movimiento, `[FALTANTE]`. La cuenta por motivo: faltante y sobrante contra 504.25.02.00 (Fernando, 9-nov), que ya es la cuenta de AJUSTE_INVENTARIO; las demás siguen la cuenta por omisión de la salida hasta que Contabilidad las defina.
- En AJUSTE_SALIDA y ROTURA el costo no viaja: costo promedio.
- **Medidas** en mm sin redondear. La medida girada es la misma hoja (3302×6100 = 6100×3302): el resolvedor compara en los dos órdenes y, si el catálogo trajera ambas orientaciones como variantes distintas, elige la del mismo orden y luego la de menor Id. `piezas` son hojas enteras y se baja piezas × m² de la hoja (área a 4 decimales); el `m2` del grupo se acepta si cuadra con tolerancia 0.0001 + 0.00005 por hoja (no se redondea por hoja de su lado).
- **AJUSTE_SALIDA con `documentoPrevio`:** el descuento llega **al validar** el documento previo, no al cortar; entre corte y validación el inventario del ERP aún tiene la hoja disponible (el apartado es de la app y no viaja). Un renglón que se baja a cero no llega nunca (`piezas` ≤ 0 → ERROR). El ERP no reserva; si el contrato trae `documentoPrevio` como texto o número, queda en el motivo del movimiento («documento previo X»). No se interpreta su ventana ni sus renglones: el contrato no fija su forma.
- **Componentes (argón, interlayer, separadores…):** los descuenta el ERP al terminarse cada pieza (A4.8, `ConsumoComponentes`) y la app **no** los manda en AJUSTE_SALIDA; el ERP no los espera. La norma de consumo, el factor m²→m³ y el consumo de silicón y butilo siguen pendientes de Millet.

## Vista de costo promedio para Almacenes (v9, PROPUESTA a TI; M-447)
Desde v9 la app valúa entrada y salida manual con el costo promedio del ERP (opción a). Mientras no exista, sigue con el de A+W. Propuesta para el visto bueno de TI (antes del 13-nov):
- **Dónde:** tabla `dbo.erp_costo_promedio` en `AlmacenRQ_MP` (pruebas: `AlmacenRQ_MP_ENSAYO`), que la app ya lee con su propio acceso. El ERP solo inserta (mismo patrón que `erp_tipo_cambio`); no necesita usuario nuevo de su lado.
- **Columnas:** `articulo_aw` (clave de A+W, la misma de los mensajes), `sucursal` (clave A+W, p. ej. CONKAL), `almacen_tipo` y `linea` (la llave del almacén, M-446; `linea` vacía en herraje; las dos vacías en sucursales sin lista de almacenes, con el costo de la sucursal), `ancho_mm` y `alto_mm` (vidrio por medida; nulos en material por cantidad), `costo_m2` (vidrio) o `costo_pieza` (por cantidad), `moneda` = `PESOSMX`, `vigente_desde` (datetime2), `version` (bigint, sube con cada cambio).
- **Lectura:** vista `dbo.vw_erp_costo_promedio_vigente` con la última versión por llave (artículo, sucursal, tipo, línea, medida).
- **Cuándo se publica:** cada vez que cambia el promedio en el ERP (compra recibida, CAMBIO_ALMACEN o entrada con costo); con existencia en cero se conserva la última fila. El costo sale del saldo del ERP por ubicación y medida y es el mismo para todos los lotes (E07 S7).
- **Sin fila:** el ERP nunca tuvo ese artículo en ese almacén; se pregunta a TI si la entrada manual sigue con el costo de A+W.
- **Pendiente de TI:** visto bueno de nombre y columnas, y crear la tabla con el script que les mandamos.
- **Construido (M-447):** script `docs/operacion/aw-integracion-scripts/19_create_erp_costo_promedio.sql` (tabla, vista, permisos; guarda de base). Publicador `ErpCostoPromedioPublicador` + `ErpCostoPromedioWorker` (Integraciones.Aw): cada `IntervaloSegundos` calcula el promedio por llave (`CostoPromedioAppLector`, Almacén) y solo inserta la versión siguiente si cambió. Sección `Integraciones:CostoPromedio` (`Habilitado=false`, `Destino=Simulado`; `Sql` usa `ConnectionStrings:AlmacenesAppDb`). Solo artículos ligados a A+W con clave numérica.

## Área y equipo de la salida (H1, 6-nov)
Contabilidad pidió que quien captura el movimiento elija el **área** y, si la cuenta es de gasto (600), el **equipo**. No hay valores por omisión ni exención. La ubicación no se captura: sale de la sucursal.

- **Campos nuevos en `datos`** de AJUSTE_SALIDA, ROTURA y SALIDA_MANUAL, opcionales y para todo el movimiento:
  - `area`: clave del CeCo (Dim2);
  - `equipo`: clave del equipo (Dim3).

  Pueden venir como texto o número.
- **Cómo se resuelven:** dentro de la sucursal del mensaje, con los centros de sus ubicaciones más los CeCo corporativos (K10.2/V49).
- **Acuse ERROR** cuando:
  - la clave no existe, está dada de baja o es de otra sucursal;
  - el equipo no pertenece al área.
- **Qué viaja:** el equipo (o, si no viene, el área) va en cada línea del movimiento y en `almacen.salida_inventario.registrada.v1`. La póliza lleva la ubicación, el área y el equipo; la ubicación se deriva del centro.
- **Cuándo son obligatorios:** solo si la cuenta que cargará la póliza por esa causa los exige por la regla C-03:
  - costos (500): área;
  - gastos (600): área y equipo.

  La cuenta es la del rol de la plantilla vigente: CONSUMO, MERMA o AJUSTE_INVENTARIO.
- **Mientras la app no mande los campos** (`Integraciones:AlmMensaje:ExigirAreaEquipo=false`, por omisión):
  - la salida se acepta y su póliza queda en Rechazos con «Dato faltante», como hoy;
  - con `true`, la salida que no trae lo que exige su cuenta se acusa ERROR con el motivo («Falta el área: la cuenta … lo exige»).
- **Pendiente con TI:**
  - agregar los dos campos a la captura de salidas de la app;
  - de dónde toma la app el catálogo de áreas y equipos por sucursal: hoy está en el ERP (`/api/v1/contabilidad/movimientos/centros`).

## Entrada: ENTRADA_ALMACEN (v8, M-159)
La app la manda al **cerrar una entrada**. Referencia: el folio de la entrada (`EN-…`). `datos`: `entrada`, `compra` (folio de la OC del ERP), `sucursal` (código, `CONKAL`), `piezas` (total) y `lineas[]` con `requisicion`, `posicion`, `articuloAw`, `articuloAlmacenAw`, `articulo`, `ancho`, `alto`, `almacenGeneral` (`id`, `nombre`, `tipo`, `linea`: la línea si todo quedó en racks de una sola, si no `null`), `piezas`, `reclamadas`, `grupos[]` y `almacenes[]` (detalle por división + línea, con sus `grupos[]`, igual que en los demás mensajes). Herraje: `ancho`, `alto` y `m2` en `null`.

- **Qué hace el ERP:** sube existencia por sucursal + artículo A+W + medida **sin pedir rack**, en el almacén general (ubicación ÚNICA) de la sucursal: un movimiento `EntradaCompra` (folio `M-ENT-…`) y el acuse `RECIBIDO` con ese folio. Toma los grupos de `almacenes[].grupos` (si una línea no trae `almacenes`, los de `grupos`); no suma ambos. Con medida, `piezas` son hojas y sube piezas × m² de la hoja; el `m2` del grupo debe coincidir (misma tolerancia que en salidas). Sin medida sube piezas.
- **Lote (Eliam E3, ficha MP-05):** cada `grupos[].lote` es parte de la llave del saldo, igual que en la recepción de la pantalla: dos lotes en una línea quedan en dos filas de saldo (las piezas se reparten por grupo). Obligatorio con medida (vidrio de A+W): un grupo sin lote → ERROR «Falta el lote de la medida …» sin mover saldo. Sin medida (herraje) es opcional. El costo sigue por medida (E07 S7). `pedimento` y `etiqueta` no se guardan: la línea del movimiento no tiene esos campos.
- **Reclamadas:** la entrada trae el **total** de la compra, incluidas las `reclamadas`; entra el total. Las reclamadas salen aparte por `RECLAMACION_PROVEEDOR` (no implementado aquí), así no se cuentan dos veces.
- **Validaciones (ERROR con folio `REV-{mensajeId}`, sin mover saldo):** sucursal, artículo A+W o medida que no existen; la suma de los grupos distinta de las `piezas` de la línea; la suma de las líneas distinta de `datos.piezas`; `reclamadas` fuera de 0..piezas; sucursal sin almacén general activo; las reglas de OC, costo y periodo de abajo. Los siguientes de la misma referencia esperan; el reenvío (mismo `mensajeId`, `intento`+1) se vuelve a evaluar. El mismo `mensajeId` ya aplicado → `DUPLICADO` con el mismo folio.
- **Almacén:** cada grupo entra a la ubicación de su llave (`almacenGeneral.tipo` + `linea` de su renglón de `almacenes[]`, o de la línea si no trae `almacenes`; ver «Mapeo de almacén»). Solo en una sucursal sin llaves (o un mensaje sin `tipo`) entra a la ÚNICA activa de la sucursal, primero la del sub-almacén de materiales directos (nunca MAT-REV ni transitorio). El costo promedio de la entrada sin OC es el de esa ubicación.
- **Contra una OC del ERP (`compra` = folio de OC, p. ej. `OC-CNK2026-000123`):** la entrada **es la recepción de esa OC**, con el usuario de servicio del ERP. Cada partida se liga a la línea de la OC por artículo + medida (la app no manda `lineaErp`; si hay dos líneas iguales, la primera con pendiente) y entra al **precio de esa línea en pesos** (precio de inventario = OC; Fernando Alejos 9-nov: «la compra se registra con su valor real»): por m² en vidrio, por pieza en herraje, con el tipo de cambio de la OC. Aplica las reglas de la recepción de la pantalla: OC `Autorizada`, sucursal destino igual a la de la entrada, costo válido y **pendiente exacto** (más de lo pendiente → ERROR «Excede lo pendiente…», sin tolerancia en piezas). Publica `almacen.oc_recepcion.registrada.v1` con `FacturaPendiente=true` (como la lista de empaque: la factura llega después y CxP la concilia), así Compras actualiza lo recibido y su sub-estado Recepción. El movimiento lleva la OC, la línea de OC en cada renglón y la marca `app-almacenes:{entrada}`: **una entrada = una recepción**; la misma `entrada` con otro `mensajeId` acusa `DUPLICADO` con el folio de la primera, sin segunda recepción.
- **Sin OC del ERP** (folios de prueba como `zz_arnes_EKH`): entra al costo promedio vigente de esa medida en el almacén general; **nunca a costo cero**: sin promedio, ERROR «…no se registra a costo cero» sin mover saldo (antes entraba a 0 y diluía el promedio: 0 seguido de una recepción a 285.40/m² dejaba 95.13/m²).
- **Periodo:** mismo candado que la recepción de la pantalla (`PERIODO_CERRADO` de inventario y `PERIODO_CONTABLE_NO_ADMITE` si Contabilidad no tiene abierto el mes): ERROR con el motivo, sin mover saldo.
- **Bandeja «Mensajes de la app»:** un `ENTRADA_ALMACEN` en ERROR aparece en `GET /api/v1/almacen/mensajes-app/revision` (misma lista de tipos que el reenvío, `AlmacenesAppProcesador.TiposReevaluables`).
- **Recepción con factura:** desde M-159 una medida de A+W también se puede recibir en el almacén general de la sucursal (sin rack ni asignación). Packing list, devolución interna y dictamen de revisión siguen exigiendo rack. El trigger del ledger ya no rechaza A+W en la ÚNICA (migración `EntradaAppAlmacenGeneralAw`); la regla vive en `UbicacionEntradaGuard`.

## Mapeo de almacén (M-446, lista de TI del 9-nov)
La llave de un almacén es **sucursal + `almacenGeneral.tipo` + `almacenGeneral.linea`** (línea vacía o `null` en herraje); el `id` y el `nombre` del almacén general son dato acompañante y no se usan. Mayúsculas y espacios se normalizan.

- **Catálogo:** tabla `almacen.almacenes_app` (`sucursal_id`, `tipo_almacen`, `linea`, `ubicacion_id`, `ubicacion_aw`, `activo`; única por sucursal + tipo + línea). Cada llave apunta a una **ubicación** del ERP, que es donde vive el saldo (por artículo, medida y lote). La migración `M446AlmacenesAppLlaves` carga las **16 llaves de Conkal** (EXPORTACION, OBRAS, GENERAL, IMPORTACION TEMPORAL × BYSTRONIC, HEGLA 1, HEGLA 2 y herraje sin línea) en un almacén `APP-CONKAL` con un sub-almacén `MP-APP` (materiales directos) y una ubicación por llave (`EXP-HEGLA 1`, `OBR-HERRAJE`, …). Es idempotente: correrla otra vez no duplica. Sin la sucursal Conkal no inserta nada. Las sucursales que TI marcó «No (no habilitada)» (Circuito, Cottolengo, Cancún) no se crean.
- **Un solo sub-almacén para las 16:** la división vive solo en la app y en A+W las cuatro divisiones comparten la ubicación de la línea (`CRISTAL - HEGLA 1`, …, `HERRAJES - BODEGA`, guardada en `ubicacion_aw`). Como un movimiento es de un solo sub-almacén, así una entrada con grupos en varias llaves, o un traslado entre llaves, cabe en un movimiento.
- **Líneas de producción:** no se reusa `almacen.lineas_produccion` (M-172). Ese catálogo es de las líneas de consumo con tambores (insulado) y lo lista la pantalla de tambores; aquí la línea es solo parte de la llave del saldo, sin comportamiento propio.
- **Resolución (todos los mensajes que mueven existencia: ENTRADA_ALMACEN, AJUSTE_SALIDA, ROTURA, SALIDA_MANUAL y CAMBIO_ALMACEN):** si la sucursal tiene llaves, cada renglón resuelve la suya y solo toca esa ubicación (una salida no toma existencia de otra llave). Llave que no está en la lista, o renglón sin `tipo` en una sucursal con llaves → **ERROR** con folio `REV-{mensajeId}`, sin mover nada. Si ningún renglón trae `tipo`, o la sucursal aún no tiene llaves, se resuelve como antes (por sucursal).
- **Lote en las salidas:** si el grupo trae `lote`, se toma primero de los saldos de ese lote; si no alcanza, del resto de la misma llave (el más grande primero).
- **Ojo al arrancar Conkal con llaves:** la existencia que entró antes a la ÚNICA del almacén general de la sucursal (M-159) no está en ninguna llave; la carga inicial (`alm_carga_inicial`) debe dejar el saldo en las ubicaciones de las llaves.
- **Dónde se ven:** catálogo de almacenes del ERP (almacén `APP-CONKAL` → sub-almacén `MP-APP` → 16 ubicaciones).

## CAMBIO_ALMACEN (M-446)
Referencia: el folio del movimiento de la app (`MV-…`). `almacenes[]` dice de qué almacén salen las hojas (división + línea, con sus grupos) y `almacenDestino` a cuál entran. Un mismo mensaje cubre cambio de división, de línea o de ambos (Jorge 9-nov); el traspaso entre racks de la misma línea no se manda.

- **Qué hace el ERP:** por cada sub-almacén de origen registra un **par enlazado**: salida `TrasladoInterno` (tipo 14, folio `M-TRA…`) del origen y entrada `EntradaTrasladoInterno` (tipo 15, folio `M-TRE…`) al destino, ligada a la salida (`salida_origen_id`). La entrada lleva el **costo promedio del origen**, la misma medida y el mismo lote; en el destino el trigger recalcula su promedio. El acuse es `RECIBIDO` con el folio de la salida (uno solo cuando el origen es una llave). El motivo de ambos lleva el `MV-…`, el motivo de la app y «origen → destino».
- **Sin póliza:** el traslado no publica evento contable (Contabilidad solo contabiliza los eventos que Almacén le publica; ninguno sale de un traslado). En kardex aparecen las dos filas («Traslado interno (salida)» / «(entrada)»).
- **Sin existencia en el origen:** ERROR `REV-{mensajeId}` con «Sin existencia suficiente en el almacén …», nada se mueve, el mensaje queda `EN_REVISION` y los siguientes de su referencia esperan; el reenvío (mismo `mensajeId`) se vuelve a evaluar. El mismo `mensajeId` ya aplicado → `DUPLICADO` con el mismo folio, sin segundo movimiento.
- **Validaciones:** `almacenDestino.tipo` requerido; origen o destino fuera de la lista → ERROR `REV-…`; mismas reglas de grupo que las salidas (artículo, medida, `m2`). No lleva costo, área ni equipo.
- **Otros flujos que siguen al traslado:** el costo de una recepción (`RecepcionesCostoReadAdapter`) sigue sus hojas al destino como en la liberación de revisión, y una recepción cuyas hojas ya se trasladaron no se puede cancelar (igual que tras una salida).

## Cancelación de una entrada: REQUISICION_CANCELADA / REQUISICION_REDUCIDA + SALIDA_MANUAL (M-446)
La app escribe **dos mensajes en la misma operación**: primero `REQUISICION_CANCELADA` (si era la única entrada cerrada de la requisición) o `REQUISICION_REDUCIDA` (si la requisición tiene otra vigente), y luego una `SALIDA_MANUAL` con `origen.tipo = CANCELACION_ENTRADA`. Se ligan por el folio de la entrada (`entradaCancelada` en el aviso, `origen.entrada` en la salida) y el de la compra.

- **El aviso de requisición** llega a Compras por su puerto público (`IComprasCrearRqAppPort`, el mismo de `REQUISICION_ENVIADA`) con el folio de la app (`datos.folio` = folio externo de la RQ). **No mueve existencia.**
  - `REQUISICION_CANCELADA`: la RQ pasa a `Cancelada` con el mismo agregado y evento que la cancelación de la pantalla, motivo «Otro» (`RECH-OTRO`) y el texto de la app («motivo · entrada EN-… cancelada · compra …»), usuario de servicio del ERP. También llega al cancelar una RQ ya enviada sin compra (sin `entradaCancelada` ni `compra`).
  - `REQUISICION_REDUCIDA`: por cada `lineas[]` (`posicion`, `piezasQuitadas`) baja lo que falta comprar de esa posición de la RQ (`CantidadDeCompra`, sin bajar de lo ya recibido) y lo pasa a «sin compra», así el saldo por comprar ya no lo incluye. Con `cierreConFaltante: true` es lo que ya no se espera.
  - Acuse `RECIBIDO` con el folio del ERP de la RQ. RQ que nunca llegó al ERP (o el ERP la rechazó) o ya terminada (cancelada, cerrada) → `RECIBIDO` sin cambios (folio vacío si no existe). Posición inexistente u otra regla de Compras → `ERROR` (no se guarda: el reenvío se vuelve a evaluar).
- **La SALIDA_MANUAL** es la que baja lo recibido, con su costo de A+W y las reglas de «Salidas» (por llave en una sucursal con llaves).
- Cada mensaje se aplica una sola vez por su `mensajeId` (reenvío = `DUPLICADO` con el mismo folio), así el saldo y el kardex cuadran con una sola baja.
- **No hace:** no revierte lo recibido de la OC en Compras ni crea devolución a proveedor (eso es el sub-flujo 8.B); una OC ya emitida no se reduce sola (Compras la cancela o cierra desde su pantalla).

## Compatibilidad
Todo campo nuevo del sobre es opcional (también `area` y `equipo`, H1). Cambios: `costoM2`/`costoPieza` por grupo reemplazan a `costo` (se quita el total del grupo: aviso de TI en la siguiente versión del contrato); el acuse de salida sin existencia pasa de RECIBIDO a ERROR.

## Falta contra el sistema real
- Probar `SqlAlmMensajeOrigen` contra `AlmacenRQ_MP_ENSAYO` (~13-nov): permisos del login (UPDATE solo de `acuse_*`), la consulta de pendientes con reenvíos (intento+1) y el plan de ejecución (agrupa por `mensaje_id`; si la tabla crece, indexar `(mensaje_id, intento)`).
- Forma de `documentoPrevio` (ventana y renglones).
- Que el ERP deba o no fallar un `REQUISICION_ENVIADA` RECIBIDO sin folio (hoy el procesador siempre lo manda).
