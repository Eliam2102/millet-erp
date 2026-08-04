/**
 * Tipos compartidos del módulo CxP frontend (FE-F1-PR1+). Mirror manual
 * de los DTOs que devuelve el backend en
 * <c>/api/v1/cuentas-por-pagar/...</c>. Cuando ADR-0017 (codegen TS
 * desde OpenAPI) entre, este archivo se vuelve autogenerado.
 *
 * <para>Convención: enums se modelan como <c>const</c> + <c>type</c>
 * union para cumplir <c>erasableSyntaxOnly</c> (mismo patrón que
 * <c>compras/api/types.ts</c> y <c>almacen/api/types.ts</c>).</para>
 */

// ─── Paged response (compartido con backend Common.PagedResponse) ─────

export interface PagedResponse<T> {
  items: T[];
  offset: number;
  limit: number;
  total: number;
}

// ─── CFDIs recibidos (FE-F1-PR1) ──────────────────────────────────────

/** Estado del CFDI recibido (alineado con backend §F1-PR1). */
export const EstadoCfdiRecibido = {
  PorProcesar: 1,
  ConvertidoEnPasivo: 2,
  Duplicado: 3,
  Descartado: 4,
} as const satisfies Record<string, number>;
export type EstadoCfdiRecibido =
  (typeof EstadoCfdiRecibido)[keyof typeof EstadoCfdiRecibido];

/** Tipo del CFDI según el SAT (00-Ingreso, 01-Egreso, etc.). */
export const TipoCfdi = {
  Desconocido: 0,
  Ingreso: 1,
  Egreso: 2,
  Pago: 3,
  Traslado: 4,
  Nomina: 5,
} as const satisfies Record<string, number>;
export type TipoCfdi = (typeof TipoCfdi)[keyof typeof TipoCfdi];

/** Canal por el cual entró el CFDI al sistema. */
export const CanalOrigenCfdi = {
  Desconocido: 0,
  DescargaSat: 1,
  Mailbox: 2,
  CargaManual: 3,
  PortalProveedor: 4,
} as const satisfies Record<string, number>;
export type CanalOrigenCfdi =
  (typeof CanalOrigenCfdi)[keyof typeof CanalOrigenCfdi];

export interface CfdiListItem {
  id: string;
  uuidCfdi: string;
  rfcEmisor: string;
  tipo: TipoCfdi;
  folio: string | null;
  serie: string | null;
  fechaCfdi: string;
  total: number;
  /** Importes persistidos desde la ingesta; pre-llenan la captura. */
  subtotal: number;
  impuestosTrasladados: number;
  retenciones: number;
  tipoCambio: number | null;
  moneda: string;
  canalOrigen: CanalOrigenCfdi;
  fechaRecepcion: string;
  estado: EstadoCfdiRecibido;
}

/** Respuesta de GET /cfdis/{id}/parseado — XML re-parseado on-demand. */
export interface CfdiParseado {
  id: string;
  uuidCfdi: string;
  total: number;
  subtotal: number;
  impuestosTrasladados: number;
  retenciones: number;
  moneda: string;
  tipoCambio: number | null;
  lineas: CfdiLineaParseada[];
  /** Nodos cfdi:CfdiRelacionados — prellenan la relación de NC/anticipo. */
  cfdiRelacionados: CfdiRelacionados[] | null;
}

/** Nodo cfdi:CfdiRelacionados: TipoRelacion SAT (01/03/07…) + UUIDs. */
export interface CfdiRelacionados {
  tipoRelacion: string;
  uuids: string[];
}

/** Línea de cfdi:Concepto extraída del XML para precarga. */
export interface CfdiLineaParseada {
  posicion: number;
  claveProdServ: string;
  cantidad: number;
  claveUnidad: string;
  unidad: string | null;
  descripcion: string;
  valorUnitario: number;
  importe: number;
  descuento: number | null;
}

/** Respuesta de GET /cfdis/{id} — detalle para el viewer de la bandeja. */
export interface CfdiDetalle {
  id: string;
  uuidCfdi: string;
  rfcEmisor: string;
  tipo: TipoCfdi;
  folio: string | null;
  serie: string | null;
  fechaCfdi: string;
  total: number;
  subtotal: number;
  impuestosTrasladados: number;
  retenciones: number;
  tipoCambio: number | null;
  moneda: string;
  canalOrigen: CanalOrigenCfdi;
  fechaRecepcion: string;
  estado: EstadoCfdiRecibido;
  motivoDescarte: string | null;
  cfdiOriginalId: string | null;
  documentoDestinoId: string | null;
  tieneXml: boolean;
  tienePdf: boolean;
}

// ─── Labels para UI ───────────────────────────────────────────────────

export const EstadoCfdiRecibidoLabels: Record<EstadoCfdiRecibido, string> = {
  [EstadoCfdiRecibido.PorProcesar]: 'Por procesar',
  [EstadoCfdiRecibido.ConvertidoEnPasivo]: 'Convertido en pasivo',
  [EstadoCfdiRecibido.Duplicado]: 'Duplicado',
  [EstadoCfdiRecibido.Descartado]: 'Descartado',
};

export const TipoCfdiLabels: Record<TipoCfdi, string> = {
  [TipoCfdi.Desconocido]: 'Desconocido',
  [TipoCfdi.Ingreso]: 'Ingreso',
  [TipoCfdi.Egreso]: 'Egreso',
  [TipoCfdi.Pago]: 'Pago (REP)',
  [TipoCfdi.Traslado]: 'Traslado',
  [TipoCfdi.Nomina]: 'Nómina',
};

export const CanalOrigenCfdiLabels: Record<CanalOrigenCfdi, string> = {
  [CanalOrigenCfdi.Desconocido]: 'Desconocido',
  [CanalOrigenCfdi.DescargaSat]: 'Descarga SAT',
  [CanalOrigenCfdi.Mailbox]: 'Mailbox',
  [CanalOrigenCfdi.CargaManual]: 'Carga manual',
  [CanalOrigenCfdi.PortalProveedor]: 'Portal proveedor',
};

// ─── Comandos CFDIs (FE-F1-PR1) ───────────────────────────────────────

