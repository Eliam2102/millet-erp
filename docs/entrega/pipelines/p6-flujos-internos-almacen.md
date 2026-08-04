# P6 — Flujos internos de Almacén

Cuatro flujos que viven completos dentro de Almacén (no cruzan con CxP ni
Tesorería): salida contra RQ, vale urgente con regularización, devolución
interna (8.A) y conteo de inventario con ajuste.

**Evidencia en dev (2026-07-15):** salida `M-SAL2026-000004` · vale
`M-SAL2026-000005` · devolución interna `M-DEV2026-000002` · ajuste
`M-AJN2026-000001` (−1 pza, −$150.00).

## Actores y permisos

| Flujo | Actor | Permisos requeridos |
|---|---|---|
| Salida con RQ | Almacenista | `almacen.salidas.capturar`, `.registrar` |
| Vale urgente | Almacenista (autorizado) | `almacen.salidas.por-vale` |
| Devolución interna 8.A | Almacenista / Supervisor | `almacen.devoluciones-internas.capturar` |
| Conteo y ajuste | Contador (captura), aprobadores por monto | `almacen.inventarios.crear`, `.capturar`, `.aprobar-nivel1/2/3` |

## Flujo 1 — Salida normal contra RQ (`M-SAL2026-000004`)

**Objetivo:** surtir material a un área con una requisición aprobada como
respaldo (el módulo no autoriza salidas; la autorización es la RQ).

1. En `/almacen/salidas` ("Salidas"), botón **"Nueva salida"** → Sheet, variante
   **con RQ**: elegir la requisición aprobada, las líneas y cantidades a surtir
   y el sub-almacén origen (no se mezclan sub-almacenes en un folio).
2. **Registrar** → movimiento firme `M-SAL2026-000004`; decrementa el saldo y
   actualiza el cubrimiento de la RQ (una RQ puede surtirse en varias salidas).
   📸 Captura pendiente: Sheet "Nueva salida" variante con RQ.

**Errores esperados:** `SALIDA_RQ_NO_APROBADA` — "La RQ '{folio}' está en estado
'{estado}'; no acepta salida." · `SALIDA_BLOQUEADA_POR_INVENTARIO_ANUAL` — "El
sub-almacén '{...}' está en conteo anual; las salidas están bloqueadas hasta que
se aplique o rechace."

## Flujo 2 — Vale urgente + regularización (`M-SAL2026-000005`)

**Objetivo:** entregar material urgente **sin RQ previa** (línea parada, fuga),
dejando el vale auditable y la obligación de regularizarlo con una RQ dentro de
48 horas hábiles.

1. En `/almacen/salidas`, **"Nueva salida"** → variante **vale urgente**
   (requiere el permiso específico `almacen.salidas.por-vale`). Capturar
   destinatario y material; registrar → `M-SAL2026-000005` con
   `pendiente_regularizacion=true`.
2. La bandeja marca el envejecimiento del vale: insignia "Vale (Xh)" en ámbar
   antes de 24 h y **"Vale Xd!"** en rojo al exceder las 48 h del SLA. El filtro
   **"Solo vales urgentes"** los aísla.
3. **Regularizar:** el solicitante crea la RQ correspondiente (flujo P1 pasos
   1–3) y el almacenista la liga al vale → insignia "Vale ✓".
   📸 Captura pendiente: bandeja de salidas con insignias de vale.

**Errores esperados:** `MOV_NO_VALE` — "Solo aplica a salidas por vale." ·
`VALE_YA_REGULARIZADO` — "El vale ya fue regularizado." ·
`SALIDA_BLOQUEADA_POR_INVENTARIO_ANUAL` (igual que la salida normal).

## Flujo 3 — Devolución interna 8.A (`M-DEV2026-000002`)

**Objetivo:** un área regresa material que le fue surtido (sobrante, error).
Regresa al sub-almacén origen **al costo de la salida original**. No cruza con
CxP — nada que ver con la devolución a proveedor 8.B de [P3](p3-devolucion-proveedor.md).

