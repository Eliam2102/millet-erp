/**
 * Tipos compartidos del módulo Facturación frontend (FE-F0+). Mirror
 * manual de los DTOs / enums que devuelve el backend en
 * <c>/api/v1/facturacion/...</c>. Cuando ADR-0017 (codegen TS desde
 * OpenAPI) entre, este archivo se vuelve autogenerado.
 *
 * <para>Convención: los enums se modelan como <c>const</c> + <c>type</c>
 * union para cumplir <c>erasableSyntaxOnly</c> (mismo patrón que
 * <c>cxp/api/types.ts</c> y <c>almacen/api/types.ts</c>). Los valores
 * numéricos espejan el <c>short</c> del backend — están fijos por ABI,
 * agregar al final, nunca renumerar.</para>
 *
 * <para>FE-F0 solo declara el andamio fundacional (paginado + enums de
 * estado del pedido y de la FSM de timbrado). Los DTOs ricos por
 * recurso (líneas de pedido, comprobantes, cadena CFDI, anticipos…)
 * llegan con la fase FE que los consume.</para>
 */

// ─── Paged response (compartido con backend Common.PagedResponse) ─────

export interface PagedResponse<T> {
  items: T[];
  offset: number;
  limit: number;
  total: number;
}

// ─── Pedido facturable (Domain.Pedidos) ──────────────────────────────

/**
 * Estado de un pedido facturable — <b>binario respecto a facturación</b>
 * (D19): no existe "parcialmente facturado". Mirror de backend
 * <c>EstadoPedidoFacturable</c>.
 */
export const EstadoPedidoFacturable = {
  /** Listo para facturar; única ventana de edición libre. */
  Importado: 1,
  /** Soft-lock mientras un cajero lo factura. */
  Bloqueado: 2,
  /** Tiene un CFDI vigente. Al cancelar el CFDI vuelve a Importado. */
  Facturado: 3,
  /** Cancelado (cancelación de A+W sin CFDI, o anulado). */
  Cancelado: 4,
  /** Falló validación en la ingesta (no aplica a Manual). */
  Excepcion: 5,
} as const satisfies Record<string, number>;
export type EstadoPedidoFacturable =
  (typeof EstadoPedidoFacturable)[keyof typeof EstadoPedidoFacturable];

/**
 * Origen de un pedido facturable. El Sistema de Salidas <b>no</b> es
 * origen (aporta pedimento, no pedidos). Mirror de backend
 * <c>OrigenPedido</c>.
 */
export const OrigenPedido = {
  /** Ingesta automática desde A+W. */
  Aw: 1,
  /** Ingesta desde Planta Pintura. */
  PlantaPintura: 2,
  /** Captura manual en el ERP. */
  Manual: 3,
} as const satisfies Record<string, number>;
export type OrigenPedido = (typeof OrigenPedido)[keyof typeof OrigenPedido];

// ─── Comprobante (Domain.Comprobantes) ────────────────────────────────

/**
 * FSM del ciclo de timbrado de un comprobante. Modelada
 * asíncrono-tolerante: <c>TimbradoEnProceso</c> y
 * <c>CancelacionPendiente</c> permiten que el flujo no asuma respuesta
 * síncrona del PAC. Mirror de backend <c>EstadoTimbrado</c>.
 */
export const EstadoTimbrado = {
  /** Borrador local, sin timbrar. Editable. */
  Borrador: 1,
  /** Retenido por requerir pedimento aún no disponible. */
  PendientePedimento: 2,
  /** Enviado al PAC; el timbre puede resolverse asíncronamente. */
  TimbradoEnProceso: 3,
  /** Timbrado por el SAT: UUID + sellos disponibles. Inmutable. */
  Timbrado: 4,
  /** Error definitivo del PAC; corregible → vuelve a Borrador. */
  TimbradoFallido: 5,
  /** Solicitud de cancelación SAT 4.0 en curso. */
  CancelacionPendiente: 6,
  /** Cancelado ante el SAT. El CFDI nunca se borra (inmutable). */
  Cancelado: 7,
  /** Fallida descartada a conciencia: terminal, quema el folio y libera el pedido (01-G G3). */
  Descartada: 8,
} as const satisfies Record<string, number>;
export type EstadoTimbrado =
  (typeof EstadoTimbrado)[keyof typeof EstadoTimbrado];

/**
 * Eje fiscal de la facturación: cómo se comporta la venta frente al
 * SAT. Ortogonal al canal. Mirror de backend <c>ComportamientoFiscal</c>.
 */
export const ComportamientoFiscal = {
  MostradorInmediato: 1,
  ConAnticipo: 2,
  ExportacionConCce: 3,
  TrasladoConCartaPorte: 4,
  VentaActivoFijo: 5,
  Administrativa: 6,
} as const satisfies Record<string, number>;
export type ComportamientoFiscal =
  (typeof ComportamientoFiscal)[keyof typeof ComportamientoFiscal];

/** Tipo de anticipo (serie FANT). Mirror de backend <c>TipoAnticipo</c>. */
export const TipoAnticipo = {
  ClientesMxp: 1,
  ClientesUsd: 2,
} as const satisfies Record<string, number>;
export type TipoAnticipo = (typeof TipoAnticipo)[keyof typeof TipoAnticipo];

/** Estado de una autorización de venta de activo. Mirror de backend. */
export const EstadoAutorizacionActivo = {
  Autorizada: 1,
  Usada: 2,
  Cancelada: 3,
} as const satisfies Record<string, number>;
export type EstadoAutorizacionActivo =
  (typeof EstadoAutorizacionActivo)[keyof typeof EstadoAutorizacionActivo];

/** Estado de un anticipo. Mirror de backend <c>EstadoAnticipo</c>. */
export const EstadoAnticipo = {
  Abierto: 1,
  Amortizado: 2,
  Cancelado: 3,
} as const satisfies Record<string, number>;
export type EstadoAnticipo =
  (typeof EstadoAnticipo)[keyof typeof EstadoAnticipo];