export interface DescartarCfdiCommand {
  motivo: string;
}

export interface MarcarCfdiDuplicadoCommand {
  cfdiOriginalId: string;
}

/** Respuesta de POST /cfdis/cargar (carga manual de respaldo). */
export interface IngresarCfdiResponse {
  id: string;
  uuidCfdi: string;
  estado: EstadoCfdiRecibido;
  cfdiOriginalId: string | null;
}

// ─── Facturas (FE-F2-PR1) ─────────────────────────────────────────────

/** Estado del ciclo de vida del pasivo (factura). */
export const EstadoPasivo = {
  Capturada: 1,
  EnRevision: 2,
  Autorizada: 3,
  Pagada: 4,
  Cancelada: 5,
} as const satisfies Record<string, number>;
export type EstadoPasivo = (typeof EstadoPasivo)[keyof typeof EstadoPasivo];

/** Motivo de cancelación de factura. */
export const MotivoCancelacion = {
  RechazadaPorTolerancia: 1,
  CfdiCanceladoEnSat: 2,
  ErrorCaptura: 3,
  OtroConTexto: 99,
} as const satisfies Record<string, number>;
export type MotivoCancelacion =
  (typeof MotivoCancelacion)[keyof typeof MotivoCancelacion];

export const EstadoPasivoLabels: Record<EstadoPasivo, string> = {
  [EstadoPasivo.Capturada]: 'Capturada',
  [EstadoPasivo.EnRevision]: 'En revisión',
  [EstadoPasivo.Autorizada]: 'Autorizada',
  [EstadoPasivo.Pagada]: 'Pagada',
  [EstadoPasivo.Cancelada]: 'Cancelada',
};

export const MotivoCancelacionLabels: Record<MotivoCancelacion, string> = {
  [MotivoCancelacion.RechazadaPorTolerancia]: 'Rechazada por tolerancia',
  [MotivoCancelacion.CfdiCanceladoEnSat]: 'CFDI cancelado en SAT',
  [MotivoCancelacion.ErrorCaptura]: 'Error de captura',
  [MotivoCancelacion.OtroConTexto]: 'Otro (con texto)',
};

export interface FacturaListItem {
  id: string;
  proveedorId: string;
  sucursalId: string;
  folioProveedor: string | null;
  serieProveedor: string | null;
  fechaDocumento: string;
  fechaVencimiento: string;
  total: number;
  saldoPendiente: number;
  moneda: string;
  estado: EstadoPasivo;
  ordenCompraId: string | null;
  version: number;
  /** Razón social resuelta server-side (ADR-0042); null si no resuelve. */
  proveedorNombre: string | null;
}

export interface FacturaLinea {
  id: string;
  posicion: number;
  articuloId: string | null;
  claveProdServ: string | null;
  descripcion: string;
  cantidad: number;
  claveUnidad: string;
  precioUnitario: number;
  importe: number;
  descuento: number | null;
  lineaOcId: string | null;
  conceptoContableId: string | null;
}

export interface FacturaDetalle {
  id: string;
  empresaId: string;
  cfdiRecibidoId: string | null;
  uuidCfdi: string | null;
  proveedorId: string;
  sucursalId: string;
  folioProveedor: string | null;
  serieProveedor: string | null;
  fechaDocumento: string;
  fechaContabilizacion: string;
  fechaVencimiento: string;
  moneda: string;
  tipoCambio: number | null;
  subtotal: number;
  descuentos: number;
  impuestosTrasladados: number;
  retenciones: number;
  total: number;
  ordenCompraId: string | null;
  estado: EstadoPasivo;
  diferenciaContraOc: number;
  anticipoAplicadoTotal: number;
  ncAplicadasTotal: number;
  importePagado: number;
  saldoPendiente: number;
  motivoCancelacion: MotivoCancelacion | null;
  motivoCancelacionTexto: string | null;
  enRevision: boolean;
  version: number;
  lineas: FacturaLinea[];
  /** Etiquetas resueltas server-side (ADR-0042); null si no resuelven. */
  proveedorNombre: string | null;
  sucursalNombre: string | null;
}

export interface CapturarFacturaConOcLinea {
  articuloId: string | null;
  claveProdServ: string | null;
  descripcion: string;
  cantidad: number;
  claveUnidad: string;
  unidad: string | null;
  precioUnitario: number;
  importe: number;
  descuento: number | null;
  lineaOcId: string | null;
  conceptoContableId: string | null;
}

export interface CapturarFacturaConOcCommand {
  ordenCompraId: string;
  proveedorId: string;
  sucursalId: string;
  cfdiRecibidoId: string | null;
  uuidCfdi: string | null;
  folioProveedor: string | null;
  serieProveedor: string | null;
  fechaDocumento: string;
  fechaContabilizacion: string;
  fechaVencimiento: string;
  moneda: string;
  tipoCambio: number | null;
  subtotal: number;
  descuentos: number;
  impuestosTrasladados: number;
  retenciones: number;
  total: number;
  lineas: CapturarFacturaConOcLinea[];
}

export interface CapturarFacturaConOcResponse {
  id: string;
  estado: EstadoPasivo;
  diferenciaContraOc: number;
  saldoPendiente: number;
  motivoCancelacion: MotivoCancelacion | null;
  version: number;
}

export interface CancelarFacturaCommand {
  motivo: MotivoCancelacion;
  texto: string | null;
}

export interface CancelarFacturaResponse {
  id: string;
  estado: EstadoPasivo;
  version: number;
}

export interface EditarCabeceraFacturaCommand {
  folioProveedor: string | null;
  serieProveedor: string | null;
  fechaVencimiento: string;
  fechaContabilizacion: string;
}

export interface EditarCabeceraFacturaResponse {
  id: string;
  version: number;
}

export interface AutorizarFacturaResponse {
  id: string;
  estado: EstadoPasivo;
  version: number;
}

// ─── Revisión por área (FE-F3-PR1) ────────────────────────────────────

export interface MotivoRevision {
  id: string;
  codigo: string;
  nombre: string;
  descripcion: string | null;
  slaDias: number | null;
  dependenciaRevisoraDefaultCodigo: string | null;
  activo: boolean;
}

