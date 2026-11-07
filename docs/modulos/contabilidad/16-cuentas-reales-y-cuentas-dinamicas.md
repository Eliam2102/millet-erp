# Cuentas reales del motor contable y cuentas dinámicas (86e3za6ck)

**Fuente:** el 5-nov, Contabilidad respondió la tabla «Cuentas por operación» y envió el catálogo actualizado al 5-nov. En este documento, «renglón N» es el número de fila de esa tabla.

**Qué cubre este documento:**
- qué cuentas se dan de alta y cómo;
- qué roles del motor quedan con su cuenta real;
- qué cuentas no son fijas (dependen del artículo, del banco, del servicio o del régimen) y qué soporta hoy el motor para configurarlas.

## 1. Cuentas nuevas del catálogo

Comparado con la carga final del 29-oct, el catálogo del 5-nov agrega 9 cuentas y no quita ninguna. En las cuentas 801 a 899 solo se movió la naturaleza a la columna «Falta confirmar»; el valor es el mismo.

| Código | Nombre | Nivel | Naturaleza | Agrupador SAT | Uso |
|---|---|---|---|---|---|
| 101.01.03.00 | COBROS TD/TC | 3 | Deudora | — | Cobro en caja con tarjeta (renglón 34) |
| 101.01.04.00 | CHEQUES SBC EN CAJA COBRANZA | 3 | Deudora | — | Cobro en caja con cheque (renglón 38) |
| 201.05.00.00 | COMPRAS | 2 | Acreedora | 201.01 | Cuenta puente recepción–factura (renglones 2, 3, 5, 12 y 16) |
| 201.06.00.00 | COMPRAS ACTIVO FIJO | 2 | Acreedora | 201.01 | Cuenta puente de la compra de activo fijo (renglones 6 y 57) |
| 504.25.02.00 | MERMAS | 3 | Deudora | — | Faltante, merma y sobrante de inventario (renglones 50, 51 y 54) |
| 601.73.01.00 | HONORARIOS ADUANALES PERSONAS FÍSICAS | 3 | Deudora | — | Gasto de importación |
| 601.73.02.00 | HONORARIOS ADUANALES PERSONAS MORALES | 3 | Deudora | — | Gasto de importación |
| 704.23.01.00 | INDEMNIZACIONES POR SEGUROS | 3 | Acreedora | — | Otros productos |
| 704.23.02.00 | REVALUACION DE ACTIVOS FIJOS | 3 | Acreedora | — | Revaluación de activo fijo (renglón 61) |

### Cómo se dan de alta

Con el importador del catálogo, igual que la carga de CON-01: *Contabilidad › Catálogo › Importar* con el archivo del 5-nov.
- La importación es incremental y no destructiva. Crea solo las cuentas nuevas; las demás quedan «sin cambios», y una celda vacía conserva el valor existente.
- El lote pasa por la autorización del catálogo.
- No se usa una migración porque el alta de cuentas debe quedar autorizada y en la bitácora del catálogo, como cualquier otra carga.

**Efecto en las cuentas padre (regla P20):** `601.73.00.00 GASTOS DE IMPORTACION` y `704.23.00.00 OTROS PRODUCTOS` hoy reciben movimientos. Al recibir sus primeras hijas pasan a acumular, y el importador lo avisa en la vista previa. Si alguna ya tiene movimientos, el importador no la convierte: Contabilidad decide si reclasifica esos movimientos antes de importar.

## 2. Roles del motor con la cuenta confirmada

El script `backend/scripts/contabilidad-asignar-cuentas-roles.sql` asigna estas cuentas. Se corre después de importar el catálogo.

**Cómo se comporta el script:**
- Es idempotente.
- Solo toca el rol base (sin sucursal ni moneda), y solo si está sin cuenta o con una cuenta de prueba (`990.*` o `DEMO-*`). Nunca pisa una cuenta real que Contabilidad ya haya asignado en *Roles de cuenta*.
- La cuenta destino debe existir, estar activa y ser afectable.
- Cada asignación deja su renglón en la bitácora del rol.
- Al final imprime qué pasó con cada rol: asignada ahora, ya estaba, falta la cuenta, o no se tocó porque ya tenía otra cuenta real.

