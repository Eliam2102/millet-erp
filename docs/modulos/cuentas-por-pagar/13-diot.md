# DIOT · Declaración informativa de operaciones con terceros (F1-CXP-10)

> Tarea 86e3jvva3. Rev 1.0, 7-nov-2026. Primera entrega: proveedores nacionales por pagos.

## 1. Qué resuelve

La DIOT es una declaración mensual: informa al SAT lo pagado a cada proveedor, separado por tasa de IVA. Hasta ahora Contabilidad la armaba en un papel de trabajo fuera del ERP. Con esta entrega, CxP calcula la DIOT del mes y genera el archivo de carga masiva del SAT.

- Pantalla: CxP › Reportes › DIOT (`/cxp/reportes/diot`), con el permiso `cuentas_por_pagar.reportes.diot`. El permiso ya existía y no se crean permisos nuevos.
- La DIOT es de la empresa, así que no se filtra por sucursal.
- Endpoints:
  - `GET /api/v1/cuentas-por-pagar/reportes/diot?anio=&mes=` devuelve el reporte con la forma estándar de ADR-0036. La exportación a Excel es la del `ReporteShell`.
  - `GET /api/v1/cuentas-por-pagar/reportes/diot/archivo?anio=&mes=` devuelve el TXT de carga (`DIOT_AAAAMM.txt`).

## 2. Base: flujo de efectivo

Cuenta lo pagado en el mes, no lo facturado. Es el mismo criterio de la liquidación de IVA (`docs/modulos/contabilidad/15-iva-flujo-efectivo-y-liquidacion.md`) y el que usa el papel de trabajo de Contabilidad.

| Movimiento | Fuente | Fecha que cuenta |
|---|---|---|
| Pago de Tesorería | `pagos_proveedor_local`, sin los revertidos | Fecha del pago |
| Pago sin Tesorería (tarjeta corporativa) | Movimiento de pago del pasivo, neto del mes | Fecha del movimiento |
| Nota de crédito de descuento o de devolución, no cancelada | `NotaCreditoProveedor` | Fecha del CFDI |

- No cuentan las facturas canceladas ni los anticipos.
- Las notas de amortización de anticipo (tipo 07) tampoco cuentan.

## 3. Cálculo por pago

En cada pago se prorratean los importes de la factura según la parte que cubre el pago:

- **Factor:** importe aplicado ÷ total de la factura, convertido a pesos.
- **Tipo de cambio:**
  - Pesos: 1.
  - Otra moneda: el tipo de cambio del pago que manda Tesorería, que ahora se guarda en `pagos_proveedor_local`.
  - Si el pago no lo trae, por ejemplo un pago anterior a este cambio, se usa el de la factura.
- **Importes:**
  - IVA pagado = IVA de la factura × factor.
  - IVA retenido = retenciones con impuesto `002` × factor.
  - IVA no acreditable = IVA del gasto no deducible (P8.6) × factor.
- **Tasa:** el CFDI recibido no se guarda desglosado por tasa, así que se deduce de IVA ÷ base:
  - Cerca de 8 %: región fronteriza norte, con base = IVA ÷ 0.08.
  - Cualquier otra: 16 %, con base = IVA ÷ 0.16.
  - Lo que queda de la base sin IVA va a tasa 0 %.
- **Notas de crédito:** van en «devoluciones, descuentos y bonificaciones» y restan IVA.

Se arma un renglón por tercero y tipo de operación, con centavos. El archivo los redondea a pesos enteros, como el que presenta Contabilidad.

### Tipo de tercero y de operación

| Dato | Regla |
|---|---|
| Tipo de tercero | `05` si el proveedor es extranjero en el catálogo; `15` si su RFC es el genérico `XAXX010101000`; si no, `04` |
| Tipo de operación | Por las claves de producto de la factura: todas de arrendamiento de inmuebles (`8013xxxx`) = `06`; todas de servicios (división 70 en adelante) = `03`; alguna de bienes = `02`; sin claves = `85` |

## 4. Archivo de carga (layout de 54 campos)

- Un renglón por tercero y tipo de operación, con campos separados por `|`.
- Codificación UTF-8 sin BOM y fin de línea CRLF.
- Se llenan los mismos campos que el archivo que Contabilidad presentó en julio de 2026, que el SAT aceptó:

| Campo | Contenido |
|---|---|
| 1 | Tipo de tercero |
| 2 | Tipo de operación |
| 3 | RFC (nacional y global) |
| 4–6 | ID fiscal, nombre y país alfa-3 (extranjero) |
| 8 / 9 | Actos pagados y devoluciones de la región fronteriza norte (8 %) |
| 12 / 13 | Actos pagados y devoluciones al 16 % |
| 14 | Importación de bienes al 16 % (`0` en esta entrega) |
| 48 | IVA retenido |
| 50 | Exentos (`0`) |
| 51 | Actos pagados al 0 % |
| 54 | Manifestación de efectos fiscales, `01` |

- Los campos 18 a 47 (IVA acreditable y no acreditable) van vacíos, igual que en el archivo de julio.
- No se escribe un renglón sin montos.

## 5. Fuera de esta entrega

1. **Importaciones:** extranjeros por pedimento, con el IVA pagado en aduana (campo 14, operación `07`). Lo pide el criterio CXP-10-c.
   - Hoy los extranjeros salen en la pantalla y el Excel, pero no en el archivo, porque Contabilidad los declara por la importación.
   - Necesita un puerto de lectura de Comercio Exterior para el IVA de importación de cada expediente.
2. **Caja chica y viáticos:** sus facturas no tienen pago propio, porque se paga la reposición. Falta definir qué fecha cuenta.

## 6. Supuestos a validar con Contabilidad

1. **Tipo de operación:** se toma de las claves de producto (§3). Si hace falta fijarlo por proveedor, se agrega un catálogo.
2. **Tasa:** se deduce de IVA ÷ base. Exento y tasa 0 % no se distinguen sin el desglose del CFDI, así que todo va a 0 %.
3. **IVA no acreditable:** aparece en la pantalla y el Excel (CXP-10-b). En el archivo, los campos 18 a 47 van vacíos, como en julio. Falta confirmar si Contabilidad lo captura en la plataforma del SAT.
4. **Notas de crédito:** cuentan en el mes de su CFDI.
5. **Retención de IVA en facturas sin desglose del CFDI** (captura manual): no se reporta.
