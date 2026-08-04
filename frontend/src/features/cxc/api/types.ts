/**
 * Tipos compartidos del módulo Cuentas por Cobrar frontend (CXC-FE-PR1+).
 * Mirror manual de los DTOs / enums que devuelve el backend en
 * <c>/api/v1/cuentas-por-cobrar/...</c>. Cuando ADR-0017 (codegen TS
 * desde OpenAPI) entre, este archivo se vuelve autogenerado.
 *
 * <para>Convención: los enums se modelan como <c>const</c> + <c>type</c>
 * union para cumplir <c>erasableSyntaxOnly</c> (mismo patrón que
 * <c>facturacion/api/types.ts</c>). Los valores numéricos espejan el
 * <c>short</c> del backend — están fijos por ABI, agregar al final,
 * nunca renumerar (incidente 2026-07-11: al agregar un valor va
 * constraint + migration + mirror FE en el mismo PR).</para>
 *
 * <para>CXC-FE-PR1 solo declara el andamio fundacional (paginado +
 * líneas de crédito + crédito disponible). Los DTOs de liberaciones,
 * cobranza, cartera, aplicaciones y alertas llegan con la fase FE que
 * los consume.</para>
 */

// ─── Paged response (compartido con backend Common.PagedResponse) ─────

export interface PagedResponse<T> {
  items: T[];
  offset: number;
  limit: number;
  total: number;
}

// ─── Línea de crédito (Domain.LineaCredito, CXC-PR1) ──────────────────

/**
 * Estado de una línea de crédito. Mirror de backend
 * <c>EstadoLineaCredito</c> (short + check constraint).
 */
export const EstadoLineaCredito = {
  Activa: 1,
  Bloqueada: 2,
  /** Suspensión administrativa (p.ej. baja del seguro SOLUNION). */
  Suspendida: 3,
} as const satisfies Record<string, number>;
export type EstadoLineaCredito =
  (typeof EstadoLineaCredito)[keyof typeof EstadoLineaCredito];

/**
 * Origen del límite de crédito: asegurado por SOLUNION o asignado
 * internamente por Millet. Mirror de backend <c>OrigenLineaCredito</c>.
 */
export const OrigenLineaCredito = {
  Solunion: 1,
  Interno: 2,
} as const satisfies Record<string, number>;
export type OrigenLineaCredito =
  (typeof OrigenLineaCredito)[keyof typeof OrigenLineaCredito];

/**
 * Clasificación del cliente por tipo de crédito (levantamiento §1.2).
 * En backend es <c>string?</c> validado contra este set (A/B/C/E), no
 * enum numérico — acá el union de literales cumple el mismo rol.
 */
export const CLASIFICACIONES_CREDITO = ['A', 'B', 'C', 'E'] as const;
export type ClasificacionCredito = (typeof CLASIFICACIONES_CREDITO)[number];

/** Monedas válidas de una línea de crédito (multi-moneda sin conversión). */
export const MONEDAS_LINEA_CREDITO = ['MXN', 'USD'] as const;
export type MonedaLineaCredito = (typeof MONEDAS_LINEA_CREDITO)[number];

/** Mirror de backend <c>LineaCreditoResponse</c> (CXC-PR1). */
export interface LineaCreditoResponse {
  id: string;
  clienteId: string;
  moneda: string;
  limite: number;
  origen: OrigenLineaCredito;
  plazoDias: number;
  clasificacion: string | null;
  estado: EstadoLineaCredito;
  motivoBloqueo: string | null;
  /** Versión de concurrencia optimista — va en <c>X-Expected-Version</c>. */
  version: number;
}

/** Payload de creación (mirror de <c>CrearLineaCreditoCommand</c>). */
export interface CrearLineaCreditoCommand {
  clienteId: string;
  moneda: string;
  limite: number;
  origen: OrigenLineaCredito;
  plazoDias: number;
  clasificacion: string | null;
}

/** Body del PUT (mirror de <c>ActualizarLineaCreditoBody</c>). */
export interface ActualizarLineaCreditoBody {
  limite: number;
  plazoDias: number;
  clasificacion: string | null;
}

// ─── Liberación de pedidos (Domain.Liberacion, CXC-PR4) ───────────────

