# Facturación — Ficha de administración

## Permisos canónicos (principales)

| Permiso | Rol sugerido |
|---|---|
| `facturacion.pedidos.importar` / `.capturar` / `.excepciones-resolver` | Facturista (captura + bandeja de importación A+W) |
| `facturacion.facturas.emitir` / `.leer` | Facturista / consulta |
| `facturacion.facturas.editar-receptor` / `.editar-articulo` | Facturista (corrección pre-timbre) |
| `facturacion.anticipos.emitir` / `.vincular` / `.leer` | Facturista (anticipos rel. 07) |
| `facturacion.notas-credito.bonificacion-emitir` / `.leer` | Facturista |
| `facturacion.carta-porte.emitir` / `.leer` | Facturista de traslados (incl. catálogos vehículo/operador) |
| `facturacion.repp.emitir` | Facturista |
| `facturacion.cancelaciones.solicitar` / `.consultar` | Facturista senior |
| `facturacion.activos.autorizar` | **Contador General** (venta de activos fijos) |
| `facturacion.caja.operar` | Cajero (abrir sesión propia, cobrar, arquear) |
| `facturacion.caja.liquidar` | Cajero autorizado / supervisor (cierre) |
| `facturacion.caja.supervisar` | Supervisor (apertura ajena, reabrir, cancelar cobros) |
| `facturacion.caja.administrar` | Administrador de cajas (CRUD + alcances Capa A) |
| `facturacion.caja.leer-todas` | Alcance administrativo total (bucket "Sin asignar") |
| `facturacion.comprobantes.reintentar-timbrado` / `.descartar` | Facturista senior |
| `facturacion.reportes.leer` | Perfiles de consulta |

## Configuración previa obligatoria

1. **Series CFDI por tipo de documento** (Compartido/Administración): venta
   (`VEN`), notas de crédito (`NCRED`, la usan ranura y amortización de
   anticipos) y **anticipos serie `FANT`** (`TipoDocumentoSerie`, se valida en
   la captura). Sin serie activa por sucursal el timbrado no arranca.
2. **`ConfiguracionPac`** (módulo `Integraciones.Fiscal`): CSD del emisor +
   identidades sandbox en dev. Emisor ≠ receptor en sandbox (P9-H1). Sin ella,
   el timbrado falla visible.
3. **`Facturacion:ReppAutomatico:SucursalClave`** (appservice.bicep): sucursal
   emisora de los REPP automáticos de cobros bancarios. Con varias sucursales
   activas es obligatorio fijar la clave.
4. **Cajas en dos capas**: Capa A — alcance administrativo por
   sucursal × canal × usuario (`caja.administrar`); Capa B — la sesión de
   efectivo del cajero (`caja.operar`). Los perfiles administrativos
   (Planta de Pintura, Venta de Activos, Facturación Administrativa) operan
   **sin sesión** vía `usuario_alcance`.
5. **Clientes nominales** en el master (para anticipos el receptor es siempre
   nominal; en dev sandbox el genérico XAXX no pasa el PAC).
6. **Catálogos de Carta Porte**: vehículos y operadores
   (`/facturacion/carta-porte` → catálogos) antes de emitir un traslado.
7. **Datos de exportación** (para CCE): cliente extranjero con `NumRegIdTrib`,
   país y domicilio completo; y en el pedido, pedimento/INCOTERM/fracciones.

## Eventos que publica / consume

| Dirección | Evento | Con quién | Efecto operativo |
|---|---|---|---|
| Publica | `facturacion.factura-venta.timbrada.v1` | CxC | Proyecta `factura_cartera` |
| Publica | `facturacion.factura-anticipo.timbrada.v1` | CxC | Anticipo disponible |
| Publica | `facturacion.nota-credito.timbrada.v1` | CxC | NC ranura (01) / amortización (07) a cartera |
| Publica | `facturacion.recibo-pago.timbrado.v1` (REPP) | CxC (aplica pago), Tesorería (marca `ReppTimbrado`) | Cartera saldada |
| Publica | `facturacion.cobro-mostrador.registrado.v1` / `.cancelado.v1` | CxC | Aplica/reversa cobro en cartera |
| Publica | `facturacion.caja-sesion.abierta.v1` / `.cerrada.v1` | Tesorería | Expectativa de depósito de caja |
| Publica | `facturacion.comprobante.cancelado.v1` | CxC | Reversa de cartera |
| Consume | `tesoreria.pago-cliente.confirmado.v1` | `TesoreriaEventListenerWorker` (en Facturación) | Emite el **REPP automático** al confirmarse un depósito bancario |
| Consume | Ingesta A+W (`aw_solicitud_pedido`) | `AwSolicitudesWorker` + write-back | Crea `PedidoFacturable` (moneda del pedido) |

## Monitoreo y troubleshooting

- **Workers en Application Insights**: `TesoreriaEventListenerWorker` (REPP
  automático), `AwSolicitudesWorker` / `WriteBackResultadoWorker` (ingesta A+W),
  más el `OutboxPublisherWorker` del esquema. Si un cobro bancario no genera
  REPP, revisar el listener de Tesorería y `EmitirReppDesdePagoConfirmadoCommand`.
- **Timbrado**: el CFDI se arma en `CfdiEmisionBuilder` y viaja al PAC vía
  `FiscalApiTimbradoAdapter` (`POST /api/v4/invoices`). Complementos soportados:
  Pago 2.0, Comercio Exterior (CCE 2.0), Carta Porte 3.1. El XML sellado se
  persiste en `integraciones_fiscal.cfdi_archivo.xml_contenido` (TEXT en
  Postgres) y se descarga por `.../{id}/xml` en cada familia.
- **Idempotencia y outbox**: los eventos se rutean al outbox del schema del
  módulo dueño (fix P9-H7, #666). Un evento encolado que cruza DbContext (p. ej.
  amortización de varios anticipos) ya no se pierde.

## Límites conocidos vigentes

- **La caja solo cobra MXN** (`COBRO_MONEDA_INVALIDA`): USD exclusivamente por
  transferencia con alcance administrativo.
- **Carta Porte solo autotransporte federal nacional** (`TranspInternac="No"`,
  país MEX, una ubicación origen/destino, un operador figura `01`). Sin soporte
  marítimo/aéreo/ferroviario ni transporte internacional.
- **Tipo de cambio de venta manual** (no auto-DOF): el facturista lo captura; el
  CCE lleva su `TcDof` aparte (día hábil anterior).
- **Ranura** solo llega de la ingesta A+W (el pedido manual no expone `ranura`);
  en dev se simula con UPDATE autorizado.
- La API acepta **enums como enteros** y fechas **solo en UTC** (offset 0) — el
  `fechaPago` del REPP debe viajar en UTC (P9-H5).