export interface FacturaEnRevisionItem {
  id: string;
  proveedorId: string;
  sucursalId: string;
  folioProveedor: string | null;
  serieProveedor: string | null;
  fechaDocumento: string;
  fechaVencimiento: string;
  total: number;
  saldoPendiente: number;
  moneda: string;
  motivoRevisionId: string;
  fechaEntradaRevision: string;
  diasEnRevision: number;
  version: number;
}

export interface EnviarFacturaARevisionCommand {
  motivoRevisionId: string;
  dependenciaRevisoraId: string;
}

export interface EnviarFacturaARevisionResponse {
  id: string;
  estado: EstadoPasivo;
  version: number;
}

export interface LiberarRevisionFacturaCommand {
  accionTomada: string;
}

export interface LiberarRevisionFacturaResponse {
  id: string;
  estado: EstadoPasivo;
  version: number;
}

// ─── Evidencias de autorización (FE-F3-PR1) ───────────────────────────

export const TipoEvidencia = {
  CapturaWhatsapp: 1,
  Audio: 2,
  Email: 3,
  FirmaEscaneada: 4,
  Otro: 99,
} as const satisfies Record<string, number>;
export type TipoEvidencia = (typeof TipoEvidencia)[keyof typeof TipoEvidencia];

export const EstadoFirmaFisica = {
  NoAplica: 1,
  Pendiente: 2,
  Recibida: 3,
} as const satisfies Record<string, number>;
export type EstadoFirmaFisica =
  (typeof EstadoFirmaFisica)[keyof typeof EstadoFirmaFisica];

export const TipoEvidenciaLabels: Record<TipoEvidencia, string> = {
  [TipoEvidencia.CapturaWhatsapp]: 'Captura WhatsApp',
  [TipoEvidencia.Audio]: 'Audio',
  [TipoEvidencia.Email]: 'Email',
  [TipoEvidencia.FirmaEscaneada]: 'Firma escaneada',
  [TipoEvidencia.Otro]: 'Otro',
};

export const EstadoFirmaFisicaLabels: Record<EstadoFirmaFisica, string> = {
  [EstadoFirmaFisica.NoAplica]: 'No aplica',
  [EstadoFirmaFisica.Pendiente]: 'Pendiente',
  [EstadoFirmaFisica.Recibida]: 'Recibida',
};

export interface Evidencia {
  id: string;
  tipo: TipoEvidencia;
  archivoBlobRef: string;
  nombreArchivo: string;
  contentType: string;
  tamanioBytes: number | null;
  comentario: string;
  estadoFirmaFisica: EstadoFirmaFisica;
  fechaLimiteFirmaFisica: string | null;
  fechaRecepcionFirmaFisica: string | null;
  capturadoPor: string | null;
  fechaCaptura: string;
}

export interface AdjuntarEvidenciaResponse {
  id: string;
}

// ─── Notas de crédito (FE-F4-PR1) ─────────────────────────────────────

export const EstadoNotaCredito = {
  EnEspera: 1,
  Abierta: 2,
  Aplicada: 3,
  Cancelada: 4,
} as const satisfies Record<string, number>;
export type EstadoNotaCredito =
  (typeof EstadoNotaCredito)[keyof typeof EstadoNotaCredito];

export const TipoNotaCredito = {
  Descuento: 1,
  Devolucion: 2,
  AmortizacionAnticipo: 3,
} as const satisfies Record<string, number>;
export type TipoNotaCredito =
  (typeof TipoNotaCredito)[keyof typeof TipoNotaCredito];

export const TipoRelacionCfdi = {
  NotaCredito: 1,
  Devolucion: 3,
  AmortizacionAnticipo: 7,
} as const satisfies Record<string, number>;
export type TipoRelacionCfdi =
  (typeof TipoRelacionCfdi)[keyof typeof TipoRelacionCfdi];

export const EstadoNotaCreditoLabels: Record<EstadoNotaCredito, string> = {
  [EstadoNotaCredito.EnEspera]: 'En espera',
  [EstadoNotaCredito.Abierta]: 'Abierta',
  [EstadoNotaCredito.Aplicada]: 'Aplicada',
  [EstadoNotaCredito.Cancelada]: 'Cancelada',
};

export const TipoNotaCreditoLabels: Record<TipoNotaCredito, string> = {
  [TipoNotaCredito.Descuento]: 'Descuento (01)',
  [TipoNotaCredito.Devolucion]: 'Devolución (03)',
  [TipoNotaCredito.AmortizacionAnticipo]: 'Amortización anticipo (07)',
};

export interface NotaCreditoListItem {
  id: string;
  uuidCfdi: string;
  proveedorId: string;
  folioProveedor: string | null;
  serieProveedor: string | null;
  fechaCfdi: string;
  total: number;
  moneda: string;
  tipo: number;
  tipoRelacionCfdi: number;
  uuidRelacionCfdi: string;
  facturaOrigenId: string | null;
  saldoPorAplicar: number;
  estado: EstadoNotaCredito;
  version: number;
  /** Razón social resuelta server-side (ADR-0042); null si no resuelve. */
  proveedorNombre: string | null;
}

/**
 * Espejo de <c>NotaCreditoDetalleResponse</c> (backend
 * ObtenerNotaCreditoQuery, PR #632). Detalle read-only con importes
 * desglosados, vínculo con CfdiRecibido y trazabilidad.
 */
export interface NotaCreditoDetalle {
  id: string;
  cfdiRecibidoId: string | null;
  uuidCfdi: string;
  proveedorId: string;
  folioProveedor: string | null;
  serieProveedor: string | null;
  fechaCfdi: string;
  moneda: string;
  tipoCambio: number | null;
  subtotal: number;
  impuestosTrasladados: number;
  retenciones: number;
  total: number;
  tipo: TipoNotaCredito;
  tipoRelacionCfdi: TipoRelacionCfdi;
  uuidRelacionCfdi: string;
  facturaOrigenId: string | null;
  montoAplicado: number;
  saldoPorAplicar: number;
  estado: EstadoNotaCredito;
  fechaCaptura: string;
  fechaMatch: string | null;
  fechaCancelacion: string | null;
  motivoCancelacion: string | null;
  capturadoPor: string | null;
  version: number;
  /** Razón social resuelta server-side (ADR-0042); null si no resuelve. */
  proveedorNombre: string | null;
}

