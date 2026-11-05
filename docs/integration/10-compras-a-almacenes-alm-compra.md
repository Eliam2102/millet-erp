# 10 · Compras de MP → Almacenes (`alm_compra`) · C3.2 (86e3jvu83)

Estado: construido contra transporte **simulado**. La tabla `alm_compra` no existe (Jorge, ~13-nov). Fuentes: contrato v6 §9, acuerdo 28-oct, correo de Jorge 29/30-oct (`Millet_VILO_Almacenes_28oct_con_respuestas.md`) y 1-nov. **Fuente de los mensajes de Compras a Almacenes: la definición escrita de TI de Millet del 5-nov** (la de abajo manda sobre lo anterior). TI confirmó que el ERP **no manda nada de la recepción** (hojas, lote ni rack de la entrada): eso es de Almacenes.

## Flujo
- **Qué se manda:** las líneas de la OC cuyo artículo está ligado a A+W (vidrio y herrajes; criterio de MP del acuerdo 28-oct), vengan o no de una RQ de la app (ficha C3.2, regla 1). Sin líneas así no se genera nada y la OC se cancela como siempre. Las líneas sin RQ de la app van sin `requisicion`/`posicion` (v6 los marca obligatorios: por confirmar con Jorge).
- OC autorizada (N2) → fila `COMPRA_RECIBIDA` en `compras.alm_compra_envios`, en la misma transacción que la autorización.
- **Recepción: no viaja** (TI, 5-nov). Se quitó `datos.recepciones[]` (hojas, lote, rack, fracción e importación de cada entrada) y el envío al confirmar la entrada en el ERP. `COMPRA_RECIBIDA` sale al autorizar la OC (v6).
- **Tablero** (I13.6): Compras › Envíos a Almacenes (`/compras/integraciones/alm-compra`) lista los mensajes con su acuse, resume lo que recibe Almacenes (hojas, lote del proveedor, pedimento) y reintenta los que quedaron en ERROR sin escribirse (sin duplicar: `mensajeId` único).
- Pedimento / BL / ETA / agente editado en OC autorizada (o reautorización tras reabrir) → `COMPRA_MODIFICADA` **completa**, solo si hay cambio y ya se mandó la compra. Validado en el ERP **antes de encolar** (reglas de TI, abajo).
- **Cancelar una OC ya mandada es condicional al acuse** (Jorge 29-oct pt. C y 1-nov: sin entrada cerrada se acepta; con entrada, ERROR y lo sobrante sale como SALIDA_MANUAL desde la app). El ERP no ve las entradas de la app, así que la OC pasa a `CancelacionSolicitada` (solicitud con `EsperaAlmacenes`, reutiliza el patrón de la doble firma), se encola `COMPRA_CANCELADA` y no se libera ninguna RQ. Acuse PROCESADO/DUPLICADO → `Cancelada` + liberación de RQ + `OrdenCompraCanceladaEvent`, como hoy. Acuse ERROR → la OC vuelve a su estado previo, la fila queda `RECHAZADO` (no detiene la referencia) y la solicitud guarda «ya tiene entrada en Almacenes; lo sobrante sale como salida manual» (visible en `solicitudesCancelacion[].respuestaAlmacenes` del detalle de la OC). Misma vía para la segunda firma de la cancelación con recepciones.
- `COMPRA_MODIFICADA` con ERROR sí detiene la referencia.
- Worker (`Integraciones:AlmCompra:Habilitado`, apagado por omisión) escribe por `IAlmCompraTransporte` y lee el acuse: orden estricto por referencia (folio de OC) y `secuencia`; ERROR detiene la referencia (se relee por si Almacenes reprocesa); falla de transporte reintenta con espera 5 s·2^n (tope 15 min) y tras `MaxIntentos` pasa a ERROR.
- `mensajeId` = `ERP-{folioOC}-{R|C|M}{ordinal}` (≤ 64), único en BD.