/**
 * Item del lookup de canales de venta (FAC-ING-PR3). Eje organizacional
 * de la facturación: de dónde viene la venta; ortogonal al comportamiento
 * fiscal. Ya NO es un enum hardcodeado — es el catálogo administrable
 * <c>compartido.canales_venta</c> (FAC-ING-PR2); el lookup
 * <c>GET /facturacion/catalogos/canales-venta</c> devuelve solo los
 * activos, ordenados por id. Los commands siguen mandando el id numérico.
 */
export interface CanalVentaLookupItem {
  id: number;
  nombre: string;
}

// ─── DTOs de pedidos facturables (FE-F1-PR1) ──────────────────────────

/**
 * Línea de un pedido facturable manual (input del POST/PUT). Nota: NO
 * lleva tasas de impuesto — esas se resuelven en la emisión (D: la
 * tasa fiscal del producto vive en DatosMaestros, F3 backend).
 */
export interface PedidoFacturableLineaInput {
  productoId: string | null;
  productoDescripcion: string;
  claveProdServSat: string | null;
  claveUnidadSat: string | null;
  cantidad: number;
  precio: number;
  descuento: number;
  requierePedimento: boolean;
}

/**
 * Envelope de las bandejas bajo la Capa A de Cajas (CAJAS-PR2):
 * <c>items</c> ya viene filtrado por el alcance del usuario;
 * <c>sinAsignarCount</c> solo llega (no-null) con el permiso
 * <c>facturacion.caja.leer-todas</c> — alimenta el badge "Sin asignar"
 * (FE en cajas-pr6). Los hooks de lista desenvuelven <c>items</c> para no
 * tocar a los consumidores actuales.
 */
export interface BandejaScoped<TItem> {
  items: TItem[];
  sinAsignarCount: number | null;
}

/**
 * Item de la bandeja de pedidos facturables. <c>estado</c> y
 * <c>origen</c> llegan como string (el backend serializa el enum con
 * <c>.ToString()</c>: "Importado", "Aw", "Manual", …).
 */
export interface PedidoBandejaItem {
  id: string;
  numeroPedido: string | null;
  origen: string;
  estado: string;
  clienteNombre: string;
  total: number;
  moneda: string;
  createdAt: string;
}

// ─── Anticipos (FE-F4) ────────────────────────────────────────────────

/** Command de emisión de anticipo (serie FANT). Mirror del backend. */
export interface EmitirAnticipoCommand {
  sucursalId: string;
  clienteId: string;
  receptorRfc: string;
  receptorNombre: string;
  receptorRegimenFiscal: string;
  receptorCodigoPostal: string;
  receptorUsoCfdi: string;
  receptorPais: string;
  rfcEmisor: string;
  regimenFiscalEmisor: string;
  metodoPago: string;
  formaPago: string;
  moneda: string;
  tipoCambio: number | null;
  tipoAnticipo: number;
  montoBase: number;
  tasaIvaTraslado: number | null;
  descripcion: string | null;
  pedidoFacturableId: string | null;
  pedidoOrigenRef: string | null;
  obraId: number | null;
  obraNombre: string | null;
}

/** Respuesta de la emisión de anticipo. */
export interface EmitirAnticipoResponse {
  anticipoId: string;
  facturaAnticipoId: string;
  estado: string;
  uuid: string | null;
  folio: string;
  total: number;
  saldo: number;
}

/** Anticipo a amortizar en la factura final (relación 07). */
export interface AnticipoAAmortizar {
  anticipoId: string;
  importe: number;
}

/**
 * Shape de reporte del backend (ADR-0036). Coincide exactamente con el
 * <c>reporte</c> que espera <c>&lt;ReporteShell&gt;</c>, así que se le
 * pasa directo (sin adaptador).
 */
export interface ReporteBackend<TFila> {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: ReadonlyArray<{
    clave: string;
    etiqueta: string;
    tipo: 'texto' | 'numero' | 'moneda' | 'fecha';
    alineacion?: string | null;
    anchoPx?: number | null;
  }>;
  filas: readonly TFila[];
  totales?: Record<string, number> | null;
}

/** Fila del Control de Anticipos (resumen). */
export interface ControlAnticipoFila {
  anticipoId: string;
  folio: string;
  cliente: string;
  obra: string | null;
  tipoAnticipo: string;
  moneda: string;
  montoCobrado: number;
  montoAmortizado: number;
  saldo: number;
  estado: string;
  pedidoOrigenRef: string | null;
  fechaEmision: string | null;
  /** Enlace al detalle de la factura de anticipo (ANT-PR1, doc 13). */
  facturaAnticipoId: string;
  /** Estado de timbrado del CFDI del anticipo (13-J). */
  estadoCfdi: string;
}

/** Vinculación de un anticipo a una factura (con su NC de amortización). */
export interface AnticipoVinculacionDetalle {
  facturaVentaId: string;
  facturaFolio: string | null;
  importe: number;
  ncAmortizacionId: string | null;
  ncFolio: string | null;
  ncTimbrada: boolean;
}

/** Anticipo dentro del estado de cuenta por cliente. */
export interface AnticipoEstadoCuenta {
  anticipoId: string;
  folio: string;
  tipoAnticipo: string;
  montoCobrado: number;
  montoAmortizado: number;
  saldo: number;
  estado: string;
  pedidoOrigenRef: string | null;
  /** Enlace al detalle de la factura de anticipo (ANT-PR1, doc 13). */
  facturaAnticipoId: string;
  /** Estado de timbrado del CFDI del anticipo (13-J). */
  estadoCfdi: string;
  vinculaciones: AnticipoVinculacionDetalle[];
}

