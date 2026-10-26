/**
 * DTOs y enums del submódulo Compras Órdenes de Compra (mirror manual
 * del backend). Mismo criterio que <c>features/compras/api/types.ts</c>:
 * mientras no exista codegen ADR-0017 desde OpenAPI, este archivo es la
 * fuente de verdad del shape de datos OC y se mantiene en sincronía a
 * mano.
 *
 * <para><b>Scope UF1-PR1</b>: enums actualmente serializados por el
 * backend (<c>EstadoOrdenCompra</c>, los 3 sub-estados) + DTOs de las
 * dos queries de read en main (cabecera detalle y resumen de bandeja).
 * Líneas, autorizaciones, adjuntos, info logística/importación, totales
 * y descuentos NO existen como sub-objetos en el response actual del
 * backend (ver doc-comment de <c>OrdenCompraResponse.cs</c>:
 * "F2-PR3 cuando esas columnas se cableen al agregado como VOs").
 * Cuando el backend los exponga, se agregan acá en el PR que los
 * consuma. <c>NivelAutorizacion</c>, <c>ResultadoAutorizacion</c>,
 * <c>DescuentoTipo</c> y <c>TipoDocumentoOc</c> llegan en UF2/UF3/UF4
 * cuando entren las mutaciones que los usan.</para>
 *
 * <para>El backend serializa enums como número
 * (sin <c>JsonStringEnumConverter</c>) — declaramos enums como
 * <c>as const satisfies Record&lt;string, number&gt;</c>.</para>
 */

// ============================================================================
// Enums (mirror de backend/src/Compras/Domain/Oc/*.cs)
// ============================================================================

/**
 * 7 estados del agregado <c>OrdenCompra</c> (doc 01 §5.1 + state machine
 * §5.3). Mirror de <c>EstadoOrdenCompra.cs</c>.
 */
export const EstadoOrdenCompra = {
  Borrador: 0,
  EnAutorizacionJefeCompras: 1,
  EnAutorizacionDireccion: 2,
  Autorizada: 3,
  Cerrada: 4,
  Cancelada: 5,
  Rechazada: 6,
  CancelacionSolicitada: 7,
} as const satisfies Record<string, number>;
export type EstadoOrdenCompra =
  (typeof EstadoOrdenCompra)[keyof typeof EstadoOrdenCompra];

/**
 * Sub-estado de la dimensión Recepción (doc 01 §3.bis.2). Avanza
 * post-autorización vía listeners del módulo Almacén. Mirror de
 * <c>SubEstadoRecepcion.cs</c>.
 */
export const SubEstadoRecepcion = {
  SinRecepcion: 0,
  Parcial: 1,
  Completa: 2,
} as const satisfies Record<string, number>;
export type SubEstadoRecepcion =
  (typeof SubEstadoRecepcion)[keyof typeof SubEstadoRecepcion];

/**
 * Sub-estado de la dimensión Facturación. Avanza vía listeners del
 * módulo CxP cuando se registran facturas del proveedor. Mirror de
 * <c>SubEstadoFacturacion.cs</c>.
 */
export const SubEstadoFacturacion = {
  SinFactura: 0,
  Parcial: 1,
  Completa: 2,
} as const satisfies Record<string, number>;
export type SubEstadoFacturacion =
  (typeof SubEstadoFacturacion)[keyof typeof SubEstadoFacturacion];

/**
 * Sub-estado de la dimensión Pago. Avanza vía listeners del módulo
 * Tesorería cuando se aplican pagos a las facturas del proveedor.
 * Mirror de <c>SubEstadoPago.cs</c>. El cierre de la OC requiere
 * <c>Pagada</c>.
 */
export const SubEstadoPago = {
  SinPago: 0,
  Parcial: 1,
  Pagada: 2,
} as const satisfies Record<string, number>;
export type SubEstadoPago = (typeof SubEstadoPago)[keyof typeof SubEstadoPago];

