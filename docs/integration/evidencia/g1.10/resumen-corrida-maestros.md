# Corrida de maestros contra AW_FULL (solo conteos)

Generado: 2026-10-23 00:02 UTC. Copia local de A+W, solo lectura. No contiene RFC, nombres, domicilios ni descripciones.

Huellas idénticas antes/después: **sí**

| Tabla | Filas antes | Filas después | Checksum antes | Checksum después |
|---|---|---|---|---|
| KU_KUNDEN | 44695 | 44695 | -2021978635 | -2021978635 |
| KA_ZAHLBED | 11 | 11 | -2002677067 | -2002677067 |
| BA_PRODUKTE | 6802 | 6802 | 710838557 | 710838557 |
| BA_STUKL | 10548 | 10548 | -2124846834 | -2124846834 |
| BA_LAGMA | 1780 | 1780 | -1531865802 | -1531865802 |

| Corrida | Entidad | Leídos | Creados | Actualizados | Sin cambios | Conflictos | Errores | Duración (s) |
|---|---|---|---|---|---|---|---|---|
| 1 | Clientes | 44694 | 44685 | 0 | 0 | 0 | 9 | 1306.3 |
| 1 | Productos | 6801 | 6801 | 0 | 0 | 0 | 0 | |
| 2 | Clientes | 44694 | 0 | 0 | 44685 | 0 | 9 | 407.8 |
| 2 | Productos | 6801 | 0 | 0 | 6801 | 0 | 0 | |

## Conflictos por tipo y estado

- Cliente/RfcCompartido/Pendiente: 1634

## Cambios registrados por campo

Ninguno.

## Errores de clientes por código (corrida 1)

- fila_invalida: 9

## Errores de productos por código (corrida 1)

Ninguno.

Grupos duplicados en la bitácora: 0. Clientes con el mismo RFC no genérico: 0.

## Contexto y comparación con la corrida del 21-oct

Commit de develop: `287539ec` (rama docs/g110-evidencia-86e3qcgyn). Base PostgreSQL desechable (`--shared-db`, :5433), nunca dev. Segunda corrida idempotente: 0 creados, 0 actualizados, 44,685 clientes y 6,801 productos sin cambios.

| Concepto | 21-oct | 23-oct (UTC) |
|---|---|---|
| Clientes leídos | 44,694 | 44,694 |
| Clientes creados | 30,556 | 44,685 |
| Clientes con error | 14,138 | 9 (`fila_invalida`) |
| Productos leídos | 6,801 | 6,801 |
| Productos creados | 6,770 | 6,801 |
| Productos con error | 31 | 0 |
| `RfcCompartido` Pendiente | informativo (sin revisión) | 1,634 |