/** Estado de cuenta de anticipos por cliente (GET /anticipos/control/{clienteId}). */
export interface ControlAnticiposDetalladaResponse {
  clienteId: string;
  generadoEn: string;
  totalCobrado: number;
  totalAmortizado: number;
  totalSaldo: number;
  anticipos: AnticipoEstadoCuenta[];
}

// ─── Bandeja + detalle de facturas de anticipo (ANT-PR2, doc 13) ──────

/** Item de la bandeja de facturas de anticipo (GET /anticipos/facturas). */
export interface FacturaAnticipoBandejaItem {
  id: string;
  folio: string;
  estado: string;
  uuid: string | null;
  receptorNombre: string;
  receptorRfc: string;
  tipoAnticipo: string;
  total: number;
  moneda: string;
  fechaTimbrado: string | null;
  /** Estado del saldo (Abierto/Amortizado/Cancelado); null si hay inconsistencia. */
  estadoAnticipo: string | null;
  saldo: number | null;
}

/** Vinculación M2/M3 dentro del detalle (13-A). */
export interface AnticipoVinculacionInfo {
  facturaVentaId: string;
  facturaFolio: string | null;
  facturaUuid: string | null;
  facturaEstado: string | null;
  importe: number;
  creadoEn: string;
  ncAmortizacionId: string | null;
  ncFolio: string | null;
  ncUuid: string | null;
  ncEstado: string | null;
}

/** Saldo amortizable del anticipo dentro del detalle (13-A). */
export interface AnticipoSaldoDetalle {
  anticipoId: string;
  clienteId: string;
  estado: string;
  montoCobrado: number;
  montoAmortizado: number;
  saldo: number;
  saldoDisponible: number;
  pedidoOrigenRef: string | null;
  obraId: number | null;
  obraNombre: string | null;
  vinculaciones: AnticipoVinculacionInfo[];
}

/** Detalle de una factura de anticipo (GET /anticipos/facturas/{id}). */
export interface FacturaAnticipoDetalleResponse {
  id: string;
  folio: string;
  estado: string;
  uuid: string | null;
  receptorNombre: string;
  receptorRfc: string;
  tipoAnticipo: string;
  anticipoId: string;
  pedidoFacturableId: string | null;
  descripcion: string;
  subtotal: number;
  impuestosTrasladados: number;
  total: number;
  moneda: string;
  fechaTimbrado: string | null;
  version: number;
  timbradoErrorCodigo: string | null;
  timbradoErrorMensaje: string | null;
  relaciones: RelacionCfdiDetalle[];
  anticipo: AnticipoSaldoDetalle | null;
}

// ─── REPP / Recibo de pago (FE-F6) ────────────────────────────────────

/** Factura a pagar dentro de un REPP. */
export interface ReppFacturaPago {
  facturaVentaId: string;
  importePagado: number;
}

/** Command de emisión de REPP (Pago 2.0, multi-factura). */
export interface EmitirReppCommand {
  sucursalId: string;
  fechaPago: string;
  monedaPago: string;
  tcPago: number | null;
  formaPagoReal: string;
  cuentaOrdenante: string | null;
  cuentaBeneficiaria: string | null;
  referenciaPago: string | null;
  facturas: ReppFacturaPago[];
}

/** Factura pagada (resultado de la emisión). */
export interface ReppFacturaPagada {
  facturaVentaId: string;
  numParcialidad: number;
  importePagado: number;
  saldoInsoluto: number;
  gananciaPerdidaCambiaria: number;
}

/** Respuesta de la emisión de REPP. */
export interface EmitirReppResponse {
  id: string;
  estado: string;
  uuid: string | null;
  folio: string;
  importeTotalPago: number;
  gananciaPerdidaCambiariaTotal: number;
  facturas: ReppFacturaPagada[];
}

/** Item de la bandeja de REPP. */
export interface ReppBandejaItem {
  id: string;
  folio: string;
  estado: string;
  uuid: string | null;
  receptorNombre: string;
  importeTotalPago: number;
  fechaPago: string;
}

/** Factura cubierta por un REPP (detalle). */
export interface ReppFacturaCubierta {
  facturaVentaId: string;
  folio: string | null;
  facturaUuid: string;
  numParcialidad: number;
  importePagado: number;
  saldoInsoluto: number;
  gananciaPerdidaCambiaria: number;
}

/**
 * Factura PPD con saldo por cobrar, candidata de un REPP
 * (GET /repp/facturas-cobrables; cierra PLATFORM-TODO(<FacturaPicker>)).
 * `saldo` = total − acreditadoNc − pagadoRepp ([Decisión 13-K]).
 */
export interface FacturaCobrablePpdItem {
  facturaVentaId: string;
  folio: string;
  receptorRfc: string;
  receptorNombre: string;
  moneda: string;
  total: number;
  acreditadoNc: number;
  pagadoRepp: number;
  saldo: number;
  numParcialidadSiguiente: number;
  fechaTimbrado: string | null;
}

/** Detalle de un REPP (GET /repp/{id}). */
export interface ReppDetalleResponse {
  id: string;
  folio: string;
  estado: string;
  uuid: string | null;
  receptorNombre: string;
  monedaPago: string;
  importeTotalPago: number;
  fechaPago: string;
  facturasCubiertas: ReppFacturaCubierta[];
  timbradoErrorCodigo: string | null;
  timbradoErrorMensaje: string | null;
  folioPac: string | null;
}

// ─── Carta Porte 3.1 (FE-F8) ──────────────────────────────────────────

/** Mercancía de una Carta Porte (input). */
export interface CartaPorteMercanciaInput {
  descripcion: string;
  bienesTransp: string;
  claveUnidad: string;
  cantidad: number;
  pesoEnKg: number;
  materialPeligroso: boolean;
}