/** F1-COM-11: tipo de respuesta del proveedor. Mirror de <c>TipoRespuestaProveedorOc.cs</c>. */
export const TipoRespuestaProveedorOc = {
  Confirmacion: 1,
  Rechazo: 2,
} as const satisfies Record<string, number>;
export type TipoRespuestaProveedorOc =
  (typeof TipoRespuestaProveedorOc)[keyof typeof TipoRespuestaProveedorOc];

/** F1-COM-07: tipo de incidencia del proveedor. Mirror de <c>TipoIncidenciaProveedorOc.cs</c>. */
export const TipoIncidenciaProveedorOc = {
  Faltante: 1,
  Danado: 2,
  Atraso: 3,
} as const satisfies Record<string, number>;
export type TipoIncidenciaProveedorOc =
  (typeof TipoIncidenciaProveedorOc)[keyof typeof TipoIncidenciaProveedorOc];

/** F1-COM-11: estado comercial de la OC. Mirror de <c>EstadoComercialProveedorOc.cs</c>. */
export const EstadoComercialProveedorOc = {
  RespuestaRegistrada: 4,
  SinRespuesta: 0,
  Confirmada: 1,
  ConfirmadaConDiferencias: 2,
  Rechazada: 3,
} as const satisfies Record<string, number>;
export type EstadoComercialProveedorOc =
  (typeof EstadoComercialProveedorOc)[keyof typeof EstadoComercialProveedorOc];

/**
 * Tipo de descuento aplicado en línea o cabecera (doc 01 §4.8).
 * Mirror de <c>DescuentoTipo.cs</c>. <c>Porcentaje</c>: el valor es
 * 0-100; el motor calcula <c>monto = subtotal * (valor / 100)</c>.
 * <c>Monto</c>: el valor es la cantidad fija a descontar (en la
 * moneda de la cabecera).
 */
export const DescuentoTipo = {
  Porcentaje: 0,
  Monto: 1,
} as const satisfies Record<string, number>;
export type DescuentoTipo =
  (typeof DescuentoTipo)[keyof typeof DescuentoTipo];

/**
 * Tipo de operación de comercio exterior de la OC. Mirror de
 * <c>TipoOperacionComercioExterior</c> (enum numérico del backend).
 */
// PLATFORM-TODO(<F1-COM-06>): supuesto Vilo, pendiente lista oficial de Millet.
export const TipoOperacionComercioExterior = {
  Importacion: 0,
  Exportacion: 1,
} as const satisfies Record<string, number>;
export type TipoOperacionComercioExterior =
  (typeof TipoOperacionComercioExterior)[keyof typeof TipoOperacionComercioExterior];

/**
 * Tipo de transporte de la mercancía en comercio exterior. Mirror de
 * <c>TipoTransporte</c> (enum numérico del backend).
 */
// PLATFORM-TODO(<F1-COM-06>): supuesto Vilo, pendiente lista oficial de Millet.
export const TipoTransporte = {
  Maritimo: 0,
  Aereo: 1,
  Terrestre: 2,
} as const satisfies Record<string, number>;
export type TipoTransporte = (typeof TipoTransporte)[keyof typeof TipoTransporte];

// ============================================================================
// Helpers — número ↔ string legible
// ============================================================================

const ESTADO_OC_LABELS: Record<EstadoOrdenCompra, string> = {
  [EstadoOrdenCompra.Borrador]: 'Borrador',
  [EstadoOrdenCompra.EnAutorizacionJefeCompras]: 'En autorización Jefe de Compras',
  [EstadoOrdenCompra.EnAutorizacionDireccion]: 'En autorización Dirección',
  [EstadoOrdenCompra.Autorizada]: 'Autorizada',
  [EstadoOrdenCompra.Cerrada]: 'Cerrada',
  [EstadoOrdenCompra.Cancelada]: 'Cancelada',
  [EstadoOrdenCompra.Rechazada]: 'Rechazada',
  [EstadoOrdenCompra.CancelacionSolicitada]: 'Cancelación solicitada',
};
export const estadoOcToString = (e: EstadoOrdenCompra): string =>
  ESTADO_OC_LABELS[e];