/**
 * Resultado de una decisión de liberación. Mirror de backend
 * <c>ResultadoLiberacion</c> (short + check constraint).
 */
export const ResultadoLiberacion = {
  Liberado: 1,
  Retenido: 2,
  LiberadoConOverride: 3,
} as const satisfies Record<string, number>;
export type ResultadoLiberacion =
  (typeof ResultadoLiberacion)[keyof typeof ResultadoLiberacion];

/**
 * Regla que determinó el resultado (cascada serie → crédito → override).
 * Mirror de backend <c>ReglaAplicadaLiberacion</c>.
 */
export const ReglaAplicadaLiberacion = {
  Serie: 1,
  Credito: 2,
  Override: 3,
} as const satisfies Record<string, number>;
export type ReglaAplicadaLiberacion =
  (typeof ReglaAplicadaLiberacion)[keyof typeof ReglaAplicadaLiberacion];

/**
 * Estado de una autorización de crédito consumible (calco de
 * AutorizacionAperturaCaja, un solo uso, vigencia ≤ 24 h). Mirror de
 * backend <c>EstadoAutorizacionCredito</c>.
 */
export const EstadoAutorizacionCredito = {
  /** Vigente; el beneficiario puede consumirla al decidir. */
  Autorizada: 1,
  /** Consumida por una decisión de liberación (un solo uso). */
  Usada: 2,
  /** Cancelada por el supervisor antes de usarse. */
  Cancelada: 3,
} as const satisfies Record<string, number>;
export type EstadoAutorizacionCredito =
  (typeof EstadoAutorizacionCredito)[keyof typeof EstadoAutorizacionCredito];

/** Mirror de backend <c>DecisionLiberacionResponse</c> (inmutable). */
export interface DecisionLiberacionResponse {
  id: string;
  pedidoRef: string;
  clienteId: string;
  moneda: string;
  montoPedido: number;
  creditoDisponibleSnapshot: number;
  resultado: ResultadoLiberacion;
  reglaAplicada: ReglaAplicadaLiberacion;
  overrideId: string | null;
  decididoPor: string;
  decididoEn: string;
}

/** Payload del POST decidir (mirror de <c>DecidirLiberacionCommand</c>). */
export interface DecidirLiberacionCommand {
  pedidoRef: string;
  clienteId: string;
  moneda: string;
  montoPedido: number;
  overrideId: string | null;
}

/** Mirror de backend <c>AutorizacionCreditoResponse</c>. */
export interface AutorizacionCreditoResponse {
  id: string;
  supervisorUsuarioId: string;
  beneficiarioUsuarioId: string;
  motivo: string;
  clienteOPedidoRef: string;
  fechaAutorizacion: string;
  vigenteHasta: string;
  estado: EstadoAutorizacionCredito;
  decisionLiberacionId: string | null;
  version: number;
}

/** Payload de creación (mirror de <c>CrearAutorizacionCreditoCommand</c>). */
export interface CrearAutorizacionCreditoCommand {
  beneficiarioUsuarioId: string;
  motivo: string;
  clienteOPedidoRef: string;
  /** 1..24 (default backend 24). */
  vigenciaHoras: number;
}

// ─── Cobranza (Domain.Cobranza, CXC-PR5) ──────────────────────────────

/**
 * Canal de la gestión de cobranza. Mirror de backend
 * <c>CanalCobranza</c> (short + check constraint).
 */
export const CanalCobranza = {
  Llamada: 1,
  Correo: 2,
  Whatsapp: 3,
} as const satisfies Record<string, number>;
export type CanalCobranza = (typeof CanalCobranza)[keyof typeof CanalCobranza];

/**
 * Resultado de la gestión. Mirror de backend <c>ResultadoCobranza</c>.
 * <c>PromesaPago</c> exige monto y fecha comprometidos (§4.2).
 */
export const ResultadoCobranza = {
  PromesaPago: 1,
  SinRespuesta: 2,
  Excusa: 3,
  Otro: 4,
} as const satisfies Record<string, number>;
export type ResultadoCobranza =
  (typeof ResultadoCobranza)[keyof typeof ResultadoCobranza];