export interface CapturarNotaCreditoCommand {
  cfdiRecibidoId: string | null;
  uuidCfdi: string;
  proveedorId: string;
  folioProveedor: string | null;
  serieProveedor: string | null;
  fechaCfdi: string;
  moneda: string;
  tipoCambio: number | null;
  subtotal: number;
  impuestosTrasladados: number;
  retenciones: number;
  total: number;
  tipo: TipoNotaCredito;
  tipoRelacionCfdi: TipoRelacionCfdi;
  uuidRelacionCfdi: string;
}

export interface CapturarNotaCreditoResponse {
  id: string;
  estado: EstadoNotaCredito;
  facturaOrigenId: string | null;
  saldoPorAplicar: number;
  version: number;
}

export interface VincularFacturaNotaCreditoCommand {
  facturaOrigenId: string;
}

export interface VincularFacturaNotaCreditoResponse {
  id: string;
  estado: EstadoNotaCredito;
  version: number;
}

// ─── Anticipos (FE-F4-PR1) ────────────────────────────────────────────

export const EstadoAnticipo = {
  Abierto: 1,
  Amortizado: 2,
  Cancelado: 3,
} as const satisfies Record<string, number>;
export type EstadoAnticipo =
  (typeof EstadoAnticipo)[keyof typeof EstadoAnticipo];

export const EstadoAnticipoLabels: Record<EstadoAnticipo, string> = {
  [EstadoAnticipo.Abierto]: 'Abierto',
  [EstadoAnticipo.Amortizado]: 'Amortizado',
  [EstadoAnticipo.Cancelado]: 'Cancelado',
};

export interface AnticipoListItem {
  id: string;
  uuidCfdi: string;
  proveedorId: string;
  serie: string;
  folioProveedor: string | null;
  fechaCfdi: string;
  moneda: string;
  montoEntregado: number;
  montoAmortizado: number;
  saldoAmortizable: number;
  ordenCompraId: string | null;
  estado: EstadoAnticipo;
  version: number;
  /** Razón social resuelta server-side (ADR-0042); null si no resuelve. */
  proveedorNombre: string | null;
}

export interface CapturarAnticipoCommand {
  cfdiRecibidoId: string | null;
  uuidCfdi: string;
  proveedorId: string;
  serie: string;
  folioProveedor: string | null;
  fechaCfdi: string;
  moneda: string;
  tipoCambio: number | null;
  montoEntregado: number;
  ordenCompraId: string | null;
}

export interface CapturarAnticipoResponse {
  id: string;
  estado: EstadoAnticipo;
  saldoAmortizable: number;
  version: number;
}

// ─── Notas de cargo (FE-F4-PR1) ───────────────────────────────────────

export const EstadoNotaCargo = {
  Borrador: 1,
  Autorizada: 2,
  Aplicada: 3,
  Formalizada: 4,
  Cancelada: 5,
} as const satisfies Record<string, number>;
export type EstadoNotaCargo =
  (typeof EstadoNotaCargo)[keyof typeof EstadoNotaCargo];

export const EstadoNotaCargoLabels: Record<EstadoNotaCargo, string> = {
  [EstadoNotaCargo.Borrador]: 'Borrador',
  [EstadoNotaCargo.Autorizada]: 'Autorizada',
  [EstadoNotaCargo.Aplicada]: 'Aplicada',
  [EstadoNotaCargo.Formalizada]: 'Formalizada',
  [EstadoNotaCargo.Cancelada]: 'Cancelada',
};

export interface NotaCargoListItem {
  id: string;
  folio: string;
  folioAnio: number;
  proveedorId: string;
  sucursalId: string | null;
  concepto: string;
  monto: number;
  moneda: string;
  facturaOrigenId: string | null;
  devolucionAProveedorId: string | null;
  estado: EstadoNotaCargo;
  fechaCreacion: string;
  version: number;
  /** Razón social resuelta server-side (ADR-0042); null si no resuelve. */
  proveedorNombre: string | null;
}

/**
 * Espejo de <c>NotaCargoDetalleResponse</c> (backend
 * ObtenerNotaCargoQuery, PR #632). Incluye la devolución 8.B que la
 * originó, la NC del proveedor que la concilió y la trazabilidad
 * completa del ciclo.
 */
export interface NotaCargoDetalle {
  id: string;
  folio: string;
  folioAnio: number;
  proveedorId: string;
  sucursalId: string | null;
  concepto: string;
  conceptoContableId: string | null;
  monto: number;
  moneda: string;
  tipoCambio: number | null;
  facturaOrigenId: string | null;
  devolucionAProveedorId: string | null;
  notaCreditoProveedorId: string | null;
  estado: EstadoNotaCargo;
  creadoPor: string | null;
  autorizadoPor: string | null;
  aplicadoPor: string | null;
  fechaCreacion: string;
  fechaAutorizacion: string | null;
  fechaAplicacion: string | null;
  fechaFormalizacion: string | null;
  fechaCancelacion: string | null;
  motivoCancelacion: string | null;
  version: number;
  /** Razón social resuelta server-side (ADR-0042); null si no resuelve. */
  proveedorNombre: string | null;
}

export interface CrearNotaCargoCommand {
  proveedorId: string;
  sucursalId: string | null;
  concepto: string;
  conceptoContableId: string | null;
  monto: number;
  moneda: string;
  tipoCambio: number | null;
  facturaOrigenId: string | null;
  devolucionAProveedorId: string | null;
}

export interface CrearNotaCargoResponse {
  id: string;
  folio: string;
  estado: EstadoNotaCargo;
  version: number;
}

// ─── Aplicación inline a factura (FE-F4-PR1) ──────────────────────────

export interface AplicarNotaCreditoAFacturaCommand {
  notaCreditoId: string;
  notaCreditoVersionEsperada: number;
  monto: number;
}

export interface AplicarAnticipoAFacturaCommand {
  anticipoId: string;
  anticipoVersionEsperada: number;
  monto: number;
}

// ─── Comprobaciones de gastos (FE-F4-PR2) ─────────────────────────────