/**
 * Devuelve la clave del enum (no el label humano). Útil para alimentar
 * a <c>obtenerDefinicionOc</c> del glosario, que indexa por
 * <c>'Borrador'</c>, <c>'EnAutorizacionJefeCompras'</c>, etc.
 */
const ESTADO_OC_KEYS: Record<EstadoOrdenCompra, string> = {
  [EstadoOrdenCompra.Borrador]: 'Borrador',
  [EstadoOrdenCompra.EnAutorizacionJefeCompras]: 'EnAutorizacionJefeCompras',
  [EstadoOrdenCompra.EnAutorizacionDireccion]: 'EnAutorizacionDireccion',
  [EstadoOrdenCompra.Autorizada]: 'Autorizada',
  [EstadoOrdenCompra.Cerrada]: 'Cerrada',
  [EstadoOrdenCompra.Cancelada]: 'Cancelada',
  [EstadoOrdenCompra.Rechazada]: 'Rechazada',
  [EstadoOrdenCompra.CancelacionSolicitada]: 'CancelacionSolicitada',
};
export const estadoOcToKey = (e: EstadoOrdenCompra): string =>
  ESTADO_OC_KEYS[e];

const SUB_ESTADO_RECEPCION_LABELS: Record<SubEstadoRecepcion, string> = {
  [SubEstadoRecepcion.SinRecepcion]: 'Sin recepción',
  [SubEstadoRecepcion.Parcial]: 'Recepción parcial',
  [SubEstadoRecepcion.Completa]: 'Recepción completa',
};
export const subEstadoRecepcionToString = (s: SubEstadoRecepcion): string =>
  SUB_ESTADO_RECEPCION_LABELS[s];

const SUB_ESTADO_FACTURACION_LABELS: Record<SubEstadoFacturacion, string> = {
  [SubEstadoFacturacion.SinFactura]: 'Sin factura',
  [SubEstadoFacturacion.Parcial]: 'Facturación parcial',
  [SubEstadoFacturacion.Completa]: 'Facturación completa',
};
export const subEstadoFacturacionToString = (
  s: SubEstadoFacturacion,
): string => SUB_ESTADO_FACTURACION_LABELS[s];

const SUB_ESTADO_PAGO_LABELS: Record<SubEstadoPago, string> = {
  [SubEstadoPago.SinPago]: 'Sin pago',
  [SubEstadoPago.Parcial]: 'Pago parcial',
  [SubEstadoPago.Pagada]: 'Pagada',
};
export const subEstadoPagoToString = (s: SubEstadoPago): string =>
  SUB_ESTADO_PAGO_LABELS[s];

/** F1-COM-11 */
export const ESTADO_COMERCIAL_PROVEEDOR_LABELS: Record<EstadoComercialProveedorOc, string> = {
  [EstadoComercialProveedorOc.RespuestaRegistrada]: 'Respuesta sin clasificar',
  [EstadoComercialProveedorOc.SinRespuesta]: 'Sin respuesta del proveedor',
  [EstadoComercialProveedorOc.Confirmada]: 'Confirmada por el proveedor',
  [EstadoComercialProveedorOc.ConfirmadaConDiferencias]: 'Confirmada con diferencias',
  [EstadoComercialProveedorOc.Rechazada]: 'Rechazada por el proveedor',
};
/** F1-COM-11 */
export const ESTADO_COMERCIAL_PROVEEDOR_VARIANT: Record<
  EstadoComercialProveedorOc,
  'neutral' | 'success' | 'warning' | 'danger'
> = {
  [EstadoComercialProveedorOc.RespuestaRegistrada]: 'neutral',
  [EstadoComercialProveedorOc.SinRespuesta]: 'neutral',
  [EstadoComercialProveedorOc.Confirmada]: 'success',
  [EstadoComercialProveedorOc.ConfirmadaConDiferencias]: 'warning',
  [EstadoComercialProveedorOc.Rechazada]: 'danger',
};

// ============================================================================
// DTOs (mirror de los Response records del backend)
// ============================================================================

/**
 * <b>Item de la bandeja general</b> de OCs. Mirror de
 * <c>OrdenCompraResumen</c> (14 campos) — payload de
 * <c>GET /api/v1/compras/ordenes</c>.
 */