/** Command de emisión de Carta Porte (tipo T traslado / I ingreso). */
export interface EmitirCartaPorteCommand {
  sucursalId: string;
  cajaId: string | null;
  tipoCfdi: string;
  receptorRfc: string;
  receptorNombre: string;
  receptorRegimenFiscal: string;
  receptorCodigoPostal: string;
  receptorUsoCfdi: string;
  receptorPais: string;
  rfcEmisor: string;
  regimenFiscalEmisor: string;
  moneda: string;
  origen: string;
  destino: string;
  /** F12-PR3: domicilio SAT de las ubicaciones (CP 3.1 exige Domicilio en
   * Origen/Destino para el timbrado real). CP de 5 dígitos + clave c_Estado. */
  origenCodigoPostal: string | null;
  origenEstado: string | null;
  destinoCodigoPostal: string | null;
  destinoEstado: string | null;
  distanciaKm: number;
  vehiculoId: string;
  operadorId: string;
  pedidoFacturableId: string | null;
  fechaSalida: string;
  fechaLlegadaEstimada: string;
  montoServicio: number;
  tasaIvaServicio: number | null;
  mercancias: CartaPorteMercanciaInput[];
}

/** Body de "Crear siguiente tramo" (hereda receptor/emisor del tramo previo). */
export interface SiguienteTramoCommand {
  tipoCfdi: string;
  sucursalId: string;
  origen: string;
  destino: string;
  /** F12-PR3: domicilio SAT del tramo nuevo (CP 3.1). */
  origenCodigoPostal: string | null;
  origenEstado: string | null;
  destinoCodigoPostal: string | null;
  destinoEstado: string | null;
  distanciaKm: number;
  vehiculoId: string;
  operadorId: string;
  fechaSalida: string;
  fechaLlegadaEstimada: string;
  montoServicio: number;
  tasaIvaServicio: number | null;
}

/** Respuesta de la emisión de Carta Porte. */
export interface EmitirCartaPorteResponse {
  id: string;
  tipoCfdi: string;
  estado: string;
  uuid: string | null;
  folio: string;
  total: number;
  cartaPortePreviaId: string | null;
}

/** Item de la bandeja de Carta Porte. */
export interface CartaPorteBandejaItem {
  id: string;
  folio: string;
  estado: string;
  uuid: string | null;
  tipo: string;
  tramo: string;
  fechaSalida: string;
}

export interface CartaPorteVehiculoDetalle {
  id: string;
  placa: string;
  configVehicular: string;
  anioModelo: number;
}

export interface CartaPorteOperadorDetalle {
  id: string;
  rfc: string;
  nombre: string;
  numLicencia: string;
}

export interface CartaPorteMercanciaDetalle {
  descripcion: string;
  bienesTransp: string;
  claveUnidad: string;
  cantidad: number;
  pesoEnKg: number;
  materialPeligroso: boolean;
}

/** Detalle de una Carta Porte (GET /carta-porte/{id}). */
export interface CartaPorteDetalleResponse {
  id: string;
  folio: string;
  estado: string;
  uuid: string | null;
  tipo: string;
  origen: string;
  destino: string;
  distanciaKm: number;
  fechaSalida: string;
  fechaLlegadaEstimada: string;
  cartaPortePreviaId: string | null;
  total: number;
  vehiculo: CartaPorteVehiculoDetalle | null;
  operador: CartaPorteOperadorDetalle | null;
  mercancias: CartaPorteMercanciaDetalle[];
  timbradoErrorCodigo: string | null;
  timbradoErrorMensaje: string | null;
  folioPac: string | null;
}

/** Respuesta de crear vehículo/operador (catálogo). */
export interface CatalogoCreadoResponse {
  id: string;
}

// ─── Catálogos de Carta Porte: vehículos y operadores (admin) ─────────

/** Renglón del catálogo de vehículos (GET /carta-porte/vehiculos). */
export interface VehiculoListItem {
  id: string;
  placa: string;
  configVehicular: string;
  anioModelo: number;
  tipoPermisoSct: string | null;
  numPermisoSct: string | null;
  aseguradora: string | null;
  polizaSeguro: string | null;
  /** Toneladas — obligatorio para timbrar Carta Porte 3.1. */
  pesoBrutoVehicular: number | null;
  activo: boolean;
  version: number;
}

/** Renglón del catálogo de operadores (GET /carta-porte/operadores). */
export interface OperadorListItem {
  id: string;
  rfc: string;
  nombre: string;
  numLicencia: string;
  activo: boolean;
  version: number;
}

/** Command de alta de vehículo (POST /carta-porte/vehiculos). */
export interface CrearVehiculoCommand {
  placa: string;
  configVehicular: string;
  anioModelo: number;
  tipoPermisoSct: string | null;
  numPermisoSct: string | null;
  aseguradora: string | null;
  polizaSeguro: string | null;
  pesoBrutoVehicular: number | null;
}

/** Command de alta de operador (POST /carta-porte/operadores). */
export interface CrearOperadorCommand {
  rfc: string;
  nombre: string;
  numLicencia: string;
}

/**
 * Body del PATCH de vehículo. Placa inmutable. Si viene
 * <c>configVehicular</c> se reemplazan TODOS los datos editables (los
 * opcionales null se limpian); <c>activo</c> activa/desactiva.
 */
export interface ActualizarVehiculoPayload {
  configVehicular?: string;
  anioModelo?: number;
  tipoPermisoSct?: string | null;
  numPermisoSct?: string | null;
  aseguradora?: string | null;
  polizaSeguro?: string | null;
  pesoBrutoVehicular?: number | null;
  activo?: boolean;
}

/** Body del PATCH de operador. RFC inmutable. */
export interface ActualizarOperadorPayload {
  nombre?: string;
  numLicencia?: string;
  activo?: boolean;
}

// ─── Activos fijos (FE-F9) ────────────────────────────────────────────

/** Command de autorización de venta de activo (Contador General). */
export interface AutorizarVentaActivoCommand {
  activoRef: string;
  precioVenta: number;
}