export const TipoComprobacionGastos = {
  ReembolsoCajaChica: 1,
  GastosAduanales: 2,
  Viaticos: 3,
  TarjetaCredito: 4,
} as const satisfies Record<string, number>;
export type TipoComprobacionGastos =
  (typeof TipoComprobacionGastos)[keyof typeof TipoComprobacionGastos];

export const EstadoComprobacionGastos = {
  Borrador: 1,
  PorRevisar: 2,
  Autorizada: 3,
  Aplicada: 4,
  Rechazada: 5,
  AutorizadaNivel1: 6,
} as const satisfies Record<string, number>;
export type EstadoComprobacionGastos =
  (typeof EstadoComprobacionGastos)[keyof typeof EstadoComprobacionGastos];

export const TipoComprobacionGastosLabels: Record<
  TipoComprobacionGastos,
  string
> = {
  [TipoComprobacionGastos.ReembolsoCajaChica]: 'Caja chica',
  [TipoComprobacionGastos.GastosAduanales]: 'Aduanales',
  [TipoComprobacionGastos.Viaticos]: 'Viáticos',
  [TipoComprobacionGastos.TarjetaCredito]: 'Tarjeta crédito',
};

export const EstadoComprobacionGastosLabels: Record<
  EstadoComprobacionGastos,
  string
> = {
  [EstadoComprobacionGastos.Borrador]: 'Borrador',
  [EstadoComprobacionGastos.PorRevisar]: 'Por revisar',
  [EstadoComprobacionGastos.Autorizada]: 'Autorizada',
  [EstadoComprobacionGastos.Aplicada]: 'Aplicada',
  [EstadoComprobacionGastos.Rechazada]: 'Rechazada',
  [EstadoComprobacionGastos.AutorizadaNivel1]: 'Autorizada N1',
};

export interface ComprobacionGastosListItem {
  id: string;
  tipo: TipoComprobacionGastos;
  estado: EstadoComprobacionGastos;
  sucursalId: string;
  responsableId: string;
  fechaInicio: string;
  fechaFin: string;
  montoTotal: number;
  moneda: string;
  numeroLineas: number;
  fechaCreacion: string;
  version: number;
}

/**
 * Espejo de <c>LineaComprobacionDetalleResponse</c> (backend
 * ObtenerComprobacionGastosQuery, PR #632).
 */
export interface LineaComprobacionDetalle {
  id: string;
  facturaProveedorId: string;
  cfdiRecibidoId: string | null;
  uuidCfdi: string | null;
  proveedorId: string;
  folioProveedor: string | null;
  fechaCfdi: string;
  subtotal: number;
  impuestosTrasladados: number;
  retenciones: number;
  total: number;
  moneda: string;
  concepto: string | null;
}

/**
 * Espejo de <c>ComprobacionGastosDetalleResponse</c> (backend
 * ObtenerComprobacionGastosQuery, PR #632). Incluye las líneas (CFDIs)
 * con su factura generada, pedimento (aduanales) y trazabilidad de
 * revisión/autorización/aplicación/rechazo. Etiquetas de
 * sucursal/responsable resueltas server-side (ADR-0042).
 */
export interface ComprobacionGastosDetalle {
  id: string;
  tipo: TipoComprobacionGastos;
  sucursalId: string;
  responsableId: string;
  fechaInicio: string;
  fechaFin: string;
  moneda: string;
  montoTotal: number;
  estado: EstadoComprobacionGastos;
  numeroPedimento: string | null;
  observaciones: string | null;
  autorizadoPorNivel1: string | null;
  fechaAutorizacionNivel1: string | null;
  autorizadoPor: string | null;
  fechaAutorizacion: string | null;
  aplicadoPor: string | null;
  fechaAplicacion: string | null;
  rechazadoPor: string | null;
  fechaRechazo: string | null;
  motivoRechazo: string | null;
  fechaCreacion: string;
  fechaEnvioRevision: string | null;
  lineas: LineaComprobacionDetalle[];
  version: number;
  sucursalNombre: string | null;
  responsableNombre: string | null;
}

export interface CrearComprobacionCajaChicaLinea {
  cfdiRecibidoId: string | null;
  uuidCfdi: string | null;
  proveedorId: string;
  folioProveedor: string | null;
  serieProveedor: string | null;
  fechaCfdi: string;
  subtotal: number;
  descuentos: number;
  impuestosTrasladados: number;
  retenciones: number;
  total: number;
  fechaVencimiento: string;
  concepto: string | null;
}

/** GI-PR4 (doc 12 Q1): destino de la reposición de caja chica. */
export const DestinoReposicionCaja = {
  CuentaSucursal: 1,
  Responsable: 2,
} as const satisfies Record<string, number>;
export type DestinoReposicionCaja =
  (typeof DestinoReposicionCaja)[keyof typeof DestinoReposicionCaja];

export const DestinoReposicionCajaLabels: Record<DestinoReposicionCaja, string> = {
  [DestinoReposicionCaja.CuentaSucursal]: 'Cuenta de la sucursal',
  [DestinoReposicionCaja.Responsable]: 'Responsable de la caja',
};

export interface CrearComprobacionCajaChicaCommand {
  sucursalId: string;
  responsableId: string;
  fechaInicio: string;
  fechaFin: string;
  moneda: string;
  observaciones: string | null;
  cfdis: CrearComprobacionCajaChicaLinea[];
  destinoReposicion: DestinoReposicionCaja;
}

export interface SaldoPendienteReposicion {
  sucursalId: string;
  destino: DestinoReposicionCaja;
  moneda: string;
  saldo: number;
  numeroComprobaciones: number;
  montoMinimo: number;
}

export interface ReposicionCajaListItem {
  id: string;
  sucursalId: string;
  destino: DestinoReposicionCaja;
  beneficiarioId: string;
  moneda: string;
  montoTotal: number;
  numeroComprobaciones: number;
  esCorteManual: boolean;
  fechaEmision: string;
}

export interface ConfiguracionReposicionResponse {
  sucursalId: string;
  montoMinimo: number;
}

export interface EmitirReposicionManualResponse {
  reposicionId: string;
  montoTotal: number;
  numeroComprobaciones: number;
  moneda: string;
}