| Rol | Cuenta | Renglones |
|---|---|---|
| MERCANCIA_RECIBIDA_POR_FACTURAR | 201.05.00.00 COMPRAS | 2, 3, 5, 12 y 16 |
| IVA_ACREDITABLE_PENDIENTE | 119.01.00.00 IVA PENDIENTE DE PAGO | 7, 13, 17 y S4 |
| PROVEEDORES | 201.01.00.00 PROVEEDORES NACIONALES | 8, 10, 14 y 20 |
| ANTICIPO_A_PROVEEDORES | 120.01.00.00 ANTICIPO A PROVEEDORES NACIONAL | 18 |
| CLIENTES | 105.01.00.00 CLIENTES NACIONALES | 22, 31, 33, 35, 37, 39 y 45 |
| VENTAS | 401.01.00.00 VENTAS Y/O SERVICIOS GRAVADOS A LA TASA GENERAL | 24 |
| IVA_TRASLADADO_NO_COBRADO | 209.01.00.00 IVA TRASLADADO NO COBRADO | 25, 28, 30 y S5 |
| ANTICIPOS_DE_CLIENTES | 206.01.00.00 ANTICIPO DE CLIENTE NACIONAL | 27 y 32 |
| DESCUENTOS_SOBRE_VENTAS | 402.01.00.00 DEVOLUCIONES DESC BONIF SOBRE VENTAS… | 29 |
| COBROS_TC_TD | 101.01.03.00 COBROS TD/TC | 34 |
| COBROS_CHEQUE | 101.01.04.00 CHEQUES SBC EN CAJA COBRANZA | 38 |
| MERMA | 504.25.02.00 MERMAS | 51 |
| AJUSTE_INVENTARIO | 504.25.02.00 MERMAS | 50 y 54 |
| IVA_ACREDITABLE_PAGADO | 118.01.01.00 IVA 16% PAGADO | S3 y S8 |
| COMISIONES_BANCARIAS | 701.10.00.00 COMISIONES BANCARIAS | S7 |

**Notas de Contabilidad:**
- **Merma:** «la cuenta de DESPERDICIO tiene otro uso», así que la merma no va a 504.25.01.00.
- **IVA de la comisión bancaria:** va a 118.01.01.00 (IVA pagado), porque el banco ya la cobró.
- **Factura de proveedor:** se registra sin descontar retenciones; el «descuento» se hace al pagar (renglón 8). Es parte de la regla de retenciones pendiente (§4).

### Después de correr el script

En *Contabilidad › Motor › Rechazos*, filtrar por causa «Rol sin cuenta» y reprocesar cada rechazo. El reproceso corre la plantilla vigente con el mismo evento y la misma llave, así que no duplica pólizas.

## 3. Roles que no se tocan

| Rol | Por qué |
|---|---|
| EFECTIVO_CAJA_COBRANZA, CAJA | Contabilidad dice que el corte de caja no genera asiento (renglones 42 y 43). Sigue pendiente decidir si la póliza del efectivo se hace por cobro o por corte. La cuenta 101.01.02.00 está confirmada para el depósito al banco (S12) |
| DEUDORES_FUNCIONARIOS_EMPLEADOS | Faltante o sobrante del arqueo (S13): Contabilidad no respondió |
| RETENCIONES_POR_PAGAR, RETENCIONES_CLIENTES | Rol base. Desde 86e3zcbjw cada concepto tiene su rol (§4.1) y su script |
| BANCOS, BANCOS_MN, GASTO, CONSUMO, INVENTARIO | Dependen de otro catálogo (§4) |
| COBROS_OTRAS_FORMAS | Las cajas de sucursal no aceptan otra forma de pago (renglones 40 y 41) |

## 4. Cuentas dinámicas: de dónde salen y qué soporta hoy el motor

El motor resuelve la cuenta de cada partida así:
1. **Cuenta propia de la línea.** Algunos eventos traen la cuenta de cada línea, por ejemplo la devolución interna usa la de la salida original.
2. **Rol por clasificador** (`{ROL}_{CLASIFICADOR}`), si la plantilla lo pide y ese rol tiene cuenta. Si no, se usa el rol base.
3. **Rol base:** el más específico vigente. Va primero el de la sucursal y moneda exactas, y al final el genérico.

