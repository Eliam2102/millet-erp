# 15 · Conciliación de auxiliares contra cuentas de control (F1-CON-09)

Reporte de Contabilidad que compara, a una **fecha de corte**, el saldo de cada **cuenta de control** contra el total de su módulo. Es la base del checklist de cierre (CON-12), que no se implementa aquí.

## Controles

| Control | Saldo contable | Saldo del auxiliar (módulo dueño) | Detalle |
|---|---|---|---|
| Clientes | Cuentas marcadas `CuentaControl = Clientes` y la cuenta del rol `CLIENTES` (y sus variantes `CLIENTES_*`). Saldo deudor | CxC: total de cartera al corte, por moneda | Solo totales. El detalle de cobros contra pólizas es *Cuentas por cobrar › Conciliación con pólizas* y se enlaza |
| Proveedores | `CuentaControl = Proveedores` y rol `PROVEEDORES`. Saldo acreedor | CxP: saldo histórico por factura al corte (`SaldosHistoricos`), en MXN con el TC del documento | Por proveedor, facturas sin póliza (CON-09-a) y partidas sin auxiliar |
| Bancos | Cuenta de cada `CuentaBancaria.CuentaContableRef` y rol `BANCOS` (y `BANCOS_*`). Saldo deudor | Tesorería: saldo inicial + movimientos con fecha valor ≤ corte, por cuenta bancaria | Por cuenta bancaria (partidas con esa cuenta bancaria contra libros) y partidas sin cuenta bancaria |

Estados: **Cuadra** (diferencia ≤ 0.01), **Diferencia**, **Sin cuenta configurada** (no compara; indica el rol que falta y cuántos rechazos `RolSinCuenta` siguen pendientes) y **No comparable** (filtro o moneda que el auxiliar no puede respetar).

## Cómo se obtiene cada saldo

- Contable: partidas de pólizas no borrador con fecha ≤ corte, sobre las cuentas del control, en el propio esquema de Contabilidad.
- Auxiliares: puertos de consumidor en `Contabilidad/Application/ConciliacionAuxiliares` (`IAuxiliarProveedoresReadPort`, `ISaldoCarteraAlCorteReadPort`, `ISaldosBancariosAlCorteReadPort`), implementados por CxP, CxC y Tesorería en su `Infrastructure/PublicAdapters` y registrados en `Program.cs`. Ningún módulo lee tablas de otro.
- Documento sin póliza (CON-09-a): factura de CxP con saldo al corte sin póliza `cuentas_por_pagar / factura_proveedor` con fecha ≤ corte. La causa es el rechazo pendiente del motor si existe, la fecha de la póliza si quedó después del corte, o «el evento no generó póliza».

## API

- `GET /api/v1/contabilidad/reportes/conciliacion-auxiliares?corte&sucursalId` → resumen por control.
- `GET /api/v1/contabilidad/reportes/conciliacion-auxiliares/{proveedores|bancos}?corte&sucursalId` → diferencias (forma ADR-0036).
- Permiso `contabilidad.polizas.leer`, como la cuenta puente. Contabilidad es corporativa: la sucursal es un filtro opcional.
- Puerto público `IConciliacionAuxiliaresResumenReadPort` para que el cierre (CON-12) pregunte si todo cuadra.

## Supuestos a validar

1. Precedencia de cuentas: marcadas con `CuentaControl`, luego las del rol del motor y, para bancos, la de cada cuenta bancaria.
2. Sin cuenta configurada no se compara (evita un falso «Cuadra»).
3. Comparación en MXN. La cartera de CxC en moneda extranjera y las cuentas bancarias en moneda extranjera no se convierten: se informan en notas.
4. La cartera de CxC no guarda la fecha de cancelación: una factura cancelada no cuenta en ningún corte.
5. Con filtro de sucursal, clientes y bancos quedan «No comparable»: la cartera y los libros no se separan por sucursal.
6. Se reutiliza `contabilidad.polizas.leer`; el reporte se calcula a demanda, sin historial.

## Fuera de alcance (siguiente entrega)

- Inventario (valuación al corte de Almacén) y activos fijos (libro al corte) contra sus cuentas.
- Conciliación bancaria contra estado de cuenta (Tesorería T9.2), ingresos y pasivos contra CFDI (CON-12) y revaluación.