export interface CrearComprobacionCajaChicaResponse {
  id: string;
  estado: EstadoComprobacionGastos;
  montoTotal: number;
  numeroLineas: number;
  facturaIds: string[];
  version: number;
}

export interface CrearComprobacionAduanalesCommand {
  sucursalId: string;
  responsableId: string;
  proveedorId: string;
  numeroPedimento: string;
  fechaInicio: string;
  fechaFin: string;
  moneda: string;
  observaciones: string | null;
  facturaProveedorIds: string[];
}

export interface CrearComprobacionAduanalesResponse {
  id: string;
  estado: EstadoComprobacionGastos;
  montoTotal: number;
  numeroLineas: number;
  numeroPedimento: string;
  version: number;
}

export interface TransicionComprobacionResponse {
  id: string;
  estado: EstadoComprobacionGastos;
  version: number;
}

export interface RechazarComprobacionCommand {
  motivo: string;
}

// ─── Viáticos electrónicos (FE-F5-PR1) ────────────────────────────────

export const EstadoSolicitudViaticos = {
  Solicitada: 1,
  AutorizadaPorJefe: 2,
  RequiereDireccionFinanzas: 3,
  AutorizadaCompleta: 4,
  Anticipada: 5,
  ComprobacionCapturada: 6,
  Liquidada: 7,
  Rechazada: 8,
} as const satisfies Record<string, number>;
export type EstadoSolicitudViaticos =
  (typeof EstadoSolicitudViaticos)[keyof typeof EstadoSolicitudViaticos];

export const TipoDestinoViatico = {
  Nacional: 1,
  Internacional: 2,
} as const satisfies Record<string, number>;
export type TipoDestinoViatico =
  (typeof TipoDestinoViatico)[keyof typeof TipoDestinoViatico];

export const EstadoSolicitudViaticosLabels: Record<
  EstadoSolicitudViaticos,
  string
> = {
  [EstadoSolicitudViaticos.Solicitada]: 'Solicitada',
  [EstadoSolicitudViaticos.AutorizadaPorJefe]: 'Autorizada por jefe',
  [EstadoSolicitudViaticos.RequiereDireccionFinanzas]: 'Requiere DF',
  [EstadoSolicitudViaticos.AutorizadaCompleta]: 'Autorizada (DF)',
  [EstadoSolicitudViaticos.Anticipada]: 'Anticipada',
  [EstadoSolicitudViaticos.ComprobacionCapturada]: 'Comprobación capturada',
  [EstadoSolicitudViaticos.Liquidada]: 'Liquidada',
  [EstadoSolicitudViaticos.Rechazada]: 'Rechazada',
};

export const TipoDestinoViaticoLabels: Record<TipoDestinoViatico, string> = {
  [TipoDestinoViatico.Nacional]: 'Nacional',
  [TipoDestinoViatico.Internacional]: 'Internacional',
};

export interface SolicitudViaticosListItem {
  id: string;
  empleadoId: string;
  jefeDirectoId: string;
  destino: string;
  fechaSalida: string;
  fechaRegreso: string;
  montoSolicitado: number;
  topePolitica: number;
  excedePolitica: boolean;
  estado: EstadoSolicitudViaticos;
  montoComprobado: number | null;
  diferenciaLiquidacion: number | null;
  fechaSolicitud: string;
  version: number;
}

/**
 * Espejo de <c>LineaViaticosDetalleResponse</c> (backend
 * ObtenerSolicitudViaticosQuery, PR #632).
 */
export interface LineaViaticosDetalle {
  id: string;
  facturaProveedorId: string | null;
  cfdiRecibidoId: string | null;
  uuidCfdi: string | null;
  proveedorId: string | null;
  folioProveedor: string | null;
  fechaGasto: string;
  subtotal: number;
  impuestosTrasladados: number;
  retenciones: number;
  total: number;
  moneda: string;
  concepto: string;
  esTicketNoFiscal: boolean;
}

/**
 * Espejo de <c>SolicitudViaticosDetalleResponse</c> (backend
 * ObtenerSolicitudViaticosQuery, PR #632). Incluye política aplicada
 * (snapshot), trazabilidad de firmas y las líneas de la comprobación.
 * Etiquetas de empleado/jefe/puesto resueltas server-side (ADR-0042).
 */
export interface SolicitudViaticosDetalle {
  id: string;
  empleadoId: string;
  puestoId: string;
  jefeDirectoId: string;
  destino: string;
  tipoDestino: TipoDestinoViatico;
  fechaSalida: string;
  fechaRegreso: string;
  diasEstimados: number;
  moneda: string;
  montoSolicitado: number;
  topePolitica: number;
  excedePolitica: boolean;
  justificacionExceso: string | null;
  estado: EstadoSolicitudViaticos;
  autorizadoPorJefe: string | null;
  fechaAutorizacionJefe: string | null;
  autorizadoPorDf: string | null;
  fechaAutorizacionDf: string | null;
  rechazadoPor: string | null;
  fechaRechazo: string | null;
  motivoRechazo: string | null;
  fechaSolicitud: string;
  fechaAnticipoPagado: string | null;
  fechaComprobacion: string | null;
  fechaLiquidacion: string | null;
  montoComprobado: number | null;
  diferenciaLiquidacion: number | null;
  lineas: LineaViaticosDetalle[];
  version: number;
  empleadoNombre: string | null;
  jefeDirectoNombre: string | null;
  puestoNombre: string | null;
}

export interface SolicitarAnticipoViaticosCommand {
  empleadoId: string;
  puestoId: string;
  jefeDirectoId: string;
  destino: string;
  tipoDestino: TipoDestinoViatico;
  fechaSalida: string;
  fechaRegreso: string;
  moneda: string;
  montoSolicitado: number;
  justificacionExceso: string | null;
}

export interface SolicitarAnticipoViaticosResponse {
  id: string;
  estado: EstadoSolicitudViaticos;
  topePolitica: number;
  excedePolitica: boolean;
  diasEstimados: number;
  version: number;
}

