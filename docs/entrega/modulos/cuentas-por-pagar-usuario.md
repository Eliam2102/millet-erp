# Cuentas por Pagar — Ficha de usuario

## Qué hace el módulo

Administra el ciclo del pasivo con proveedores: recibe los CFDI (repositorio),
captura facturas conciliándolas contra OC y recepción (three-way match con
tolerancia por proveedor), maneja la revisión por área, anticipos (serie FANT),
notas de crédito del proveedor, notas de cargo internas, comprobaciones de
gastos, viáticos y tarjetas de crédito empresariales. **No** ejecuta pagos (los
autoriza y los entrega a Tesorería), no genera OCs, no recibe mercancía y no
registra el REPP del proveedor (eso lo hace Tesorería).

## Quién lo usa

| Rol | Qué hace aquí |
|---|---|
| Auxiliar de CxP | Captura todas las facturas, NC, anticipos (el CFDI), notas de cargo; libera comprobaciones |
| Comprador | Deposita CFDIs en el repositorio; NO captura facturas; corrige OCs rechazadas |
| Encargado de área | Libera facturas/proveedores en revisión de su área |
| Dirección General / Dirección de Finanzas | Autoriza anticipos, notas de cargo y segundas firmas (aduanales, viáticos) |
| Responsable de CxP | Autorización manual de facturas (override), ajuste de tolerancias |

## Operaciones principales

| Operación | Pantalla | Pasos resumidos |
|---|---|---|
| Gestionar CFDIs recibidos | `/cxp/cfdis` ("CFDIs recibidos") → **"Cargar CFDI"** | Canal de respaldo manual; marcar duplicado o **"Descartar"** con motivo |
| Capturar factura contra OC | `/cxp/facturas` → **"Capturar factura"** | Sheet "Capturar factura desde OC": elegir OC + CFDI del picker → concilia 3-way → `Capturada` |
| Autorizar / enviar a revisión | `/cxp/facturas/$id` → **"Autorizar"** / **"Enviar a revisión"** | La OC autorizada autoriza el pasivo; revisión por dependencia con liberación del área |
| Capturar anticipo | `/cxp/anticipos` → **"Capturar anticipo"** | CFDI serie FANT; se amortiza desde el detalle de la factura destino |
| Capturar nota de crédito | `/cxp/notas-credito` → **"Capturar NC"** | CFDI Egreso con relación 01/03/07; la rel. 03 concilia devoluciones 8.B |
| Notas de cargo | `/cxp/notas-cargo` → **"Crear nota de cargo"** | Interna, no fiscal; Dirección autoriza → aplicar → formalizar con NC fiscal |
| Revisión por área | `/cxp/revision` | Bandeja de facturas retenidas; **liberar** cuando el área resuelve |
| Reportes | `/cxp/reportes/antiguedad`, `/cartera`, `/antiguedad-anticipos`, `/diot`... | Ejecutar con filtros y exportar |

## Flujos internos de control

Los tres bloques de gasto interno que no pasan por OC tienen guion propio en
[P7 — Gastos internos de CxP](../pipelines/p7-cxp-gastos-internos.md):

- **Comprobaciones de gastos** (`/cxp/comprobaciones`): caja chica (aprueba el
  responsable de sucursal) y aduanales (**doble firma**: Comercio Exterior +
  Dirección de Finanzas, personas distintas).
- **Viáticos** (`/cxp/viaticos`): préstamo al empleado validado contra política
  por puesto y destino; jefe directo autoriza, DF cuando excede política;
  comprobación al regreso liberada por el Auxiliar.
- **Tarjetas de crédito empresariales** (`/cxp/tc/*`): cada CFDI se registra
  individual; el corte cierra el estado de cuenta y genera el pasivo agregado
  contra el banco; disputas y refunds controlados.

Además:

- **Proveedor en revisión global**: retiene automáticamente todas las facturas
  nuevas del proveedor hasta liberarlo (permisos `poner-revision` /
  `liberar-revision`).
- **Revisión por área con SLA**: 5 días hábiles con escalamientos (día 3, 5 y
  10); la bandeja es `/cxp/revision`.

## Ejemplos con folios reales

- **Factura `P1-001`** ($1,350.00) — capturada desde CFDI contra
  `OC-MID2026-000020`, autorizada y pagada:
  [P1](../pipelines/p1-flujo-feliz-insumos.md).
- **Factura `P2-001`** ($1,000.50) — conciliada con diferencia de $0.50 dentro
  de tolerancia contra `OC-MID2026-000021`:
  [P2](../pipelines/p2-materiales-directos.md); con el anticipo `ANT-P5`
  amortizado dejó saldo neto de $500.50: [P5](../pipelines/p5-anticipos.md).
- **Nota de cargo `NCG-2026-000001`** ($125.00) — generada automáticamente por
  la devolución `M-DEV2026-000001` y formalizada con la NC fiscal rel. 03:
  [P3](../pipelines/p3-devolucion-proveedor.md).

## Errores comunes (en lenguaje de negocio)

- **La factura "desaparece" al capturarla** — si la diferencia contra la OC
  excede la tolerancia del proveedor, la factura queda **Cancelada** de
  inmediato con motivo *Rechazada por tolerancia* y el detalle de la diferencia.
  No es una falla: el comprador debe corregir la OC o el proveedor refacturar
  ([P4](../pipelines/p4-rechazo-tolerancia.md)).
- **"Ya existe un anticipo con UUID '...'."** — ese CFDI FANT ya está capturado.
- **"La serie del CFDI de anticipo debe ser 'FANT'..."** — el proveedor emitió
  el anticipo con otra serie; pedir corrección.
- **"El UUID de la factura origen (relación CFDI) es obligatorio..."** — toda NC
  debe relacionar la factura que afecta (relación 01, 03 o 07).
- **"Una factura pagada no puede cancelarse en este flujo. Coordinar con
  Tesorería."** — el reverso de un pago va por Tesorería, no por cancelación.
- **"Aplicar {monto} dejaría el saldo negativo..."** — la NC/anticipo excede el
  saldo pendiente de la factura; aplicar parcial.
- **La factura no se puede editar** — autorizada es inmutable: cancelar (si
  procede) y recapturar.
