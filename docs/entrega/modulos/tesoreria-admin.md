# Tesorería (Bancos) — Ficha de administración

## Permisos canónicos

⚠️ Los permisos de Tesorería usan el verbo **`.ver`** (no `.leer`): el rol seed
`auditor` (que junta los `.leer`) **no** cubre Tesorería — asignar los `.ver`
explícitamente a los perfiles de consulta.

| Permiso | Rol sugerido |
|---|---|
| `tesoreria.cuentas.ver`, `tesoreria.movimientos.ver` | Todos los usuarios del módulo |
| `tesoreria.movimientos.registrar` | Auxiliar de Tesorería |
| `tesoreria.movimientos.ver-cuenta-completa` | Restringido (des-enmascara CLABE/cuenta — PII, ADR-0018) |
| `tesoreria.pasivos.ver`, `tesoreria.pagos.aplicar` | Auxiliar de Tesorería |
| `tesoreria.pagos.revertir` | Jefe de Tesorería (RN-10) |
| `tesoreria.pagos-cuenta.registrar` / `.ligar` | Auxiliar registra; el gate RN-2 aplica solo |
| `tesoreria.depositos.confirmar` / `.rechazar` | Tesorería-Ingresos (RN-6) |
| `tesoreria.repp.registrar` | Auxiliar de Tesorería |
| `tesoreria.corridas.crear` / `.autorizar` / `.ejecutar` | (Sin pantalla aún) Auxiliar arma, Jefe autoriza, RN-5 |
| `tesoreria.conciliacion.operar` / `.cerrar` | (Sin pantalla aún) Auxiliar opera, Jefe cierra (RN-7) |
| `tesoreria.pasivos.solicitar-cancelacion` | Jefe de Tesorería |
| `tesoreria.reportes.ver` | Perfiles de consulta |

## Configuración previa obligatoria

1. **Cuentas bancarias**: el alta es **por script, no por UI** —
   [`backend/scripts/seed-cuentas-bancarias-tesoreria.sql`](../../../backend/scripts/seed-cuentas-bancarias-tesoreria.sql)
   (banco, número, CLABE, moneda, perfil de extracto, activa). El CRUD en
   pantalla está diferido a Administración. En dev existe la cuenta de prueba
   `0199PRUEBA01` (BBVA PRUEBA DEV).
2. **Datos bancarios del proveedor** en Datos Maestros: el evento de pasivo no
   los trae; Tesorería los resuelve por puerto de lectura al mostrar la bandeja
   y ejecutar el pago. Proveedor sin CLABE/cuenta = pago no ejecutable en banca.
3. **Catálogo `ConceptoMovimiento`** con clasificación de flujo
   (Operación/Inversión/Financiamiento) — alimenta el reporte de flujo de
   efectivo.
4. **Matriz de autorización de corridas** (umbral por monto, suplencias) —
   pendiente de definición con el Jefe de Tesorería (gate T-G4) antes de
   habilitar corridas.
5. Periodos contables: el puerto `IPeriodoContablePort` es hoy un stub
   siempre-abierto (PLATFORM-TODO); al conectarse a Contabilidad, `MOV_PERIODO_CERRADO`
   empezará a aplicar de verdad.

## Eventos que publica / consume

| Dirección | Evento | Con quién | Efecto operativo |
|---|---|---|---|
| Publica | `PagoFacturaProveedorEvent` (`pago-factura-proveedor.aplicado.v1`, uno **por factura** — RN-4) | CxP (marca `Pagada`), Compras (sub-estado Pago — ⚠️ listener pendiente) | Al registrar el pago |
| Publica | `repp-proveedor.recibido.v1` | CxP | Libera el motivo `FALTA_REPP` |
| Publica | `PagoClienteConfirmadoEvent` (RN-6) | CxC / Facturación | Dispara la emisión del REPP de cliente |
| Consume | `PasivoAutorizadoParaPagoEvent` | `cuentas-por-pagar-events` | Alimenta la bandeja de pagos |
| Consume | `propuesta-aplicacion.creada.v1` (CxC) y expectativas de Caja | — | Alimenta "Depósitos por confirmar" |

## Monitoreo y troubleshooting

- **Bandeja de pagos vacía con facturas autorizadas en CxP**: revisar el outbox
  de CxP y los dead-letters del topic `cuentas-por-pagar-events`; existe el
  script de respaldo
  [`backend/scripts/backfill-pasivos-pendientes-tesoreria.sql`](../../../backend/scripts/backfill-pasivos-pendientes-tesoreria.sql)
  para reproyectar pasivos.
- **REPP SLA 5 días**: la bandeja `/tesoreria/repp` calcula los días sin
  complemento (constante `SlaDias = 5`); el filtro "Solo vencidos" es la vista
  de trabajo diaria. Hallazgo menor abierto: pagos del mismo día pueden mostrar
  `diasSinRepp: -1` por el cálculo UTC vs hora local — cosmético.
- **Factura pagada que no se marca `Pagada` en CxP**: dead-letters de
  `cuentas-por-pagar-tesoreria-sub` y log del `TesoreriaEventListenerWorker`
  (en CxP) en Application Insights.
- **Depósito que no encuentra su movimiento**: la confirmación exige ingreso
  identificado con moneda y monto exactos (RN-6); si el banco recibió otro
  importe, el flujo correcto es **rechazar** para que CxC re-proponga.

## Límites conocidos vigentes

- **Corridas de pago y conciliación bancaria sin pantalla** (placeholder en
  `/tesoreria/corridas` y `/tesoreria/conciliacion`): los permisos y el diseño
  existen; la operación llegará en los PRs pendientes (gates T-G4 y T-G2/T-G8).
  Mientras tanto: pagos individuales y conciliación manual fuera del sistema.
- **Pagos cross-moneda bloqueados** (RN-3) en MVP.
- Alta/edición de cuentas bancarias **solo por runbook SQL** (ver arriba).