export interface CapturarLineaViaticosInput {
  cfdiRecibidoId: string | null;
  uuidCfdi: string | null;
  proveedorId: string | null;
  folioProveedor: string | null;
  fechaGasto: string;
  subtotal: number;
  impuestosTrasladados: number;
  retenciones: number;
  total: number;
  moneda: string;
  concepto: string;
  esTicketNoFiscal: boolean;
}

export interface CapturarComprobacionViaticosCommand {
  lineas: CapturarLineaViaticosInput[];
}

export interface CapturarComprobacionViaticosResponse {
  id: string;
  estado: EstadoSolicitudViaticos;
  numeroLineas: number;
  montoComprobado: number;
  version: number;
}

export interface LiberarComprobacionViaticosResponse {
  id: string;
  estado: EstadoSolicitudViaticos;
  diferenciaLiquidacion: number;
  facturaIds: string[];
  version: number;
}

export interface TransicionViaticosResponse {
  id: string;
  estado: EstadoSolicitudViaticos;
  version: number;
}

export interface RechazarSolicitudViaticosCommand {
  motivo: string;
}

// ─── Tarjetas de Crédito + Movimientos (FE-F6-PR1) ────────────────────

export const EstadoTarjeta = {
  Activa: 1,
  Bloqueada: 2,
  Cancelada: 3,
} as const satisfies Record<string, number>;
export type EstadoTarjeta = (typeof EstadoTarjeta)[keyof typeof EstadoTarjeta];

export const TipoMovimientoTc = {
  CompraConCfdi: 1,
  CompraSinCfdi: 2,
  Refund: 3,
  GastoFinanciero: 4,
  Anualidad: 5,
  ComisionDivisa: 6,
} as const satisfies Record<string, number>;
export type TipoMovimientoTc =
  (typeof TipoMovimientoTc)[keyof typeof TipoMovimientoTc];

export const EstadoMovimientoTc = {
  Registrado: 1,
  ConciliadoConEstadoCuenta: 2,
  EnDisputa: 3,
  Reversado: 4,
  PagadoAlBanco: 5,
} as const satisfies Record<string, number>;
export type EstadoMovimientoTc =
  (typeof EstadoMovimientoTc)[keyof typeof EstadoMovimientoTc];

export const EstadoTarjetaLabels: Record<EstadoTarjeta, string> = {
  [EstadoTarjeta.Activa]: 'Activa',
  [EstadoTarjeta.Bloqueada]: 'Bloqueada',
  [EstadoTarjeta.Cancelada]: 'Cancelada',
};

export const TipoMovimientoTcLabels: Record<TipoMovimientoTc, string> = {
  [TipoMovimientoTc.CompraConCfdi]: 'Compra con CFDI',
  [TipoMovimientoTc.CompraSinCfdi]: 'Compra sin CFDI',
  [TipoMovimientoTc.Refund]: 'Refund',
  [TipoMovimientoTc.GastoFinanciero]: 'Gasto financiero',
  [TipoMovimientoTc.Anualidad]: 'Anualidad',
  [TipoMovimientoTc.ComisionDivisa]: 'Comisión divisa',
};

export const EstadoMovimientoTcLabels: Record<EstadoMovimientoTc, string> = {
  [EstadoMovimientoTc.Registrado]: 'Registrado',
  [EstadoMovimientoTc.ConciliadoConEstadoCuenta]: 'Conciliado',
  [EstadoMovimientoTc.EnDisputa]: 'En disputa',
  [EstadoMovimientoTc.Reversado]: 'Reversado',
  [EstadoMovimientoTc.PagadoAlBanco]: 'Pagado al banco',
};

export interface Tarjeta {
  id: string;
  emisora: string;
  perfilParser: string;
  numeroEnmascarado: string;
  nombreAlias: string;
  titularId: string;
  bancoProveedorId: string;
  limiteCreditoMxn: number;
  monedaDefault: string;
  diaCorte: number;
  diaLimitePago: number;
  estado: EstadoTarjeta;
  fechaBloqueo: string | null;
  motivoBloqueo: string | null;
  vigenciaDesde: string;
  vigenciaHasta: string | null;
  version: number;
}

export interface UsuarioAutorizado {
  id: string;
  tarjetaId: string;
  empleadoId: string;
  vigenciaDesde: string;
  vigenciaHasta: string | null;
  montoMaxMensualMxn: number | null;
}

export interface MovimientoTc {
  id: string;
  tarjetaId: string;
  usuarioQueUsoId: string;
  fechaMovimiento: string;
  tipo: TipoMovimientoTc;
  estado: EstadoMovimientoTc;
  montoOriginal: number;
  monedaOriginal: string;
  tipoCambioCaptura: number | null;
  montoMxn: number;
  merchantNormalizado: string;
  facturaProveedorId: string | null;
  conceptoContable: string;
  version: number;
}

export interface CrearTarjetaCommand {
  emisora: string;
  perfilParser: string;
  ultimosCuatro: string;
  nombreAlias: string;
  titularId: string;
  bancoProveedorId: string;
  limiteCreditoMxn: number;
  monedaDefault: string;
  diaCorte: number;
  diaLimitePago: number;
  vigenciaDesde: string;
}

export interface ActualizarTarjetaCommand {
  nombreAlias: string;
  limiteCreditoMxn: number;
  diaCorte: number;
  diaLimitePago: number;
}

export interface BloquearTarjetaCommand {
  motivo: string;
  fecha: string;
}

export interface AgregarUsuarioAutorizadoCommand {
  empleadoId: string;
  vigenciaDesde: string;
  vigenciaHasta: string | null;
  montoMaxMensualMxn: number | null;
}

export interface RegistrarMovimientoTcConCfdiCommand {
  tarjetaId: string;
  usuarioQueUsoId: string;
  fechaMovimiento: string;
  montoOriginal: number;
  monedaOriginal: string;
  tipoCambioCaptura: number | null;
  merchantRaw: string;
  descripcionLibre: string | null;
  conceptoContable: string;
  cfdiRecibidoId: string;
  proveedorId: string;
  uuidCfdi: string | null;
  folioProveedor: string | null;
  serieProveedor: string | null;
  fechaCfdi: string;
  subtotal: number;
  descuentos: number;
  impuestosTrasladados: number;
  retenciones: number;
  totalFactura: number;
  fechaVencimiento: string;
}