/** Respuesta de la autorización (calcula valor neto + utilidad/pérdida). */
export interface AutorizarVentaActivoResponse {
  autorizacionId: string;
  descripcion: string;
  valorNetoEnLibros: number;
  utilidadOPerdida: number;
}

/** Item de la bandeja de autorizaciones de venta de activo. */
export interface AutorizacionActivoItem {
  id: string;
  activoRef: string;
  descripcion: string;
  precioVenta: number;
  valorNetoEnLibros: number;
  utilidadOPerdida: number;
  autorizadoPor: string;
  fechaAutorizacion: string;
  estado: string;
}

/** Respuesta del POST/PUT de pedido manual. */
export interface PedidoFacturableResponse {
  id: string;
  estado: string;
  total: number;
  version: number;
}

/**
 * Línea del detalle de un pedido (GET /pedidos-facturables/{id}).
 * FAC-UX-PR3: las claves SAT llegan resueltas desde el master
 * `producto_aw` cuando el snapshot de la línea las tenía en null;
 * `objetoImp` y tasas siempre vienen del master (la línea no las
 * persiste) — null si el producto no está en el master.
 */
export interface PedidoFacturableLineaDetalle {
  posicion: number;
  productoId: string | null;
  productoDescripcion: string;
  claveProdServSat: string | null;
  claveUnidadSat: string | null;
  cantidad: number;
  precio: number;
  descuento: number;
  requierePedimento: boolean;
  objetoImp: string | null;
  tasaIvaTraslado: number | null;
  tasaRetencionIva: number | null;
  tasaRetencionIsr: number | null;
}

/**
 * Datos fiscales vivos del cliente en `compartido.clientes` (FAC-UX-PR3).
 * `null` total si el cliente no existe en el master; campos individuales
 * en null = incompletos (gap G12 — régimen/CP no vienen de A+W; se
 * completan en /admin/datos-maestros/clientes).
 */
export interface PedidoClienteFiscal {
  rfc: string | null;
  regimenFiscal: string | null;
  codigoPostalFiscal: string | null;
  usoCfdiDefault: string | null;
  formaPagoDefault: string | null;
  metodoPagoDefault: string | null;
  esGenerico: boolean;
}

/**
 * Detalle de un pedido facturable (GET /pedidos-facturables/{id}, B1).
 * `origen`, `estado` y `comportamientoFiscal` llegan como string (nombre
 * del enum). `canalVentaId` + `canalVenta` vienen del catálogo
 * `compartido.canales_venta` (FAC-ING-PR2): el id prellena el selector al
 * editar/facturar; el nombre es para display (histórico incluido — un
 * canal desactivado sigue mostrando su nombre). `version` se usa como
 * `If-Match` al editar.
 */
export interface PedidoFacturableDetalleResponse {
  id: string;
  numeroPedido: string | null;
  origen: string;
  estado: string;
  sucursalId: string;
  clienteId: string;
  clienteNombre: string;
  canalVentaId: number;
  canalVenta: string;
  comportamientoFiscal: string;
  moneda: string;
  obraId: number | null;
  obraNombre: string | null;
  comentarios: string | null;
  comprobanteVigenteId: string | null;
  total: number;
  /**
   * RANURA-PR1: descuento "ranura" de cabecera del pedido A+W (bruto, IVA
   * incluido). No se resta en el CFDI — se documenta con NC (relación 01)
   * y la caja cobra total − NC. null = sin ranura.
   */
  ranura: number | null;
  version: number;
  clienteFiscal: PedidoClienteFiscal | null;
  lineas: PedidoFacturableLineaDetalle[];
}

/**
 * Defaults del emisor (GET /facturacion/emisor-defaults, FAC-UX-PR3):
 * RFC/razón social/régimen de la empresa del contexto + la sucursal
 * única activa (null si hay más de una — el usuario elige).
 */
export interface EmisorDefaultsResponse {
  rfcEmisor: string;
  razonSocialEmisor: string;
  regimenFiscalEmisor: string;
  sucursalIdDefault: string | null;
  /**
   * Tasa de IVA default de la empresa (fracción, FAC-DET-PR2/PR3).
   * Fallback para líneas manuales sin tasa del artículo; null = sin
   * configurar (la emisión conserva su fallback local 0.16).
   */
  tasaIvaDefault: number | null;
  /**
   * CP fiscal de la empresa (LugarExpedicion del CFDI 4.0, F12-PR1).
   * null = sin capturar — la emisión fallará con
   * EMISOR_SIN_LUGAR_EXPEDICION hasta capturarlo en Admin → Empresas.
   */
  codigoPostalEmisor: string | null;
}

/**
 * Item del lookup de clientes para pickers de emisión
 * (GET /facturacion/catalogos/clientes, FAC-UX-PR4). Incluye los
 * defaults fiscales para autollenar el form al seleccionar.
 */
export interface ClienteLookupItem {
  id: string;
  clave: string;
  razonSocial: string;
  rfc: string | null;
  regimenFiscal: string | null;
  codigoPostalFiscal: string | null;
  usoCfdiDefault: string | null;
  formaPagoDefault: string | null;
  metodoPagoDefault: string | null;
  monedaDefault: string;
  esGenerico: boolean;
  /** Receptor extranjero (CCE) para prellenar el encabezado de exportación. */
  numRegIdTrib: string | null;
  paisResidencia: string | null;
  domicilioExtranjeroCalle: string | null;
  domicilioExtranjeroEstado: string | null;
  domicilioExtranjeroCodigoPostal: string | null;
  datosFiscalesCompletos: boolean;
}

/**
 * Item del lookup de productos A+W para el selector de conceptos
 * (GET /facturacion/catalogos/productos-aw, FAC-UX-PR4).
 */
