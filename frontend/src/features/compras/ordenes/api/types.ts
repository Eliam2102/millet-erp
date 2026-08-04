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
  moneda: string;
  /** ISO 8601 UTC. */
  fechaDocumento: string;
  referenciaProveedor: string | null;
}

/**
 * <b>Detalle de OC</b> — cabecera completa. Mirror de
 * <c>OrdenCompraResponse</c> (cabecera-only en el backend actual; ver
 * la nota de scope arriba). Payload de
 * <c>GET /api/v1/compras/ordenes/{id}</c>; el backend además expone el
 * <c>Version</c> vía header <c>ETag</c> que <c>useOrdenCompra</c>
 * captura para <c>If-Match</c> en mutaciones futuras.
 */
export interface OrdenCompraDetalleResponse {
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

/**
 * <b>Adjunto de OC</b> — mirror de <c>AdjuntoOcResponse</c> backend
 * (UF3-PR2). Consumido por el <c>&lt;AdjuntosManager/&gt;</c>.
 */
export interface AdjuntoOcResponse {
  id: string;
  tipoDocumentoId: string;
  nombreArchivo: string;
  /**
   * URL del blob storage (Azure Blob en prod, filesystem stub en dev).
   * Usado por el preview (PDF embed / img thumbnail) y para download.
   */
  blobUrl: string;
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