## Sobre v1 (campos)
`mensajeId, tipo, version=1, fecha, origen="ERP", referencia` (referencia = folio OC, campo agregado, opcional) + `datos`:
`folio, proveedor{claveAw,nombre}, moneda, tipoCambio (solo si moneda≠MXN), fechaCompra, lineas[]`.
Línea: `lineaErp, requisicion y posicion (solo si viene de una RQ de la app; FolioExterno), articuloAw, ancho, alto, piezas, lote, pedimento ("26 47 3456 6001234"), costoM2, costoPieza`. Nulos no viajan. `costoMedioM2` y `etiqueta` no se mandan; `articuloAlmacenAw` no se manda (Jorge 30-oct). Medida girada = misma hoja: las líneas que solo difieren en la orientación (mismo artículo, lote, pedimento, RQ y costo) se juntan en una sola, sumando piezas y con la orientación de la primera; el material por cantidad va sin ancho ni alto (Jorge 30-oct y 1-nov).
## Reglas de TI (5-nov) para COMPRA_CANCELADA y COMPRA_MODIFICADA
**COMPRA_CANCELADA.datos** = `{folio, motivo, fechaCancelacion?}`. `folio` obligatorio (si no existe, ERROR); `motivo` obligatorio (texto); `fechaCancelacion` opcional (si falta, se toma la fecha del mensaje: el ERP no la manda). Solo se acepta mientras la compra no tenga entrada cerrada; con entrada cerrada, ERROR (lo sobrante sale como `SALIDA_MANUAL`). En el ERP: `AlmCompraSobre.DatosCancelada` rechaza un motivo vacío (`ALM_COMPRA_CANCELACION_SIN_MOTIVO`) antes de encolar; los handlers de cancelar lo arman del catálogo de motivos, así que nunca va vacío.

**COMPRA_MODIFICADA.datos** = la compra completa, con la misma forma y el mismo sobre que `COMPRA_RECIBIDA`, solo hasta la entrada (ventana del pedimento). TI aplica: `moneda`, `tipoCambio`, `fechaCompra`, `piezas` (sin bajar de lo recibido ni pasar de lo que falta por comprar), `pedimento`, `costoM2`/`costoPieza`/`costoMedioM2` (no negativos); `lote` y `etiqueta` solo si el almacenista no los editó. **No cambian y deben coincidir, o ERROR:** `folio`, `proveedor`, `lineaErp`/`requisicion`/`posicion`, `articuloAw`, `ancho` y `alto` (en cualquier orientación). `articuloAlmacenAw` es informativo (no se manda).

Validación en el ERP antes de encolar (`AlmCompraEncolador`):
- **Ventana:** con alguna línea ya recibida en el ERP (`CantidadRecibida > 0`) no se encola `COMPRA_MODIFICADA` (se omite sin fila; el reintento manual de una fila ya armada falla con `ALM_COMPRA_MODIFICADA_FUERA_DE_VENTANA`). Es un sustituto: el ERP no ve las entradas cerradas en la app de Almacenes; ahí decide el acuse ERROR.
- **No cambia:** si folio, proveedor (`claveAw`), líneas (mismo conjunto de `lineaErp`), `requisicion`/`posicion`, `articuloAw` o la medida (en cualquier orientación) difieren de la `COMPRA_RECIBIDA` ya mandada, la fila queda en ERROR local `ALM_COMPRA_MODIFICADA_CAMPO_FIJO` (visible y reintentable en el tablero; no se manda).
- **Piezas:** sigue `ALM_COMPRA_MODIFICADA_BAJO_RECIBIDO` (no bajar de lo recibido). «No pasar de lo que falta por comprar» no se valida en el ERP (no conoce el saldo de la compra en Almacenes); lo decide el acuse.

**Acuse:** `PROCESADO`, `DUPLICADO` o `ERROR` con texto. Es lo que usa el contrato `alm_compra` en el código (`AlmCompraEnvio`); `RECIBIDO` es el literal de los mensajes que entran de la app de Almacenes al ERP (otro flujo). TI dijo antes `RECIBIDO` para este acuse: sin evidencia escrita no se cambió; por confirmar con TI.

## Compatibilidad
Todo es nuevo (v1). Campo nuevo = opcional. Config: `Integraciones:AlmCompra:{Habilitado=false, Transporte=Simulado|Sql, IntervaloSegundos=10, MaxIntentos=8}`.

## Falta contra el sistema real
`SqlAlmCompraTransporte` usa nombres de columna supuestos y no se ha probado; ajustar al DDL de Jorge. Ver PR para supuestos y preguntas.