export interface ProductoAwLookupItem {
  id: string;
  referenciaExterna: string;
  descripcion: string;
  unidadMedida: string;
  claveProdServSat: string | null;
  claveUnidadSat: string | null;
  objetoImp: string | null;
  tasaIvaTraslado: number | null;
  tasaRetencionIva: number | null;
  tasaRetencionIsr: number | null;
  /** Datos de aduana (CCE) para prellenar la línea de exportación. */
  fraccionArancelaria: string | null;
  unidadAduana: string | null;
  pesoUnitarioKg: number | null;
  datosFiscalesCompletos: boolean;
}

/** Item de la bandeja de excepciones de importación (GET /pedidos-facturables/excepciones). */
export interface ExcepcionImportacionItem {
  id: string;
  origen: string;
  pedidoRef: string;
  motivo: string;
  detalle: string | null;
  resuelto: boolean;
  createdAt: string;
}

/** Comprobante del historial de un pedido (GET /pedidos-facturables/{id}/comprobantes, B5). */
export interface PedidoComprobanteItem {
  id: string;
  folio: string;
  estado: string;
  uuid: string | null;
  total: number;
  moneda: string;
  fechaTimbrado: string | null;
  vigente: boolean;
}

// ─── Emisión / comprobantes (FE-F1-PR2) ───────────────────────────────

/**
 * Método de pago CFDI 4.0 (catálogo c_MetodoPago). Solo dos valores;
 * se capturan directo (no hay catálogo en el backend que valide este).
 */
export const METODOS_PAGO: ReadonlyArray<{ value: string; label: string }> = [
  { value: 'PUE', label: 'PUE — Pago en una sola exhibición' },
  { value: 'PPD', label: 'PPD — Pago en parcialidades o diferido' },
];

/**
 * Objeto de impuesto CFDI 4.0 (catálogo c_ObjetoImp). Se captura por
 * línea; default "02" (sí objeto de impuesto).
 */
export const OBJETOS_IMP: ReadonlyArray<{ value: string; label: string }> = [
  { value: '01', label: '01 — No objeto de impuesto' },
  { value: '02', label: '02 — Sí objeto de impuesto' },
  { value: '03', label: '03 — Sí objeto, no obligado al desglose' },
  { value: '04', label: '04 — Sí objeto, no causa impuesto' },
];

/** Línea del comando de emisión (mirror de EmitirFacturaVentaLinea). */
export interface EmitirFacturaVentaLinea {
  productoId: string | null;
  claveProdServSat: string;
  descripcion: string;
  claveUnidadSat: string;
  cantidad: number;
  valorUnitario: number;
  descuento: number;
  objetoImp: string;
  tasaIvaTraslado: number | null;
  tasaRetencionIva: number | null;
  tasaRetencionIsr: number | null;
  requierePedimento: boolean;
}

/** Comando de emisión de factura de venta (subset F1 — sin CCE/anticipos). */
export interface EmitirFacturaVentaCommand {
  sucursalId: string;
  receptorRfc: string;
  receptorNombre: string;
  receptorRegimenFiscal: string;
  receptorCodigoPostal: string;
  receptorUsoCfdi: string;
  receptorPais: string;
  rfcEmisor: string;
  regimenFiscalEmisor: string;
  metodoPago: string;
  formaPago: string;
  moneda: string;
  tipoCambio: number | null;
  canalVenta: number;
  comportamientoFiscal: number;
  obraId: number | null;
  obraNombre: string | null;
  facturaAgrupada: boolean;
  /** Liga la factura al pedido origen (B2). Si se provee, al timbrar el
   * backend transiciona el pedido a `Facturado`. */
  pedidoFacturableId?: string | null;
  /** Anticipos a amortizar (relación 07). El backend emite la NC de
   * amortización atómica con el timbre de la factura final. */
  anticipos?: AnticipoAAmortizar[] | null;
  /** Complemento de Comercio Exterior (solo exportación con CCE). */
  cce?: EmitirFacturaVentaCce | null;
  /** Autorización del Contador General (obligatoria para venta de activo fijo). */
  autorizacionId?: string | null;
  lineas: EmitirFacturaVentaLinea[];
}

/** Respuesta de la emisión. */
export interface EmitirFacturaVentaResponse {
  id: string;
  estado: string;
  uuid: string | null;
  folio: string;
  total: number;
  version: number;
  /**
   * RANURA-PR2: NC automática de la ranura del pedido A+W (relación 01),
   * emitida y timbrada junto con la factura. null/ausente = sin ranura.
   */
  notaCreditoRanura?: {
    id: string;
    folio: string;
    uuid: string | null;
    total: number;
  } | null;
}

/** Command de aplicación de pedimento (F7). */
export interface AplicarPedimentoCommand {
  pedimento: string;
  fechaDocAduanero: string | null;
  identificacionMercancia: string | null;
}

/** Respuesta de aplicar pedimento. */
export interface AplicarPedimentoResponse {
  id: string;
  estado: string;
  uuid: string | null;
  folio: string;
}

/** Línea CCE (aduana) — paralela a una línea de la factura de exportación. */
export interface EmitirFacturaVentaCceLinea {
  fraccionArancelaria: string;
  unidadAduana: string;
  cantidadAduana: number;
  valorUnitarioAduana: number;
  valorDolares: number;
  aplicaIva0: boolean;
}

/** Complemento de Comercio Exterior (CCE) de la emisión de exportación. */
export interface EmitirFacturaVentaCce {
  tipoOperacion: string;
  incoterm: string;
  tcDof: number;
  receptorNumRegIdTrib: string;
  receptorPaisResidencia: string;
  lineas: EmitirFacturaVentaCceLinea[];
  /** F12-PR3 (CCE 2.0): clave SAT c_ClavePedimento (ej. "A1"). */
  claveDePedimento?: string | null;
  /** F12-PR3 (CCE 2.0): la mercancía cuenta con certificado de origen. */
  certificadoOrigen?: boolean;
  /** F12-PR3 (CCE 2.0): domicilio del receptor extranjero. Calle opcional;
   * estado/provincia (texto libre) y CP los exige el builder al timbrar. */
  receptorDomicilioCalle?: string | null;
  receptorDomicilioEstado?: string | null;
  receptorDomicilioCodigoPostal?: string | null;
}