export interface OrdenCompraResumen {
  id: string;
  folio: string;
  folioAnio: number;
  estado: EstadoOrdenCompra;
  subEstadoRecepcion: SubEstadoRecepcion;
  subEstadoFacturacion: SubEstadoFacturacion;
  subEstadoPago: SubEstadoPago;
  proveedorId: string;
  /**
   * Razón social del proveedor, resuelta server-side (ADR-0042). Permite al
   * OcSelector/sheet mostrar el nombre sin resolverlo contra un catálogo
   * capado. <c>null</c> si el backend no la resolvió.
   */
  proveedorNombre: string | null;
  /**
   * Sucursal destino de la OC (proyectada de <c>SucursalDestinoId</c>).
   * Aditivo: permite derivar la sucursal al elegir la OC en la captura de
   * factura (CapturarFacturaSheet) sin pedirla a mano.
   */
  sucursalId: string;
  compradorTitularId: string;
  compradorNombre?: string | null;
  moneda: string;
  /** ISO 8601 UTC. */
  fechaDocumento: string;
  referenciaProveedor: string | null;
  /**
   * Campos de la bandeja de pendientes de autorización (<c>/pendientes-autorizacion</c>).
   * Son <c>null</c>/ausentes en las demás proyecciones.
   */
  cantidadTotal?: number | null;
  numeroLineas?: number | null;
  totalAPagar?: number | null;
  esMenorCuantia?: boolean | null;
  esRecurrente?: boolean | null;
  proveedorConocido?: boolean | null;
  /** <c>true</c> = la OC aún requiere Dirección (N2) después de N1. */
  requiereNivel2?: boolean | null;
  /** F1-COM-11: estado comercial según la respuesta del proveedor. */
  estadoComercialProveedor?: EstadoComercialProveedorOc | null;
  /** F1-COM-11: compromiso vigente (yyyy-MM-dd). */
  fechaCompromisoProveedor?: string | null;
}

/**
 * <b>Detalle de OC</b> — cabecera completa. Mirror de
 * <c>OrdenCompraResponse</c> (cabecera-only en el backend actual; ver
 * la nota de scope arriba). Payload de
 * <c>GET /api/v1/compras/ordenes/{id}</c>; el backend además expone el
 * <c>Version</c> vía header <c>ETag</c> que <c>useOrdenCompra</c>
 * captura para <c>If-Match</c> en mutaciones futuras.
 */
export interface SolicitudCancelacionOc {
  id: string;
  solicitanteId: string;
  fechaSolicitud: string;
  motivoCancelacionId: string;
  motivoSolicitud: string;
  resolutorId: string | null;
  fechaResolucion: string | null;
  confirmada: boolean | null;
  motivoResolucion: string | null;
}

/** F1-COM-11: fecha prometida por el proveedor para una cantidad de una línea. */
export interface CompromisoEntregaOc {
  id: string;
  lineaOrdenCompraId: string;
  cantidad: number;
  /** yyyy-MM-dd */
  fechaPrometida: string;
}

/** F1-COM-11: respuesta del proveedor (historial inmutable). */
export interface RespuestaProveedorOc {
  id: string;
  numero: number;
  tipo: TipoRespuestaProveedorOc | null;
  fechaRespuesta: string | null;
  evidencia: string | null;
  contactoProveedor: string | null;
  motivo: string | null;
  tieneDiferencias: boolean;
  registradaPorId: string;
  registradaEn: string;
  vigente: boolean;
  compromisos: CompromisoEntregaOc[];
}

/** F1-COM-07: fecha compromiso de una incidencia (historial inmutable; la de mayor número es la vigente). */
export interface CompromisoIncidenciaOc {
  id: string;
  numero: number;
  fechaCompromiso: string;
  motivo: string | null;
  registradoPorId: string;
  registradoEn: string;
}