/** Mirror de backend <c>SeguimientoCobranzaResponse</c> (append-only). */
export interface SeguimientoCobranzaResponse {
  id: string;
  clienteId: string;
  fecha: string;
  usuarioId: string;
  canal: CanalCobranza;
  resultado: ResultadoCobranza;
  montoComprometido: number | null;
  /** DateOnly del backend — serializa como "yyyy-MM-dd". */
  fechaComprometida: string | null;
  nota: string;
}

/** Payload del POST (mirror de <c>RegistrarSeguimientoCobranzaCommand</c>). */
export interface RegistrarSeguimientoCobranzaCommand {
  clienteId: string;
  canal: CanalCobranza;
  resultado: ResultadoCobranza;
  montoComprometido: number | null;
  fechaComprometida: string | null;
  nota: string;
}

// ─── Reportes de cartera (Application.Reportes, CXC-PR3/PR6) ──────────

/**
 * Tipo de columna del reporte CxC. Mirror de backend
 * <c>TipoColumnaReporte</c> — OJO: CxC emite su propio shape de reporte
 * (no el canónico del <c>&lt;ReporteShell&gt;</c>); el FE lo adapta con
 * <c>adaptarReporteCxc</c>.
 */
export const TipoColumnaReporteCxc = {
  Texto: 1,
  Entero: 2,
  Numerico: 3,
  Moneda: 4,
  Fecha: 5,
  FechaHora: 6,
  Booleano: 7,
  Enum: 8,
} as const satisfies Record<string, number>;
export type TipoColumnaReporteCxc =
  (typeof TipoColumnaReporteCxc)[keyof typeof TipoColumnaReporteCxc];

/** Mirror de backend <c>AlineacionColumna</c>. */
export const AlineacionColumnaCxc = {
  Izquierda: 1,
  Centro: 2,
  Derecha: 3,
} as const satisfies Record<string, number>;
export type AlineacionColumnaCxc =
  (typeof AlineacionColumnaCxc)[keyof typeof AlineacionColumnaCxc];

/** Mirror de backend <c>ReporteJsonResponse</c> (shape propio de CxC). */
export interface ReporteCxcBackend {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Array<{ label: string; valor: string }>;
  columnas: Array<{
    key: string;
    label: string;
    tipo: TipoColumnaReporteCxc;
    alineacion: AlineacionColumnaCxc;
  }>;
  filas: Array<Record<string, unknown>>;
  totales: Record<string, unknown> | null;
}

/**
 * Saldo de anticipo por cliente vía read port de Facturación (§6.1 del
 * 01-diseño; reemplazo del "mapa" A+W). Mirror de backend
 * <c>AnticipoSaldoClienteDto</c> — <c>estado</c> viaja como string
 * ("Abierto"/"Amortizado"/"Cancelado", lo formatea Facturación).
 */
export interface AnticipoSaldoClienteItem {
  anticipoId: string;
  clienteId: string;
  estado: string;
  montoCobrado: number;
  montoAmortizado: number;
  saldo: number;
  saldoDisponible: number;
  moneda: string;
  pedidoOrigenRef: string | null;
}

// ─── Aplicación de pagos (Domain.AplicacionPagos, CXC-PR7) ────────────

/**
 * Estado de una propuesta de aplicación. Mirror de backend
 * <c>EstadoPropuestaAplicacion</c> (short + check constraint).
 */
export const EstadoPropuestaAplicacion = {
  Propuesta: 1,
  Confirmada: 2,
  Rechazada: 3,
} as const satisfies Record<string, number>;
export type EstadoPropuestaAplicacion =
  (typeof EstadoPropuestaAplicacion)[keyof typeof EstadoPropuestaAplicacion];

/** Mirror de backend <c>PropuestaFacturaResponse</c>. */
export interface PropuestaFacturaResponse {
  facturaCarteraId: string;
  facturaUuid: string;
  /** Folio de la factura en cartera — la UI lo muestra en vez del UUID. */
  folio: string | null;
  importeAplicado: number;
  numParcialidad: number | null;
}

/** Mirror de backend <c>PropuestaAplicacionResponse</c>. */
export interface PropuestaAplicacionResponse {
  id: string;
  clienteId: string;
  depositoRef: string;
  montoDeposito: number;
  moneda: string;
  remittanceRef: string;
  /** Diferencia depósito − Σ importes; solo ≤ 0 y dentro de tolerancia. */
  ajusteNoFiscal: number;
  estado: EstadoPropuestaAplicacion;
  motivoRechazo: string | null;
  resueltaPor: string | null;
  resueltaEn: string | null;
  facturas: PropuestaFacturaResponse[];
  version: number;
}

