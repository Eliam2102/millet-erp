# 10 · Compras de MP → Almacenes (`alm_compra`) · C3.2 (86e3jvu83)

Estado: construido contra transporte **simulado**. La tabla `alm_compra` no existe (Jorge, ~13-nov). Fuentes: contrato v6 §9, acuerdo 28-oct, correo de Jorge 29/30-oct (`Millet_VILO_Almacenes_28oct_con_respuestas.md`) y 1-nov.

## Flujo
- **Qué se manda:** las líneas de la OC cuyo artículo está ligado a A+W (vidrio y herrajes; criterio de MP del acuerdo 28-oct), vengan o no de una RQ de la app (ficha C3.2, regla 1). Sin líneas así no se genera nada y la OC se cancela como siempre. Las líneas sin RQ de la app van sin `requisicion`/`posicion` (v6 los marca obligatorios: por confirmar con Jorge).
- OC autorizada (N2) → fila `COMPRA_RECIBIDA` en `compras.alm_compra_envios`, en la misma transacción que la autorización.
- **Entrada confirmada en el ERP** (CA13.8, CA3.10, CA12.2) → `COMPRA_MODIFICADA` completa con el bloque aditivo `datos.recepciones[]` = `{folio, fecha, importacion?, lineas[{lineaErp, piezas, lote, rack, fraccion?}]}` (hojas, lote y rack de cada entrada; `importacion` = `{pedimento, bl, contenedor, aduana, clavePedimento}` solo si la OC es de importación). v6 no define este bloque ni el momento: es supuesto, pendiente del visto bueno por escrito de Jorge. `COMPRA_RECIBIDA` sigue saliendo al autorizar la OC (v6).
- **Tablero** (I13.6): Compras › Envíos a Almacenes (`/compras/integraciones/alm-compra`) lista los mensajes con su acuse, resume lo que recibe A+W (hojas, lote, rack, importación) y reintenta los que quedaron en ERROR sin escribirse (sin duplicar: `mensajeId` único).
- Pedimento / BL / ETA / agente editado en OC autorizada → `COMPRA_MODIFICADA` **completa**, solo si hay cambio y ya se mandó la compra.
- **Cancelar una OC ya mandada es condicional al acuse** (Jorge 29-oct pt. C y 1-nov: sin entrada cerrada se acepta; con entrada, ERROR y lo sobrante sale como SALIDA_MANUAL desde la app). El ERP no ve las entradas de la app, así que la OC pasa a `CancelacionSolicitada` (solicitud con `EsperaAlmacenes`, reutiliza el patrón de la doble firma), se encola `COMPRA_CANCELADA` y no se libera ninguna RQ. Acuse PROCESADO/DUPLICADO → `Cancelada` + liberación de RQ + `OrdenCompraCanceladaEvent`, como hoy. Acuse ERROR → la OC vuelve a su estado previo, la fila queda `RECHAZADO` (no detiene la referencia) y la solicitud guarda «ya tiene entrada en Almacenes; lo sobrante sale como salida manual» (visible en `solicitudesCancelacion[].respuestaAlmacenes` del detalle de la OC). Misma vía para la segunda firma de la cancelación con recepciones.
- `COMPRA_MODIFICADA` con ERROR sí detiene la referencia; nunca baja de lo recibido (validado en el ERP).
- Worker (`Integraciones:AlmCompra:Habilitado`, apagado por omisión) escribe por `IAlmCompraTransporte` y lee el acuse: orden estricto por referencia (folio de OC) y `secuencia`; ERROR detiene la referencia (se relee por si Almacenes reprocesa); falla de transporte reintenta con espera 5 s·2^n (tope 15 min) y tras `MaxIntentos` pasa a ERROR.
- `mensajeId` = `ERP-{folioOC}-{R|C|M}{ordinal}` (≤ 64), único en BD.

## Sobre v1 (campos)
`mensajeId, tipo, version=1, fecha, origen="ERP", referencia` (referencia = folio OC, campo agregado, opcional) + `datos`:
`folio, proveedor{claveAw,nombre}, moneda, tipoCambio (solo si moneda≠MXN), fechaCompra, lineas[]`.
Línea: `lineaErp, requisicion y posicion (solo si viene de una RQ de la app; FolioExterno), articuloAw, ancho, alto, piezas, lote, pedimento ("26 47 3456 6001234"), costoM2, costoPieza`. Nulos no viajan. `costoMedioM2` y `etiqueta` no se mandan; `articuloAlmacenAw` no se manda (Jorge 30-oct). Medida girada = misma hoja: las líneas que solo difieren en la orientación (mismo artículo, lote, pedimento, RQ y costo) se juntan en una sola, sumando piezas y con la orientación de la primera; el material por cantidad va sin ancho ni alto (Jorge 30-oct y 1-nov).
`COMPRA_CANCELADA.datos = {folio, motivo?}`; `COMPRA_MODIFICADA.datos` = igual que RECIBIDA. Estos dos formatos son supuesto: v6 solo define RECIBIDA.

## Compatibilidad
Todo es nuevo (v1). Campo nuevo = opcional. Config: `Integraciones:AlmCompra:{Habilitado=false, Transporte=Simulado|Sql, IntervaloSegundos=10, MaxIntentos=8}`.

## Falta contra el sistema real
`SqlAlmCompraTransporte` usa nombres de columna supuestos y no se ha probado; ajustar al DDL de Jorge. Ver PR para supuestos y preguntas.