/** F1-COM-07: incidencia del proveedor sobre una partida de la OC. */
export interface IncidenciaProveedorOc {
  id: string;
  numero: number;
  lineaOrdenCompraId: string;
  tipo: TipoIncidenciaProveedorOc;
  cantidad: number;
  descripcion: string | null;
  responsableId: string;
  responsableNombre: string | null;
  fechaCompromisoVigente: string;
  abierta: boolean;
  vencida: boolean;
  avisoVencidaEnviadoEn: string | null;
  registradaPorId: string;
  registradaEn: string;
  cerradaEn: string | null;
  cerradaPorId: string | null;
  cerradaPorRecepcion: boolean;
  motivoCierre: string | null;
  compromisos: CompromisoIncidenciaOc[];
}

/** F1-COM-02: cotización elegida que originó la OC. */
export interface CotizacionElegidaOc {
  cotizacionId: string;
  requisicionId: string;
  proveedorId: string;
  folioProveedor: string | null;
  motivoEleccion: string;
  elegidaEn: string;
  /** Precio cotizado por unidad de la RQ de cada partida (aviso si la línea se aparta de él). */
  precios: { lineaRequisicionId: string; precioPorUnidadRequisicion: number }[];
}

export interface RecepcionImportacion {
  recepcionId: string;
  numeroPedimento: string;
  numeroBl: string;
  numeroContenedor: string;
  aduana: string;
  clavePedimento: string;
  partidas: { lineaOcId: string; fraccionArancelaria: string }[];
}

export interface OrdenCompraDetalleResponse {
  recepcionesImportacion?: RecepcionImportacion[];
  /** Proveedor con compras previas recurrentes; ausente si no hay dato. */
  esRecurrente?: boolean;
  obra?: string | null;
  cicloAutorizacion?: number;
  autorizaciones?: AutorizacionOc[];
  solicitudesCancelacion?: SolicitudCancelacionOc[];
  /** F1-COM-11 */
  estadoComercialProveedor?: EstadoComercialProveedorOc;
  /** F1-COM-11 (yyyy-MM-dd) */
  fechaCompromisoProveedor?: string | null;
  /** F1-COM-11: más reciente primero. */
  respuestasProveedor?: RespuestaProveedorOc[];
  /** F1-COM-07: incidencias del proveedor, la más reciente primero. */
  incidencias?: IncidenciaProveedorOc[];
  /** Campos de cabecera aún en TBD; la OC no se puede transmitir mientras haya alguno. */
  camposPorCapturar?: ('proveedor' | 'condicionesPago' | 'usoPrincipal')[] | null;
  // F1-COM-02
  cotizacionElegida?: CotizacionElegidaOc | null;
  id: string;
  empresaId: string;
  folio: string;
  folioAnio: number;
  proveedorId: string;
  // Etiqueta del proveedor resuelta en backend (ADR-0042 addendum); null → cae al id.
  proveedorRazonSocial: string | null;
  proveedorClave: string | null;
  sucursalDestinoId: string;
  condicionesPagoId: string;
  usoPrincipalId: string;
  moneda: string;
  tipoCambio: number | null;
  compradorTitularId: string;
  compradorNombre?: string | null;
  encargadoComprasId: string;
  observaciones: string | null;
  sinRequisicionPrevia: boolean;
  esImportacion: boolean;
  cotizacionExcepcionada: boolean;
  /** ISO 8601 UTC. */
  fechaDocumento: string;
  /** ISO 8601 UTC, opcional. */
  fechaContabilizacion: string | null;
  /** ISO 8601 UTC, opcional. */
  fechaEntregaEsperada: string | null;
  estado: EstadoOrdenCompra;
  subEstadoRecepcion: SubEstadoRecepcion;
  subEstadoFacturacion: SubEstadoFacturacion;
  subEstadoPago: SubEstadoPago;
  motivoSinRequisicion: string | null;
  motivoCancelacion: string | null;
  motivoRechazoId: string | null;
  motivoRechazoTexto: string | null;
  ocOrigenId: string | null;
  /** Optimistic concurrency token (también expuesto vía header ETag). */
  version: number;
  /** ISO 8601 UTC. */
  createdAt: string;
  /** ISO 8601 UTC. */
  updatedAt: string;

