# 11 · Pinturas ↔ ERP: tablas en `MILLET_INTEGRACION` (script 16 v2)

Estado: **propuesta para visto bueno de TI de Millet**; el script no se ha ejecutado en ninguna base de Millet. Se validó solo en un SQL Server 2022 local desechable (crea todo, repite sin error, permisos comprobados con los dos usuarios).
Script: `docs/operacion/aw-integracion-scripts/16_create_pinturas.sql`. Demo en Postgres: `tools/aw-origen-demo/pinturas.sql`.
Fuentes: respuestas de TI del 30-oct y 1-nov (hilo «Correcciones y script de Pinturas»), acuerdo del 28-oct.

## Flujo

- El ERP **publica** cuatro catálogos (`pinturas_cliente`, `pinturas_servicio`, `pinturas_obra`, `pinturas_empleado`); el portal solo los lee. Publicación en segundos, baja lógica con `activo = 0`.
- El portal **inserta** una fila por orden en `pinturas_pedido` (línea única, `folio_orden` único). El ERP la lee cada 10 s (`PlantaPinturaImportWorker`) y escribe el acuse en la misma fila.
- **Sin reenvío:** una orden transmitida queda cerrada. Si el ERP la rechaza, se captura una orden nueva con folio nuevo (hay que recapturar el presupuesto; Pinturas lo aceptó el 1-nov como «documentos muertos»). Que el portal no pueda actualizar es una garantía de base de datos, no solo de convención.
- **Acuse de rechazo:** `erp_resultado = 2` y `erp_error` con el campo (columna de `pinturas_pedido`) y el valor que no cuadró, por ejemplo `cliente_clave='900123': no existe en el ERP` o `iva_porcentaje=8: no coincide con la tasa del servicio 'SER95224' (16)`. Lo arma `ImportarPedidoPlantaPinturaHandler`.

## Contrato (v2 frente a la v1 propuesta el 28-oct)

| Objeto | Cambio | Compatibilidad |
|---|---|---|
| `pinturas_cliente.clave` | `nvarchar(50)` → `int` (numérica; el portal la convierte a entero) | Rompe v1. La v1 nunca se ejecutó; si alguien la aplicó, rollback primero |
| `pinturas_cliente.contacto`, `celular` | Nuevas, `NULL` permitido, desde el arranque | Aditivo |
| `pinturas_pedido.cliente_clave` | `nvarchar(50)` → `int` | Rompe v1 (igual que arriba) |
| `pinturas_servicio.clave` | Sin cambio: texto, conserva la clave actual de SAP (p. ej. `SER95224`) | Igual |
| `pinturas_obra.origen`, `referencia_aw` | Nuevas: `origen` = `AW` o `PINTURAS` (por omisión `PINTURAS`); `referencia_aw` = ID en `KA_OBJEKT` cuando viene de A+W | Aditivo; permite un catálogo compartido |
| Permisos del portal en `pinturas_pedido` | `SELECT`, `INSERT`; `DENY UPDATE`, `DENY DELETE`. Se quitó el `UPDATE` de 11 columnas | Más estricto; lo pidió TI |
| Permisos del ERP | Sin cambio (catálogos `SELECT/INSERT/UPDATE`; pedido `SELECT` + `UPDATE` de las 4 columnas del acuse) | Igual |

`erp_error` sigue siendo `nvarchar(500)`; no se agregaron columnas de campo y valor aparte: el texto lleva ambos.

## Nota de ejecución

El índice filtrado de pendientes exige `QUOTED_IDENTIFIER ON`. sqlcmd lo abre en OFF, así que el script lo activa al inicio. Cualquier `INSERT` manual por sqlcmd en `pinturas_pedido` necesita `-I`; los clientes ADO.NET lo traen en ON.

## Código del ERP que depende de esto

- `ImportarPedidoPlantaPinturaHandler`: mensajes de rechazo con campo y valor (los nombres son los de las columnas de `pinturas_pedido`). Pruebas en `PlantaPinturaTests`.
- Lector real (I13.9): `PinturasPedidosAdapter` (`backend/src/Integraciones.Aw/Infrastructure/Pinturas/`). Lee `erp_resultado IS NULL` por `creado_at, id` con el nombre del servicio y la obra, y escribe el acuse solo si la fila sigue pendiente (`... AND erp_resultado IS NULL`). Usa `ConnectionStrings:AwIntegracionDb` (login `millet_erp_integracion`); sin ella queda `StubPlantaPinturaPedidosReader` (0 pedidos). Con el origen Demo lee la copia PostgreSQL (`tools/aw-origen-demo/pinturas.sql`).
- Configuración `IntegracionesAw:Pinturas`: `SucursalClave` (la tabla no trae sucursal; vacía = no lee), `ComportamientoFiscal` (por omisión `MostradorInmediato`, a validar) y `SqlQueryTimeoutSeconds`. El worker conserva sus candados (`Facturacion:Workers:PlantaPinturaImport`: `EmpresaId`, `Disabled`).
- Mapeo: `cliente_clave` (int) se busca como texto en `Cliente.ReferenciaExterna`; `servicio_clave` en la referencia del producto; `iva_porcentaje` 16 → 0.16; versión fija 1 (sin reenvío); `ObraId` solo si la obra viene de A+W (`pinturas_obra.referencia_aw`); la fila completa queda como payload crudo.
- Sigue pendiente (sin tarea): publicador de los catálogos, contacto y celular en el cliente del ERP, catálogo de obras, y el retorno de estado/UUID a Pinturas (por confirmar con TI; la tabla no tiene columnas para eso).

## Preguntas abiertas (campos configurables, sin decidir)

1. Rango de numeración de los clientes de Pinturas, y si alguna vez se envían a A+W. La columna es `int` y el rango lo aplicará el ERP por configuración.
2. Quién da de alta clientes y servicios de Pinturas (TI dijo el 1-nov que no es TI: persona de Administración con el rol para dar de alta clientes). Falta nombrarla.
3. Obras: lista inicial de SAP (P-196, sin respuesta), carga desde `KA_OBJEKT` y numeración de las altas manuales. Debe estar poblado antes del corte porque al apagar SAP desaparece la lista.
4. Cuáles de los 263 servicios restantes del grupo 158 se marcan y la carga de los 85 con sus claves.
5. Longitudes (`folio_orden`, claves de servicio/obra/empleado en `nvarchar(50)`) contra `Pinturas_NEW`.
6. Pendiente de otra línea: el registro que Pinturas escribe en el Portal de Salidas no está en este script.