1. En `/almacen/devoluciones`, botón **"Devolución interna (8.A)"** → Sheet:
   elegir la **salida registrada de origen**, líneas y cantidades a devolver
   (no puede exceder lo surtido) y la ubicación de entrada.
2. Material **dañado** debe dirigirse a un sub-almacén de tipo
   *Material en revisión* — no a stock disponible.
3. Aplicar → movimiento firme `M-DEV2026-000002`; el saldo se reincorpora.
   📸 Captura pendiente: Sheet "Devolución interna (8.A)".

**Errores esperados:** `DEV_INT_ORIGEN_NO_SALIDA` / `DEV_INT_ORIGEN_NO_REGISTRADA`
(el origen debe ser una salida firme) · `DEV_INT_EXCEDE_SALIDA` (no devolver más
de lo surtido) · `DEV_INT_DANADO_DEBE_IR_A_REVISION` — "El material dañado debe
enviarse a un sub-almacén MaterialEnRevision (A15)." · `ENTRADA_SIN_ASIGNACION`
si la ubicación de reingreso no tiene asignado el artículo.

## Flujo 4 — Conteo de inventario con ajuste (`M-AJN2026-000001`)

**Objetivo:** verificar la existencia física contra el teórico y ajustar las
diferencias con aprobación por monto. La captura es **ciega**: el contador no ve
la cantidad teórica (regla A6).

1. **Planificar.** En `/almacen/inventarios` ("Inventario físico"), botón
   **"Nuevo conteo"**: alcance (sub-almacén), tipo **rotativo** (no bloquea) o
   **anual** (bloquea salidas del sub-almacén mientras dura).
2. **Iniciar.** En el detalle, botón **"Iniciar"** → toma el *snapshot* del
   teórico y pasa a `En curso`.
3. **Capturar.** Pantalla `/almacen/inventarios/$id/captura`: el contador
   registra la "Cantidad real" por línea, sin ver el teórico. Todas las líneas
   deben capturarse (`CONTEO_CAPTURA_INCOMPLETA`).
4. **Conciliar y aprobar.** Pantalla `/almacen/inventarios/$id/aprobacion`:
   compara real vs teórico. Las diferencias pueden mandarse a **"Agregar
   recuento"** o **"Aprobar línea individualmente"**; el conteo completo se
   aprueba según el monto del ajuste — nivel 1 (<$1K, Almacenista), nivel 2
   ($1K–$10K, Supervisor), nivel 3 (>$10K, Jefe de Almacén + Finanzas) — o se
   pulsa **"Rechazar conteo"** para recontar.
5. **Aplicar.** Genera los movimientos de ajuste. Evidencia:
   `M-AJN2026-000001` = ajuste negativo de **1 pieza / −$150.00**.
   📸 Captura pendiente: pantalla de aprobación con diferencias.

**Errores esperados:** `CONTEO_SIN_SALDO` — "No hay saldos para el sub-almacén;
el conteo no se puede iniciar vacío." · `CONTEO_CAPTURA_INCOMPLETA` — "Todas las
líneas deben tener cantidad real capturada." ·
`CONTEO_APROBAR_LINEAS_PENDIENTES_RECUENTO` — "Las siguientes líneas requieren
recuento o aprobación individual: {...}."

## Cómo verificar el resultado

| Qué | Dónde | Qué esperar |
|---|---|---|
| Salida y vale | `/almacen/salidas`, `/almacen/saldos` | `M-SAL2026-000004` firme; `M-SAL2026-000005` con insignia de vale (✓ tras regularizar); saldos decrementados |
| Cubrimiento de la RQ | `/compras/requisiciones/$id` | La RQ surtida refleja las cantidades entregadas |
| Devolución interna | `/almacen/devoluciones` | `M-DEV2026-000002` aplicada; saldo reincorporado al costo de la salida original |
| Ajuste | `/almacen/inventarios/$id` | Conteo `Aplicado` e inmutable; `M-AJN2026-000001` visible en movimientos; saldo corregido |

**Eventos:** estos cuatro flujos no publican eventos hacia la triada (son
internos al esquema de Almacén); los saldos que alteran sí se reflejan en los
reportes del módulo (`/almacen/reportes`).