  // UF3-PR1: campos F2-PR3 expuestos en el detalle (sub-tabs
  // Logística / Importación / Financiera).
  referenciaProveedor: string | null;
  contactoProveedorNombre: string | null;
  contactoProveedorEmail: string | null;
  contactoProveedorTelefono: string | null;
  infoLogisticaDireccion: string | null;
  infoLogisticaTransportistaId: string | null;
  infoLogisticaTransportistaTexto: string | null;
  infoLogisticaNumeroGuia: string | null;
  infoLogisticaInstrucciones: string | null;
  infoImportIncotermId: string | null;
  infoImportPaisOrigen: string | null;
  infoImportNumeroContenedor: string | null;
  infoImportCodigoRuta: string | null;
  infoImportSemanaEmbarque: string | null;
  infoImportNumeroPedimento: string | null;
  // PLATFORM-TODO(<F1-COM-06>): supuesto Vilo, campos de comercio exterior.
  infoImportTipoOperacion: TipoOperacionComercioExterior | null;
  infoImportNumeroBl: string | null;
  infoImportTipoTransporte: TipoTransporte | null;
  infoImportAgenteAduanalNaviera: string | null;
  infoImportPuertoAduanaEntrada: string | null;
  /** Fecha estimada de arribo (ETA) en formato <c>yyyy-MM-dd</c>. */
  infoImportFechaEstimadaArribo: string | null;
  infoImportCertificadoOrigen: boolean;
  descuentoGlobalTipo: DescuentoTipo | null;
  descuentoGlobalValor: number | null;
  gastosAdicionales: number;
  redondeo: number;
  /**
   * Líneas del agregado, expuestas en el detalle desde UF2-PR3-a.
   * Backend hace <c>.Include(o => o.Lineas)</c> + Mapster proyecta
   * la collection. Recién creada sin líneas: array vacío.
   */
  lineas: LineaOrdenCompraResponse[];

  /**
   * Adjuntos del agregado, expuestos en el detalle desde UF3-PR2.
   * Mismo patrón que <c>lineas</c>: <c>.Include(o => o.Adjuntos)</c>
   * + Mapster. Recién creada sin adjuntos: array vacío.
   */
  adjuntos: AdjuntoOcResponse[];
}

export interface AutorizacionOc {
  id: string;
  ciclo: number;
  nivel: 1 | 2;
  resultado: 1 | 2;
  usuarioId: string;
  fechaHora: string;
  motivoRechazoId: string | null;
  motivoRechazoTexto: string | null;
  notas: string | null;
  motivoRechazoNombre?: string | null;
}

/**
 * <b>Adjunto de OC</b> — mirror de <c>AdjuntoOcResponse</c> backend
 * (UF3-PR2). Consumido por el <c>&lt;AdjuntosManager/&gt;</c>.
 */
export interface AdjuntoOcResponse {
  id: string;
  tipoDocumentoId: string;
  nombreArchivo: string;
  contentType: string;
  tamanoBytes: number;
  /** ISO 8601 UTC. */
  fechaCarga: string;
  usuarioCargaId: string;
}

/**
 * <b>Línea de OC</b> — mirror de <c>LineaOrdenCompraResponse</c>
 * backend (UF2-PR3-a). Cada item del array <c>lineas</c> en
 * <c>OrdenCompraDetalleResponse</c>.
 *
 * <para><c>subtotalLinea</c> es computed por el agregado
 * (<c>(cantidad * precioUnitario) - descuento</c> redondeado a 2
 * decimales). El frontend NO lo recalcula; si el caller cambia
 * cantidad/precio client-side, debe esperar al server-roundtrip o
 * recalcular client-side de su cuenta para preview.</para>
 *
 * <para><c>requisicionId</c> ≠ null indica que la línea viene de una
 * RQ (modo 1:1 o consolidación) — cantidad/artículo NO son editables
 * en esa línea (política §4.2). <c>requisicionId</c> = null = línea
 * manual creada en una OC <c>SinRequisicionPrevia</c>.</para>
 */
