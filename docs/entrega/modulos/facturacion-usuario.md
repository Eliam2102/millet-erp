# Facturación — Ficha de usuario

## Qué hace el módulo

Emite los CFDI 4.0 de venta de Millet y todo lo que cuelga de ellos: facturas
de venta (PUE/PPD), facturas de anticipo (serie FANT, relación 07), notas de
crédito (bonificación/ranura relación 01, amortización de anticipos relación
07), complementos de pago (REPP / Pago 2.0), Carta Porte 3.1 y facturas de
exportación con complemento de Comercio Exterior (CCE 2.0). Opera la **caja**
de mostrador (sesión de efectivo, cobros, arqueo y cierre) y recibe los
**pedidos facturables** desde A+W o por captura manual. **No** decide la
cobranza ni la antigüedad de saldos (eso es de CxC), no ejecuta pagos ni
concilia bancos (Tesorería), y el timbrado real lo hace el PAC (FiscalAPI) —
el módulo arma el comprobante y enruta.

## Quién lo usa

| Rol | Qué hace aquí |
|---|---|
| Cajero | Abre su sesión de caja, factura pedidos de mostrador, cobra en efectivo/tarjeta, arquea |
| Facturista | Emite facturas, anticipos, notas de crédito, REPP y Carta Porte; captura pedidos manuales |
| Facturista de exportación | Emite facturas USD con complemento CCE 2.0 |
| Supervisor de caja | Autoriza apertura de caja ajena, reabre sesión en arqueo, cancela cobros, cierra liquidaciones |

## Operaciones principales

| Operación | Pantalla | Pasos resumidos |
|---|---|---|
| Abrir caja | `/facturacion/caja` (Mi Caja) | **"Abrir sesión"** con fondo de apertura → genera movimiento `FondoApertura` |
| Facturar un pedido | `/facturacion/facturas/nueva` | Tomar el pedido (A+W o manual) → capturar receptor/método/forma → **"Emitir"** (sella + timbra en una operación) |
| Cobrar en mostrador | `/facturacion/caja` | Sobre la factura PUE, **"Cobrar"** por el neto (total − NC de ranura/anticipo) |
| Emitir anticipo | `/facturacion/anticipos/facturas` → **"Nuevo anticipo"** | Receptor **nominal**, monto base sin IVA → timbra serie FANT |
| Aplicar anticipos a la factura | `/facturacion/facturas/nueva` | En la emisión, agregar los anticipos (relación 07); genera una NC de amortización por anticipo |
| Emitir REPP (PPD) | `/facturacion/repp` → **"Nuevo REPP"** | Factura PPD → complemento de pago; `fechaPago` en UTC; cubre una o varias facturas |
| Emitir Carta Porte | `/facturacion/carta-porte` → **"Nueva Carta Porte"** | Tipo T (traslado) o I (servicio); vehículo, operador, ubicaciones origen/destino, mercancías |
| Factura de exportación (CCE) | `/facturacion/facturas/nueva` | Moneda USD + tipo de cambio + datos de Comercio Exterior (pedimento, INCOTERM, receptor extranjero) |
| Arqueo y cierre | `/facturacion/caja` | **"Iniciar arqueo"** congela el esperado → **"Cerrar"** declara el efectivo (permiso `caja.liquidar`) |
| Descargar XML/PDF | Detalle de cada comprobante | Botón **"XML"** / **"PDF"** (factura, NC, anticipo, REPP, Carta Porte) |
| Reintentar / descartar timbrado | Detalle del comprobante fallido | **"Reintentar timbrado"** tras corregir el dato, o **"Descartar"** (quema folio, libera el pedido) |

## Flujos internos de control

- **Emitir y cobrar están desacoplados** (Decisión 12-E): una factura se timbra
  primero y el cobro es un segundo acto. Una factura **sin cobro de mostrador**
  (obra / administrativo) simplemente no genera movimiento de caja; se cobra por
  la ruta bancaria CxC → Tesorería.
- **La caja solo cobra MXN** (P6): las ventas en USD (exportación) se cobran
  exclusivamente por transferencia con alcance administrativo, nunca en caja.
- **Ranura (KO_FALZ)**: la factura se emite **por el total** y la ranura se
  documenta con una **NC automática** (motivo Ranura, relación 01) timbrada en
  la misma transacción; la caja cobra `total − NC`.
- **Anticipos**: al aplicar N anticipos a la factura final, se autogenera y
  timbra **una NC de amortización (relación 07) por anticipo**, reduciendo el
  saldo de cada uno.
- **Alcance de caja en dos capas**: Capa A (a qué sucursales/canales llega el
  usuario) + Capa B (su sesión de efectivo con arqueo). Los perfiles
  administrativos operan **sin sesión** (solo transferencia).

## Ejemplos con folios reales

- Jornada completa del cajero (mostrador MXN, ranura, anticipos únicos y
  múltiples, PPD con REPP): [P9](../pipelines/p9-cajero-facturacion-tesoreria.md)
  — facturas `VEN-000009..16`, anticipos `FACANT-2026-000006..8`, NC
  `NCRED-2026-000005..8`.
- Multimoneda (mostrador MXN, obra sin caja, exportación USD/CCE) + Carta Porte:
  [P10](../pipelines/p10-facturacion-multimoneda-cce-obra.md).

## Errores comunes (en lenguaje de negocio)

- **"La caja solo cobra MXN en v1."** (`COBRO_MONEDA_INVALIDA`) — una venta USD
  no se cobra en caja; va por transferencia.
- **"El total del cobro no coincide."** (`COBRO_TOTAL_NO_COINCIDE`) — en una
  factura con ranura/anticipo se cobra el **neto** (total − NC acreditadas).
- **"La factura PPD se cobra por REPP."** (`COBRO_FACTURA_PPD`) — primero se
  emite el complemento de pago y se cobra sobre el REPP.
- **"El domicilio del receptor está incompleto para CCE."**
  (`CCE_DOMICILIO_RECEPTOR_INCOMPLETO`) — la exportación exige estado + CP del
  receptor extranjero (se valida antes de quemar folio).
- **"Faltan datos SAT de la Carta Porte."** (`CARTA_PORTE_DATOS_SAT_INCOMPLETOS`)
  — CP/estado de origen y destino y peso bruto vehicular son obligatorios.
- **"El anticipo no está timbrado."** (`ANTICIPO_CFDI_NO_TIMBRADO`) — un
  anticipo debe estar timbrado antes de vincularlo/aplicarlo.
