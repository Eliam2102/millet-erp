# 11 · Almacenes → ERP (`alm_mensaje`) · alineación con TI del 5-nov

Estado: construido contra origen **simulado**. La tabla `alm_mensaje` no existe (TI la crea ~13-nov, sin usuario del ERP todavía). Fuentes: contrato v6 (§7.1, §8.3, §8.5), acuerdo 28-oct, correos de Jorge del 29/30-oct, 1-nov y **5-nov 16:49Z** (respuesta íntegra en la bóveda: `Documentos/2026-11-05 correos Millet/`). Complementa a `10-compras-a-almacenes-alm-compra.md` (el sentido contrario).

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
- **Costo (SALIDA_MANUAL, una sola regla sin importar su origen: captura del almacenista, cancelación de entrada o devolución de venta):** el grupo trae el promedio de A+W al momento del movimiento: `costoM2` (vidrio, con medida) o `costoPieza` (material por cantidad, sin medida). Costo de la hoja = `costoM2` × m² de la hoja; costo del grupo = `costoM2` × `m2` del grupo. Se guarda el valor que llega (a la precisión de la columna, 14,4) y **no se recalcula contra A+W**. Sin costo → costo promedio del saldo del ERP. Un grupo con medida que trae solo `costoPieza` (o sin medida con solo `costoM2`) → ERROR. Ya no existe el `costo` total del grupo (ni a nivel de almacén) ni `origenCosto`; si llegara el `costo` viejo se ignora.
- En AJUSTE_SALIDA y ROTURA el costo no viaja: costo promedio.
- **Medidas** en mm sin redondear. La medida girada es la misma hoja (3302×6100 = 6100×3302): el resolvedor compara en los dos órdenes y, si el catálogo trajera ambas orientaciones como variantes distintas, elige la del mismo orden y luego la de menor Id. `piezas` son hojas enteras y se baja piezas × m² de la hoja (área a 4 decimales); el `m2` del grupo se acepta si cuadra con tolerancia 0.0001 + 0.00005 por hoja (no se redondea por hoja de su lado).
- **AJUSTE_SALIDA con `documentoPrevio`:** el descuento llega **al validar** el documento previo, no al cortar; entre corte y validación el inventario del ERP aún tiene la hoja disponible (el apartado es de la app y no viaja). Un renglón que se baja a cero no llega nunca (`piezas` ≤ 0 → ERROR). El ERP no reserva; si el contrato trae `documentoPrevio` como texto o número, queda en el motivo del movimiento («documento previo X»). No se interpreta su ventana ni sus renglones: el contrato no fija su forma.
- **Componentes (argón, interlayer, separadores…):** los descuenta el ERP al terminarse cada pieza (A4.8, `ConsumoComponentes`) y la app **no** los manda en AJUSTE_SALIDA; el ERP no los espera. La norma de consumo, el factor m²→m³ y el consumo de silicón y butilo siguen pendientes de Millet.

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

## Mapeo de almacén (cambio de TI, sin código nuevo)
El ERP **no usa** `almacenGeneral` ni su id: ignora el bloque y baja existencia por sucursal + artículo A+W + medida. La sucursal viaja por código (`CONKAL`) y se resuelve por `Clave`/`ClaveAw`. Cuando se arme el mapeo a sub-almacenes será por **sucursal + tipo** (EXPORTACIÓN, OBRAS, GENERAL e IMPORTACIÓN TEMPORAL), tolerando que falte alguno (el máximo es por sucursal y varias tienen menos) y con un nivel nuevo de **línea de producción** en Conkal (HEGLA 1, HEGLA 2, BYSTRONIC) por encima del almacén general. No hay clave A+W por sucursal y almacén general; el id es dato acompañante, el catálogo se rehará.

## Compatibilidad
Todo campo nuevo del sobre es opcional (también `area` y `equipo`, H1). Cambios: `costoM2`/`costoPieza` por grupo reemplazan a `costo` (se quita el total del grupo: aviso de TI en la siguiente versión del contrato); el acuse de salida sin existencia pasa de RECIBIDO a ERROR.

## Falta contra el sistema real
- Nombres y tipos de columna, y que `mensaje_id` sea único con `secuencia` como llave de actualización; el usuario del ERP (SELECT + UPDATE de `acuse_*`) y la base (`AlmacenesAppDb`; en el correo del 29-oct era la misma instancia que MILLET_INTEGRACION).
- Forma de `documentoPrevio` (ventana y renglones) y si el reproceso de la app borra el acuse del mismo renglón o crea otro.
- Probar el origen `Sql` (`SqlAlmMensajeOrigen`) y el comportamiento del reproceso contra la tabla real (~13-nov).