export interface LineaOrdenCompraResponse {
  descuentoTipo?: DescuentoTipo;
  descuentoValor?: number;
  id: string;
  posicion: number;
  articuloId: string;
  // Etiqueta del artículo resuelta en backend (ADR-0042 addendum); null → cae al id.
  articuloClave: string | null;
  articuloNombre: string | null;
  descripcionExtendida: string | null;
  /** Decimal serializado como number; la moneda vive en
   * <c>OrdenCompraDetalleResponse.moneda</c> (cabecera). */
  cantidad: number;
  unidadMedida: string;
  precioUnitario: number;
  ivaImporte: number;
  retencionIsr: number | null;
  /** Computed en backend; no recalcular client-side sin re-fetch. */
  subtotalLinea: number;
  departamentoSolicitanteId: string;
  /** CC-Máquina (Dim3) de la línea (Fase E PR3). Heredado de la RQ si la línea
   * viene de una RQ (read-only); elegido por el comprador si es manual. */
  centroCostoId: string | null;
  /** Etiqueta del CC-Máquina resuelta en backend por IDim3ReadPort (sin filtro,
   * incluye inactivas). null → el FE cae a "No catalogado". */
  centroCostoClave: string | null;
  centroCostoNombre: string | null;
  /** <c>null</c> si línea manual; not-null si vino de una RQ. */
  requisicionId: string | null;
  lineaRequisicionId: string | null;
  /** Folio humano de la RQ origen (p. ej. <c>MID2026-000123</c>), resuelto
   * en el backend (ADR-0042). <c>null</c> si línea manual o si la RQ no
   * resuelve; el badge cae al short-id del GUID en ese caso. */
  requisicionFolio: string | null;
  /** ISO 8601, opcional (override de la fecha de cabecera por línea). */
  fechaEntregaLinea: string | null;
  cantidadRecibida: number;
  cantidadFacturada: number;
  textoAdicional: string | null;
  /** GAP-9 / CA2.10: servicio (artículo `Servicio`); no se recibe en Almacén. */
  esServicio?: boolean;
  esActivoFijo?: boolean;
  claseActivoId?: string | null;
  /**
   * Fracción arancelaria (8 o 10 dígitos). Solo aplica a OC de comercio
   * exterior; <c>null</c> si no se capturó.
   */
  // PLATFORM-TODO(<F1-COM-06>): supuesto Vilo, campo de comercio exterior.
  fraccionArancelaria: string | null;
  /** F1-MP-01 (E1): medida de la hoja (vidrio crudo). Null/ausente si el artículo no maneja medidas
   * o es una línea heredada de RQ aún sin medida. La cantidad sigue en m² (= hojas × <c>areaHojaM2</c>). */
  productoAwVarianteId?: string | null;
  anchoMm?: number | null;
  altoMm?: number | null;
  areaHojaM2?: number | null;
  /** Artículo de vidrio crudo (A+W) con la línea aún sin medida: se exige antes de transmitir. */
  requiereMedida?: boolean;
}

/** Medida de hoja de un artículo de vidrio crudo (<c>GET /api/v1/catalogos/articulos/{id}/medidas</c>). */
export interface MedidaHojaArticulo {
  varianteId: string;
  claveVariante: string;
  anchoMm: number | null;
  altoMm: number | null;
  espesorMm: number | null;
  /** m² por hoja; null si A+W no informó ancho o alto (no elegible). */
  areaHojaM2: number | null;
}

/**
 * Paged response de la bandeja OC. <b>Notar diferencia con RQ</b>: la
 * lista de OC usa <c>page</c>/<c>pageSize</c>/<c>totalCount</c> (mirror
 * de <c>ListarOrdenesCompraResponse</c>), mientras que RQ usa
 * <c>offset</c>/<c>limit</c>/<c>total</c>. Cada submódulo respeta el
 * shape real del backend; UF1-PR2 lo consume tal cual en la bandeja.
 */
export interface ListarOrdenesCompraResponse {
  items: OrdenCompraResumen[];
  page: number;
  pageSize: number;
  totalCount: number;
}
