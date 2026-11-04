# 10 · Compras de MP → Almacenes (`alm_compra`) · C3.2 (86e3jvu83)

Estado: construido contra transporte **simulado**. La tabla `alm_compra` no existe (Jorge, ~13-nov). Fuentes: contrato v6 §9, acuerdo 28-oct, correo de Jorge 29/30-oct (`Millet_VILO_Almacenes_28oct_con_respuestas.md`) y 1-nov.

## Flujo
- OC autorizada (N2) cuyas líneas surten una RQ de la app (`Origen = AlmacenesApp`) → fila `COMPRA_RECIBIDA` en `compras.alm_compra_envios`, en la misma transacción que la autorización. OC sin líneas de la app: no genera nada.
- Pedimento / BL / ETA / agente editado en OC autorizada → `COMPRA_MODIFICADA` **completa** (no diferencias), solo si hay cambio y ya se mandó la compra.
- OC cancelada → `COMPRA_CANCELADA`. Con material recibido queda en ERROR local (Almacenes solo acepta mientras no haya entrada cerrada).
- Worker (`Integraciones:AlmCompra:Habilitado`, apagado por omisión) escribe por `IAlmCompraTransporte` y lee el acuse: orden estricto por referencia (folio de OC) y `secuencia`; ERROR detiene la referencia (se relee por si Almacenes reprocesa); falla de transporte reintenta con espera 5 s·2^n (tope 15 min) y tras `MaxIntentos` pasa a ERROR.
- `mensajeId` = `ERP-{folioOC}-{R|C|M}{ordinal}` (≤ 64), único en BD.

## Sobre v1 (campos)
`mensajeId, tipo, version=1, fecha, origen="ERP", referencia` (referencia = folio OC, campo agregado, opcional) + `datos`:
`folio, proveedor{claveAw,nombre}, moneda, tipoCambio (solo si moneda≠MXN), fechaCompra, lineas[]`.
Línea: `lineaErp, requisicion (FolioExterno de la RQ), posicion, articuloAw, ancho, alto, piezas, lote, pedimento ("26 47 3456 6001234"), costoM2, costoPieza`. Nulos no viajan. `costoMedioM2` y `etiqueta` no se mandan; `articuloAlmacenAw` no se manda (Jorge 30-oct).
`COMPRA_CANCELADA.datos = {folio, motivo?}`; `COMPRA_MODIFICADA.datos` = igual que RECIBIDA. Estos dos formatos son supuesto: v6 solo define RECIBIDA.

## Compatibilidad
Todo es nuevo (v1). Campo nuevo = opcional. Config: `Integraciones:AlmCompra:{Habilitado=false, Transporte=Simulado|Sql, IntervaloSegundos=10, MaxIntentos=8}`.

## Falta contra el sistema real
`SqlAlmCompraTransporte` usa nombres de columna supuestos y no se ha probado; ajustar al DDL de Jorge. Ver PR para supuestos y preguntas.