| Cuenta | Lo que pidió Contabilidad | Qué soporta hoy | Qué falta |
|---|---|---|---|
| Inventario (renglones 1, 4, 47, 48, 52 y 53) | Por tipo de material, definido en el catálogo de artículos; la devolución, a la misma cuenta por la que entró | ✅ `INVENTARIO_{grupo del artículo}` (F1-ALM-13, #446) para recepción, devolución, ajuste, salida y devolución interna | Contabilidad habla de «el almacén del cual sale»; hoy la cuenta va por grupo de artículo, no por almacén. Confirmar que el grupo basta |
| Consumo de almacén (renglón 46) | Por artículo | ⚠️ Un solo rol `CONSUMO`, con centro de costo | Plantilla #14 con rol por clasificador (`CONSUMO_{grupo}`) y una cuenta de consumo por grupo de artículo |
| Gasto por servicio (renglón 6) | Grupo 601 según el tipo de gasto o servicio; activo fijo a 201.06 | ⚠️ Un solo rol `GASTO`, con centro de costo | Clasificar la línea de servicio (concepto de gasto o artículo de servicio) y usar `GASTO_{tipo}`. La línea de activo fijo ya va a la cuenta puente de su clase (§5) |
| Bancos (renglones 19, 21, 36, 44, S10 y S11) | La cuenta del banco desde el que se paga o en el que se deposita | ⚠️ Un solo rol `BANCOS`/`BANCOS_MN`, que solo varía por sucursal o moneda | La cuenta bancaria de Tesorería debe llevar su cuenta contable y el evento mandarla en la línea. Ya existe el mecanismo de cuenta propia de la línea |
| Retenciones de proveedor (renglones 9, 11 y 15) | Por concepto fiscal del proveedor, desde su perfil | ✅ `RETENCIONES_POR_PAGAR_{concepto}_{impuesto}`; se abona al pagar (§4.1) | Cuentas que faltan en el catálogo (§4.1) |
| Retenciones de cliente (renglón 23) | Fletes → 113.01.02.01; servicios → 113.01.02.03; desperdicio → 113.01.02.05 | ✅ `RETENCIONES_CLIENTES_{concepto}_IVA`, concepto elegido en la línea de la factura | Cuenta de importación 16 % |
| Ventas | 401.01.00.00 | ✅ `VENTAS` y `VENTAS_{tipo de producto}` por línea | — |
| Nacional / extranjero (proveedores, clientes y anticipos) | 201.01 / 201.02, 105.01 / 105.02, 120.01 / 120.02, 206.01 / 206.02 | ⚠️ El rol puede filtrarse por moneda: dar de alta `PROVEEDORES` con moneda USD → 201.02 cubre al proveedor extranjero que factura en dólares | La moneda no es lo mismo que la nacionalidad: un extranjero en MXN caería en la cuenta nacional. Falta un criterio por tipo de tercero (clasificador del proveedor o cliente) |
| Costo de ventas (S1 y S2) | 501.01.00.00 contra 115.04.00.00 | ❌ No hay plantilla ni rol; el evento aún no existe | Cuando Producto terminado o Facturación publiquen el costo de la entrega facturada |
| Redondeo de la comisión bancaria (S9) | 703.21.00.00 OTROS GASTOS | ❌ No hay rol | Agregarlo a la plantilla de la comisión cuando se construya la conciliación TPV |
| Liquidación mensual de IVA (S14) | Manual | — | Póliza manual (dicho por Contabilidad) |

### 4.1 Retenciones por concepto (86e3zcbjw)

Fuente: tabla de retenciones de Contabilidad (5-nov) y su respuesta del 6-nov.

- **Proveedor:** la factura se registra por el total (proveedor = CFDI + retención) y no abona retenciones. Cada pago carga al proveedor el pago más la retención que le toca (proporcional; el pago que salda toma el remanente), abona bancos por el pago y abona la retención a `RETENCIONES_POR_PAGAR_{concepto}_{impuesto}`. En el mismo pago el IVA pasa de pendiente a pagado (C1.6). Las facturas contabilizadas antes conservan su retención en la factura y su pago no la vuelve a abonar.
- **Por línea:** la retención sale del CFDI, que la trae por concepto. El parser guarda la tasa y la base sobre la que se retuvo; la alerta de retenciones compara contra esa base y no contra toda la factura.
- **Cliente:** cada línea de la factura de venta lleva su concepto (fletes 4 %, servicios 6 %, desperdicio 16 %, importación 16 %). La póliza carga cada retención a `RETENCIONES_CLIENTES_{concepto}_IVA`, y la liquidación de IVA la abona a esa misma cuenta.
- **Sin cuenta en el rol del concepto**, el motor usa el rol base.
- **Script:** `backend/scripts/contabilidad-asignar-cuentas-retenciones.sql`.

| Rol | Cuenta (catálogo 5-nov) |
|---|---|
| RETENCIONES_POR_PAGAR_HONORARIOS_PF_ISR | 216.04.00.00 |
| RETENCIONES_POR_PAGAR_HONORARIOS_PF_IVA | 216.10.01.00 |
| RETENCIONES_POR_PAGAR_ARRENDAMIENTO_PF_ISR | 216.03.00.00 |
| RETENCIONES_POR_PAGAR_FLETES_IVA | 216.10.02.00 |
| RETENCIONES_CLIENTES_FLETES_IVA | 113.01.02.01 |
| RETENCIONES_CLIENTES_SERVICIOS_IVA | 113.01.02.03 |
| RETENCIONES_CLIENTES_DESPERDICIO_IVA | 113.01.02.05 |
| RETENCIONES_POR_PAGAR_ARRENDAMIENTO_PF_IVA, _SERVICIOS_IVA, _RESICO_PF_ISR, _RESICO_PF_SERVICIOS_ISR, _RESICO_PF_SERVICIOS_IVA; RETENCIONES_CLIENTES_IMPORTACION_IVA | Sin cuenta en el catálogo del 5-nov: pendiente con Contabilidad |

## 5. Activos Fijos por clase

Las cuentas de cada clase se configuran en *Activos Fijos › Clases*. Cambiar una cuenta crea una propuesta con vigencia que autoriza otra persona, por eso **no se asignan por script**: se capturan ahí con estos valores.

| Clase | Activo | Depreciación acumulada | Gasto por depreciación | Puente de alta |
|---|---|---|---|---|
| 151 Terrenos | 151.01.00.00 | No se deprecia | No se deprecia | 201.06.00.00 |
| 152 Edificios | 152.01.00.00 | 171.01.00.000 | 613.01.00.00 | 201.06.00.00 |
| 153 Maquinaria y equipo | 153.01.00.00 | 171.02.00.000 | 613.02.00.00 | 201.06.00.00 |
| 154 Automóviles… | 154.01.00.00 | 171.03.00.000 | 613.03.00.00 | 201.06.00.00 |
| 155 Mobiliario y equipo de oficina | 155.01.00.00 | 171.04.00.000 | 613.04.00.00 | 201.06.00.00 |
| 156 Equipo de cómputo | 156.01.00.00 | 171.05.00.000 | 613.05.00.00 | 201.06.00.00 |
| 157 Equipo de comunicación | 157.01.00.00 | 171.06.00.000 | 613.06.00.00 | 201.06.00.00 |
| 159 Obras en proceso | 159.01.00.00 | No se deprecia | No se deprecia | 201.06.00.00 |
| 160 Otros activos fijos | 160.01.00.00 | 171.08.00.000 | 613.08.00.00 | 201.06.00.00 |
| 165 Comunicaciones telefónicas | 165.01.00.00 | 171.13.00.000 | **613.06.00.00** (Contabilidad, 5-nov) | 201.06.00.00 |
| 166 Comunicación satelital | 166.01.00.00 | 171.14.00.000 | **613.06.00.00** (Contabilidad, 5-nov) | 201.06.00.00 |
| 169 Otra maquinaria y equipo | 169.01.00.00 | 171.18.00.000 | 613.18.00.00 | 201.06.00.00 |
| 170 Adaptaciones y mejoras | 170.01.00.00 | 171.17.00.000 | 613.17.00.00 | 201.06.00.00 |

Las cuentas 171 llevan un dígito más en el último segmento (`171.01.00.000`), igual que en el catálogo de Millet.

En la configuración del libro de Activos Fijos:

| Cuenta | Valor | Qué falta |
|---|---|---|
| Revalorización | 704.23.02.00 REVALUACION DE ACTIVOS FIJOS (renglón 61) | — |
| Puente de venta | No aplica (renglón 64): al facturar la venta, el cargo va al cliente | Revisar que la venta del activo funcione sin esta cuenta (hoy existe el campo) |
| Utilidad y pérdida en venta o baja (renglones 65 y 66) | Contabilidad las quiere **por clase** | Hoy es una sola cuenta de utilidad y una de pérdida para toda la empresa. Se necesita un campo por clase |

## 6. Pendientes con Contabilidad (mismo hilo del 5-nov)
- Póliza del efectivo de caja: por cobro o por corte.
- Cuentas de retención que faltan en el catálogo (§4.1).
- Área y equipo por omisión (tarea H1, 86e3z5w25).
- Faltante o sobrante del arqueo de caja (S13).