/** Item de la bandeja de comprobantes emitidos. */
export interface FacturaBandejaItem {
  id: string;
  folio: string;
  estado: string;
  uuid: string | null;
  receptorNombre: string;
  receptorRfc: string;
  total: number;
  moneda: string;
  fechaTimbrado: string | null;
}

/** Línea del detalle de un comprobante. */
export interface ComprobanteLineaDetalle {
  posicion: number;
  claveProdServSat: string;
  descripcion: string;
  claveUnidadSat: string;
  cantidad: number;
  valorUnitario: number;
  descuento: number;
  importe: number;
}

/** Relación CFDI (cadena: anticipos 07, NC 01, devolución 03, sustitución 04). */
export interface RelacionCfdiDetalle {
  tipoRelacion: string;
  uuidRelacionado: string;
  folio: string | null;
  tipoComprobante: string | null;
  total: number | null;
  fechaTimbrado: string | null;
}

/** Detalle completo de un comprobante (GET /facturas/{id}). */
export interface ComprobanteDetalleResponse {
  id: string;
  tipo: string;
  folio: string;
  estado: string;
  uuid: string | null;
  receptorRfc: string;
  receptorNombre: string;
  moneda: string;
  subtotal: number;
  descuento: number;
  impuestosTrasladados: number;
  retenciones: number;
  total: number;
  fechaTimbrado: string | null;
  version: number;
  lineas: ComprobanteLineaDetalle[];
  relaciones: RelacionCfdiDetalle[];
  /** Error del último intento de timbrado (estado TimbradoFallido). */
  timbradoErrorCodigo: string | null;
  timbradoErrorMensaje: string | null;
  /** Folio que asignó el PAC al CFDI (consecutivo por RFC emisor). */
  folioPac: string | null;
  /** [Decisión 13-K] Total acreditado por NC timbradas (amortización de anticipos + NC generales). */
  totalAcreditado: number;
  /** [Decisión 13-K] Monto por cobrar: total − totalAcreditado. */
  totalPorCobrar: number;
  /** [Decisión 13-K] NC timbradas que acreditan a esta factura. */
  notasCreditoAplicadas: NotaCreditoAplicadaDetalle[] | null;
}

/** NC timbrada que acredita a la factura ([Decisión 13-K]). */
export interface NotaCreditoAplicadaDetalle {
  id: string;
  folio: string;
  motivo: string;
  total: number;
  uuid: string | null;
  fechaTimbrado: string | null;
}

/** Respuesta de POST /comprobantes/{id}/reintentar-timbrado (todos los tipos). */
export interface ReintentarTimbradoResponse {
  id: string;
  tipo: string;
  estado: string;
  uuid: string | null;
  folio: string;
  timbradoErrorCodigo: string | null;
  timbradoErrorMensaje: string | null;
  version: number;
}

/** Respuesta de POST /comprobantes/{id}/descartar ([Decisión 01-G] G3). */
export interface DescartarComprobanteResponse {
  id: string;
  tipo: string;
  estado: string;
  folio: string;
  pedidoLiberadoId: string | null;
  version: number;
}

/** Item de la bitácora de intentos de timbrado (GET /comprobantes/{id}/intentos-timbrado, 01-G G4). */
export interface IntentoTimbradoItem {
  id: string;
  intentoNumero: number;
  resultado: 'Timbrado' | 'Fallido' | 'EnProceso';
  errorCodigo: string | null;
  errorMensaje: string | null;
  registradoAt: string;
}

/** Item de la bitácora de envío por correo (GET /facturas/{id}/envios, B6). */
export interface EnvioCorreoItem {
  id: string;
  destinatario: string;
  estado: string;
  intentos: number;
  enviadoAt: string | null;
  ultimoError: string | null;
}

/** Respuesta del reenvío de CFDI por correo. */
export interface ReenviarCorreoResponse {
  bitacoraId: string;
  estado: string;
}

// ─── NC bonificación + cancelación (FE-F5) ────────────────────────────

/** Command de NC por bonificación (relación 01). */
export interface NotaCreditoBonificacionCommand {
  facturaVentaId: string;
  montoTotal: number;
  tasaIva: number | null;
  descripcion: string | null;
}

/** Respuesta de la NC de bonificación. */
export interface NotaCreditoBonificacionResponse {
  id: string;
  estado: string;
  uuid: string | null;
  folio: string;
  total: number;
}

/**
 * Motivos de cancelación SAT 4.0 (catálogo c_MotivoCancelacion). El 01
 * exige UUID sustituto.
 */
export const MOTIVOS_CANCELACION: ReadonlyArray<{
  value: string;
  label: string;
  requiereSustituto: boolean;
}> = [
  {
    value: '01',
    label: '01 — Comprobante con errores con relación',
    requiereSustituto: true,
  },
  {
    value: '02',
    label: '02 — Comprobante con errores sin relación',
    requiereSustituto: false,
  },
  { value: '03', label: '03 — No se llevó a cabo la operación', requiereSustituto: false },
  {
    value: '04',
    label: '04 — Operación nominativa en factura global',
    requiereSustituto: false,
  },
];

/** Respuesta de la solicitud de cancelación. */
export interface SolicitarCancelacionResponse {
  solicitudId: string;
  comprobanteId: string;
  estadoComprobante: string;
  estadoSolicitud: string;
  estatusSat: string | null;
}

/** Estatus de la cancelación de un comprobante (GET /comprobantes/{id}/cancelar). */
export interface ConsultarCancelacionResponse {
  comprobanteId: string;
  estadoComprobante: string;
  solicitudId: string | null;
  estadoSolicitud: string | null;
  motivoSat: string | null;
  estatusSat: string | null;
  mensajeError: string | null;
  solicitadaEn: string | null;
  resueltaEn: string | null;
}