export interface RegistrarMovimientoTcSinCfdiCommand {
  tarjetaId: string;
  usuarioQueUsoId: string;
  fechaMovimiento: string;
  montoOriginal: number;
  monedaOriginal: string;
  tipoCambioCaptura: number | null;
  merchantRaw: string;
  descripcionLibre: string | null;
  conceptoContable: string;
  ticketBlobRef: string | null;
}

// ─── Estados de Cuenta TC + Cierre (FE-F6-PR2) ────────────────────────

export const EstadoCuentaTcStatus = {
  EnConciliacion: 1,
  Conciliado: 2,
  Cerrado: 3,
  PagadoBanco: 4,
} as const satisfies Record<string, number>;
export type EstadoCuentaTcStatus =
  (typeof EstadoCuentaTcStatus)[keyof typeof EstadoCuentaTcStatus];

export const EstadoCuentaTcStatusLabels: Record<EstadoCuentaTcStatus, string> =
  {
    [EstadoCuentaTcStatus.EnConciliacion]: 'En conciliación',
    [EstadoCuentaTcStatus.Conciliado]: 'Conciliado',
    [EstadoCuentaTcStatus.Cerrado]: 'Cerrado',
    [EstadoCuentaTcStatus.PagadoBanco]: 'Pagado al banco',
  };

export interface EstadoCuentaTc {
  id: string;
  tarjetaId: string;
  periodoDesde: string;
  periodoHasta: string;
  fechaCorte: string;
  fechaLimitePago: string;
  estado: EstadoCuentaTcStatus;
  totalBancoMxn: number | null;
  totalConciliadoMxn: number | null;
  diferenciaMxn: number | null;
  perfilParserUsado: string | null;
  lineasCount: number;
  version: number;
}

export interface CrearEstadoCuentaTcCommand {
  tarjetaId: string;
  periodoDesde: string;
  periodoHasta: string;
  fechaCorte: string;
  fechaLimitePago: string;
}

export interface SubirArchivoEstadoCuentaTcResponse {
  id: string;
  perfilParserUsado: string;
  lineasCount: number;
  totalBancoMxn: number;
  sha256: string;
  version: number;
}

export interface ConciliarAutomaticoResponse {
  id: string;
  matchesAuto: number;
  sugerencias: number;
  sinMatch: number;
  totalConciliadoMxn: number;
  diferenciaMxn: number;
  version: number;
}

export interface CerrarEstadoCuentaTcResponse {
  id: string;
  estado: EstadoCuentaTcStatus;
  facturaProveedorId: string;
  totalBancoMxn: number;
  version: number;
}

export interface ConfirmarMatchLineaBancoCommand {
  movimientoTcId: string;
  diferenciaCambiariaMxn: number | null;
}

export interface CapturarMovimientoDesdeLineaCommand {
  usuarioQueUsoId: string;
  conceptoContable: string;
}

// ─── Refunds + Movimientos Especiales + Disputas (FE-F6-PR2) ──────────

export interface RegistrarRefundTcCommand {
  tarjetaId: string;
  usuarioQueUsoId: string;
  fechaMovimiento: string;
  montoOriginal: number;
  monedaOriginal: string;
  tipoCambioCaptura: number | null;
  merchantRaw: string;
  conceptoContable: string;
  movimientoOriginalId: string;
}

export interface MovimientoRefundResponse {
  id: string;
  movimientoOriginalId: string;
  montoMxn: number;
  version: number;
}

export interface RegistrarMovimientoEspecialTcCommand {
  tarjetaId: string;
  usuarioQueUsoId: string;
  fechaMovimiento: string;
  tipo: TipoMovimientoTc;
  montoOriginal: number;
  monedaOriginal: string;
  tipoCambioCaptura: number | null;
  merchantRaw: string;
  conceptoContable: string;
}

export interface MovimientoEspecialResponse {
  id: string;
  tipo: TipoMovimientoTc;
  montoMxn: number;
  version: number;
}

export interface DisputarMovimientoTcCommand {
  motivo: string;
  fechaInicio: string;
}

export interface ResolverDisputaMovimientoTcCommand {
  fueLegitimo: boolean;
}

// ─── Admin: catálogos CxP (cxp-fe/admin-pages) ────────────────────────

export const TipoGastoAprobador = {
  ReembolsoCajaChica: 1,
  Viaticos: 2,
  TarjetaCreditoEmpresarial: 3,
  OtrosSinOc: 4,
} as const satisfies Record<string, number>;
export type TipoGastoAprobador =
  (typeof TipoGastoAprobador)[keyof typeof TipoGastoAprobador];

export const TipoGastoAprobadorLabels: Record<TipoGastoAprobador, string> = {
  [TipoGastoAprobador.ReembolsoCajaChica]: 'Caja chica',
  [TipoGastoAprobador.Viaticos]: 'Viáticos',
  [TipoGastoAprobador.TarjetaCreditoEmpresarial]: 'TC empresarial',
  [TipoGastoAprobador.OtrosSinOc]: 'Otros sin OC',
};

export interface AprobadorLimite {
  id: string;
  empleadoId: string;
  tipoGasto: TipoGastoAprobador;
  montoMax: number;
  moneda: string;
  vigenciaDesde: string;
  vigenciaHasta: string | null;
  version: number;
}

export interface CrearAprobadorLimiteCommand {
  empleadoId: string;
  tipoGasto: TipoGastoAprobador;
  montoMax: number;
  moneda: string;
  vigenciaDesde: string;
  vigenciaHasta: string | null;
}

export interface ActualizarAprobadorLimiteCommand {
  montoMax: number;
  moneda: string;
}

export interface PoliticaViaticos {
  id: string;
  puestoId: string;
  tipoDestino: TipoDestinoViatico;
  montoMaxDia: number;
  diasMax: number;
  moneda: string;
  version: number;
}

export interface CrearPoliticaViaticosCommand {
  puestoId: string;
  tipoDestino: TipoDestinoViatico;
  montoMaxDia: number;
  diasMax: number;
  moneda: string;
}

export interface ActualizarPoliticaViaticosCommand {
  montoMaxDia: number;
  diasMax: number;
  moneda: string;
}