/** Línea del payload de creación (mirror de <c>PropuestaFacturaLinea</c>). */
export interface PropuestaFacturaLinea {
  facturaUuid: string;
  importeAplicado: number;
  numParcialidad: number | null;
}

/** Payload del POST (mirror de <c>CrearPropuestaAplicacionCommand</c>). */
export interface CrearPropuestaAplicacionCommand {
  clienteId: string;
  depositoRef: string;
  montoDeposito: number;
  moneda: string;
  remittanceRef: string;
  facturas: PropuestaFacturaLinea[];
}

/**
 * Factura viva del cliente para el matching (CXC-FE-PR6). Mirror de
 * backend <c>FacturaAbiertaDto</c>; <c>saldo = total − pagado − NC</c>.
 */
export interface FacturaAbiertaItem {
  facturaCarteraId: string;
  uuid: string;
  folio: string;
  moneda: string;
  metodoPago: string;
  total: number;
  montoPagado: number;
  montoNc: number;
  saldo: number;
  fechaTimbrado: string;
  fechaVencimiento: string;
}

// ─── Alertas de cartera (Domain.Alertas, CXC-PR8) ─────────────────────

/**
 * Tipo de alerta de cartera. Mirror de backend <c>TipoAlertaCartera</c>
 * (short + check constraint).
 */
export const TipoAlertaCartera = {
  /** Factura de asegurado SOLUNION vencida ≥ N días (default 90). */
  Solunion90d: 1,
  /** Saldo del cliente excede el límite de su línea. */
  ExcesoCredito: 2,
  /** Vencimientos dispararon el auto-bloqueo de la línea. */
  AutoBloqueoVencimiento: 3,
} as const satisfies Record<string, number>;
export type TipoAlertaCartera =
  (typeof TipoAlertaCartera)[keyof typeof TipoAlertaCartera];

/** Mirror de backend <c>AlertaCarteraResponse</c>. */
export interface AlertaCarteraResponse {
  id: string;
  clienteId: string;
  tipo: TipoAlertaCartera;
  moneda: string;
  detalle: string;
  disparadaEn: string;
  atendida: boolean;
  atendidaPor: string | null;
  atendidaEn: string | null;
  version: number;
}

// ─── Lookup de clientes CxC (CXC-FE-PR2) ──────────────────────────────

/**
 * Mirror de backend <c>ClienteLookupCxcDto</c> —
 * <c>GET /api/v1/cuentas-por-cobrar/clientes-lookup</c> (gate
 * <c>lineas-credito.leer</c>). Item mínimo para combobox ADR-0045 y
 * resolución de nombres en bandejas.
 */
export interface ClienteLookupCxcItem {
  id: string;
  clave: string;
  rfc: string | null;
  razonSocial: string;
}

// ─── Crédito disponible (CXC-PR2) ─────────────────────────────────────

/**
 * Renglón por línea (= por moneda, nunca se convierte) del crédito
 * disponible. Mirror de backend <c>CreditoDisponibleLineaResponse</c>:
 * <c>disponible = limite − facturado − liberadoSinFactura</c>.
 */
export interface CreditoDisponibleLineaResponse {
  lineaCreditoId: string;
  moneda: string;
  limite: number;
  facturado: number;
  /** Provisional (= 0) hasta cerrar el gap G1 con A+W. */
  liberadoSinFactura: number;
  disponible: number;
  estado: EstadoLineaCredito;
  origen: OrigenLineaCredito;
  plazoDias: number;
}

/**
 * Mirror de backend <c>CreditoDisponibleResponse</c> (CXC-PR2/PR3). El
 * campo <c>datoIncompleto</c> indica que el término "material liberado
 * A+W" (gap G1) aún no entra a la fórmula — la UI muestra el número con
 * badge ámbar, nunca lo oculta (05-frontend-diseno §4.2).
 */
export interface CreditoDisponibleResponse {
  clienteId: string;
  lineas: CreditoDisponibleLineaResponse[];
  datoIncompleto: boolean;
}