// ─── Cajas (CAJAS-PR5; backend CAJAS-PR1, 12-cajas.md §7/§10) ─────────

/** Item de la bandeja de cajas; sucursales/canales/usuarios son counts. */
export interface CajaListadoItem {
  id: string;
  nombre: string;
  descripcion: string | null;
  estatus: string;
  sucursales: number;
  canales: number;
  usuarios: number;
}

export interface CajaSucursalItem {
  sucursalId: string;
}

export interface CajaCanalItem {
  canalVentaId: number;
  nombre: string | null;
}

export interface CajaUsuarioItem {
  usuarioId: string;
}

/** Detalle de la caja con sus alcances; `version` alimenta el ETag (If-Match). */
export interface CajaDetalleResponse {
  id: string;
  nombre: string;
  descripcion: string | null;
  estatus: string;
  sucursales: CajaSucursalItem[];
  canales: CajaCanalItem[];
  usuarios: CajaUsuarioItem[];
  version: number;
}

/** Respuesta común de las mutaciones de caja. */
export interface CajaMutadaResponse {
  id: string;
  version: number;
}

/** Concesión de alcance de un usuario sin caja ([Decisión 12-6]); null = comodín. */
export interface UsuarioAlcanceItem {
  usuarioId: string;
  sucursalId: string | null;
  canalVentaId: number | null;
}

export interface UsuarioAlcanceInput {
  sucursalId: string | null;
  canalVentaId: number | null;
}

export interface UsuarioAlcancesResponse {
  usuarioId: string;
  total: number;
}

// ─── Sesiones de caja + cobros (CAJAS-PR6; backend PR3/PR4) ──────────

export interface CajaSesionCorteDto {
  formaPago: string;
  montoSistema: number;
  montoDeclarado: number | null;
}

export interface CajaMovimientoDto {
  id: string;
  tipo: string;
  formaPago: string;
  importe: number;
  moneda: string;
  descripcion: string;
  referencia: string | null;
  cobroMostradorId: string | null;
  usuarioId: string;
  createdAt: string;
}

export interface CajaSesionDetalleResponse {
  id: string;
  cajaId: string;
  cajaNombre: string;
  sucursalId: string;
  responsableUsuarioId: string;
  estado: string;
  diaOperacion: string;
  fechaApertura: string;
  fechaCierre: string | null;
  fondoApertura: number;
  efectivoTeorico: number | null;
  efectivoDeclarado: number | null;
  diferencia: number | null;
  cierreExtemporaneo: boolean;
  notasCierre: string | null;
  autorizacionAperturaId: string | null;
  version: number;
  cortes: CajaSesionCorteDto[];
  movimientos: CajaMovimientoDto[];
  totalesPorForma: CajaSesionCorteDto[];
}

/** `sesion` null = sin sesión vigente; `diaAnteriorPendiente` bloquea cobros (§5.2). */
export interface SesionActualResponse {
  sesion: CajaSesionDetalleResponse | null;
  diaAnteriorPendiente: boolean;
}

export interface CajaSesionMutadaResponse {
  id: string;
  version: number;
  estado: string;
  diaOperacion: string;
}

export interface CajaMovimientoRegistradoResponse {
  id: string;
  sesionId: string;
  importe: number;
}

export interface AutorizacionAperturaResponse {
  id: string;
  cajaId: string;
  cajeroUsuarioId: string;
  vigenteHasta: string;
}

export interface CobroFormaPagoInput {
  formaPago: string;
  importe: number;
  referencia?: string | null;
  cuentaOrdenante?: string | null;
  cuentaBeneficiaria?: string | null;
}

export interface CobroMostradorResponse {
  id: string;
  comprobanteId: string;
  cajaSesionId: string;
  estado: string;
  total: number;
}

export interface CobroFormaPagoItem {
  formaPago: string;
  importe: number;
  referencia: string | null;
}

export interface CobroMostradorItem {
  id: string;
  comprobanteId: string;
  comprobanteFolio: string;
  tipoComprobante: string;
  origen: string;
  estado: string;
  total: number;
  moneda: string;
  fechaCobro: string;
  usuarioCobradorId: string;
  formasPago: CobroFormaPagoItem[];
}

// ─── Liquidación de ruta + ajustes de caja (CAJAS-PR7) ────────────────

/** Un cobro del batch de liquidación de ruta ([Decisión 12-7]). */
export interface LiquidacionRutaCobroInput {
  comprobanteId: string;
  formasPago: CobroFormaPagoInput[];
}

export interface LiquidacionRutaResponse {
  cajaSesionId: string;
  total: number;
  cobros: CobroMostradorResponse[];
}

/** Comprobante timbrado sin cobro vigente, dentro del alcance del cajero. */
export interface ComprobanteCobrableItem {
  comprobanteId: string;
  folio: string;
  tipo: string;
  receptorNombre: string;
  /** Monto por cobrar: para facturas ya viene neto de NC ([Decisión 13-K]). */
  total: number;
  moneda: string;
  fechaTimbrado: string | null;
  /** [Decisión 13-K] Cuánto acreditaron las NC timbradas (0 si no hay). */
  montoAcreditado: number;
  /**
   * RANURA-PR3: porción de montoAcreditado que corresponde a la NC de la
   * ranura del pedido A+W (motivo Ranura). 0 si no hay ranura.
   */
  montoRanura: number;
}

/** Ajuste de caja ([Decisión 12-C]); aplicadoEnSesionId null = pendiente. */
export interface CajaAjusteItem {
  id: string;
  cobroMostradorId: string;
  comprobanteFolio: string | null;
  importe: number;
  formaPago: string;
  motivo: string;
  creadoEn: string;
  aplicadoEnSesionId: string | null;
}
