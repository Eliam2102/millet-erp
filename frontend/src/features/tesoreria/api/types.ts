/**
 * Mirrors manuales de DTOs / enums del backend Millet.Tesoreria
 * (TES-FE-PR1). Los enums son objetos `as const` numéricos — mirror de
 * los valores que el backend serializa como número y que respaldan los
 * check constraints del esquema `tesoreria` (01-diseno §5.2; regla del
 * repo: nuevo valor = constraint + migration + mirror FE).
 */

// ---------------------------------------------------------------- enums

/** Mirror de `SentidoMovimiento` (ck_movimiento_bancario_sentido). */
export const SentidoMovimiento = {
  Ingreso: 1,
  Egreso: 2,
} as const;
export type SentidoMovimiento =
  (typeof SentidoMovimiento)[keyof typeof SentidoMovimiento];

export const SENTIDO_MOVIMIENTO_LABELS: Record<SentidoMovimiento, string> = {
  [SentidoMovimiento.Ingreso]: 'Ingreso',
  [SentidoMovimiento.Egreso]: 'Egreso',
};

/** Mirror de `EstadoAplicacionMovimiento` (ck_movimiento_bancario_estado_aplicacion). */
export const EstadoAplicacionMovimiento = {
  NoAplicado: 1,
  AplicadoParcial: 2,
  Aplicado: 3,
} as const;
export type EstadoAplicacionMovimiento =
  (typeof EstadoAplicacionMovimiento)[keyof typeof EstadoAplicacionMovimiento];

export const ESTADO_APLICACION_LABELS: Record<
  EstadoAplicacionMovimiento,
  string
> = {
  [EstadoAplicacionMovimiento.NoAplicado]: 'No aplicado',
  [EstadoAplicacionMovimiento.AplicadoParcial]: 'Aplicado parcial',
  [EstadoAplicacionMovimiento.Aplicado]: 'Aplicado',
};

/** Mirror de `EstadoConciliacionMovimiento` (ck_movimiento_bancario_estado_conciliacion). */
export const EstadoConciliacionMovimiento = {
  NoConciliado: 1,
  Conciliado: 2,
} as const;
export type EstadoConciliacionMovimiento =
  (typeof EstadoConciliacionMovimiento)[keyof typeof EstadoConciliacionMovimiento];

export const ESTADO_CONCILIACION_LABELS: Record<
  EstadoConciliacionMovimiento,
  string
> = {
  [EstadoConciliacionMovimiento.NoConciliado]: 'No conciliado',
  [EstadoConciliacionMovimiento.Conciliado]: 'Conciliado',
};

/** Mirror de `BeneficiarioTipo` (ck_movimiento_bancario_beneficiario_tipo). */
export const BeneficiarioTipo = {
  Proveedor: 1,
  Cliente: 2,
  Otro: 3,
} as const;
export type BeneficiarioTipo =
  (typeof BeneficiarioTipo)[keyof typeof BeneficiarioTipo];

export const BENEFICIARIO_TIPO_LABELS: Record<BeneficiarioTipo, string> = {
  [BeneficiarioTipo.Proveedor]: 'Proveedor',
  [BeneficiarioTipo.Cliente]: 'Cliente',
  [BeneficiarioTipo.Otro]: 'Otro',
};

/** Mirror de `EstadoCorridaPago` (ck_corrida_pago_estado; máquina RN-5). */
export const EstadoCorridaPago = {
  Borrador: 1,
  EnAutorizacion: 2,
  Autorizada: 3,
  Ejecutada: 4,
  Cerrada: 5,
  Rechazada: 6,
  Cancelada: 7,
} as const;
export type EstadoCorridaPago =
  (typeof EstadoCorridaPago)[keyof typeof EstadoCorridaPago];

export const ESTADO_CORRIDA_LABELS: Record<EstadoCorridaPago, string> = {
  [EstadoCorridaPago.Borrador]: 'Borrador',
  [EstadoCorridaPago.EnAutorizacion]: 'En autorización',
  [EstadoCorridaPago.Autorizada]: 'Autorizada',
  [EstadoCorridaPago.Ejecutada]: 'Ejecutada',
  [EstadoCorridaPago.Cerrada]: 'Cerrada',
  [EstadoCorridaPago.Rechazada]: 'Rechazada',
  [EstadoCorridaPago.Cancelada]: 'Cancelada',
};

/** Mirror de `EstadoDepositoConfirmacion` (ck_deposito_confirmacion_estado). */
export const EstadoDepositoConfirmacion = {
  Pendiente: 1,
  Confirmada: 2,
  Rechazada: 3,
} as const;
export type EstadoDepositoConfirmacion =
  (typeof EstadoDepositoConfirmacion)[keyof typeof EstadoDepositoConfirmacion];

export const ESTADO_DEPOSITO_LABELS: Record<
  EstadoDepositoConfirmacion,
  string
> = {
  [EstadoDepositoConfirmacion.Pendiente]: 'Pendiente',
  [EstadoDepositoConfirmacion.Confirmada]: 'Confirmada',
  [EstadoDepositoConfirmacion.Rechazada]: 'Rechazada',
};

/** Mirror de `ClasificacionFlujo` (ck_concepto_movimiento_clasificacion). */
export const ClasificacionFlujo = {
  Operacion: 1,
  Inversion: 2,
  Financiamiento: 3,
} as const;
export type ClasificacionFlujo =
  (typeof ClasificacionFlujo)[keyof typeof ClasificacionFlujo];

export const CLASIFICACION_FLUJO_LABELS: Record<ClasificacionFlujo, string> = {
  [ClasificacionFlujo.Operacion]: 'Operación',
  [ClasificacionFlujo.Inversion]: 'Inversión',
  [ClasificacionFlujo.Financiamiento]: 'Financiamiento',
};

// ---------------------------------------------------------------- DTOs (TES-FE-PR2)

/** Réplica local del shape paginado del módulo (mismo criterio que CxC). */
export interface PagedResponse<T> {
  items: T[];
  offset: number;
  limit: number;
  total: number;
}

/** Mirror de `CuentaSaldoResponse` (GET /tesoreria/cuentas). Número/CLABE llegan enmascarados salvo ver-cuenta-completa. */
export interface CuentaSaldoResponse {
  id: string;
  banco: string;
  numeroCuenta: string;
  clabe: string | null;
  moneda: string;
  cuentaContableRef: string | null;
  perfilExtracto: string | null;
  activa: boolean;
  saldo: number;
  version: number;
}

/** Mirror de `MovimientoBancarioResponse`. */
export interface MovimientoBancarioResponse {
  id: string;
  cuentaBancariaId: string;
  sentido: SentidoMovimiento;
  monto: number;
  moneda: string;
  fechaValor: string;
  referenciaBancaria: string | null;
  conceptoId: string | null;
  conceptoNombre: string | null;
  estadoAplicacion: EstadoAplicacionMovimiento;
  estadoConciliacion: EstadoConciliacionMovimiento;
  beneficiarioTipo: BeneficiarioTipo | null;
  beneficiarioRef: string | null;
  contramovimientoDe: string | null;
  motivoNoAplicado: string | null;
  creadoPor: string;
  creadoEn: string;
  version: number;
}

/** Mirror de `AplicacionMovimientoDto` — `pagoId` correlaciona con CxP y alimenta la reversa. */
export interface AplicacionMovimientoDto {
  pagoId: string;
  facturaProveedorId: string;
  proveedorId: string;
  importeAplicado: number;
  revertida: boolean;
  corridaId: string | null;
  creadoEn: string;
}

/** Mirror de `MovimientoDetalleResponse` (GET /tesoreria/movimientos/{id}). */
export interface MovimientoDetalleResponse {
  movimiento: MovimientoBancarioResponse;
  aplicaciones: AplicacionMovimientoDto[];
  contramovimientos: MovimientoBancarioResponse[];
}

/** Mirror de `PasivoPendienteResponse` (GET /tesoreria/pasivos-pendientes). */
export interface PasivoPendienteResponse {
  facturaProveedorId: string;
  proveedorId: string;
  proveedorClave: string | null;
  proveedorRazonSocial: string | null;
  banco: string | null;
  clabe: string | null;
  beneficiario: string | null;
  ordenCompraId: string | null;
  montoTotal: number;
  saldoPendiente: number;
  moneda: string;
  tipoCambio: number | null;
  fechaVencimiento: string;
  uuidCfdi: string | null;
  folioProveedor: string | null;
  metodoPago: string | null;
  recibidoEn: string;
  pagoACuentaAbiertoMovimientoId: string | null;
}

/** Item del command RegistrarPagoProveedor. */
export interface AplicacionPagoItem {
  facturaProveedorId: string;
  importe: number;
}

/** Mirror de `AplicacionPagoResponse`. */
export interface AplicacionPagoResponse {
  pagoId: string;
  facturaProveedorId: string;
  importe: number;
  revertida: boolean;
}

/** Mirror de `PagoProveedorResponse` (POST /tesoreria/pagos y reversa). */
export interface PagoProveedorResponse {
  movimientoId: string;
  cuentaBancariaId: string;
  proveedorId: string;
  monto: number;
  moneda: string;
  fechaValor: string;
  referenciaBancaria: string | null;
  aplicaciones: AplicacionPagoResponse[];
}

/** Mirror de `PagoACuentaResponse` (POST /tesoreria/pagos-cuenta). */
export interface PagoACuentaResponse {
  movimientoId: string;
  cuentaBancariaId: string;
  proveedorId: string | null;
  monto: number;
  moneda: string;
  fechaValor: string;
  referenciaBancaria: string | null;
  motivo: string;
  estadoAplicacion: EstadoAplicacionMovimiento;
}

// ---------------------------------------------------------------- reportes (TES-FE-PR6)

/** Mirror de `TipoColumnaReporte` del ReporteJsonResponse de Tesorería (ADR-0036). */
export const TipoColumnaReporteTesoreria = {
  Texto: 1,
  Entero: 2,
  Numerico: 3,
  Moneda: 4,
  Fecha: 5,
  FechaHora: 6,
  Booleano: 7,
  Enum: 8,
} as const;
export type TipoColumnaReporteTesoreria =
  (typeof TipoColumnaReporteTesoreria)[keyof typeof TipoColumnaReporteTesoreria];

/** Mirror de `ReporteJsonResponse` (GET /tesoreria/reportes/*). */
export interface ReporteTesoreriaBackend {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Array<{ label: string; valor: string }>;
  columnas: Array<{
    key: string;
    label: string;
    tipo: TipoColumnaReporteTesoreria;
    alineacion: number;
  }>;
  filas: Array<Record<string, unknown>>;
  totales: Record<string, unknown> | null;
}

/** Mirror de `PagoACuentaAbiertoResponse` (GET /tesoreria/pagos-cuenta). */
export interface PagoACuentaAbiertoResponse {
  movimientoId: string;
  cuentaBancariaId: string;
  proveedorId: string | null;
  proveedorClave: string | null;
  proveedorRazonSocial: string | null;
  monto: number;
  importeLigado: number;
  moneda: string;
  fechaValor: string;
  referenciaBancaria: string | null;
  motivo: string;
  estadoAplicacion: EstadoAplicacionMovimiento;
  antiguedadDias: number;
}

// ---------------------------------------------------------------- Depósitos (TES-FE-PR4b)

/** Factura de la propuesta CxC cubierta por el depósito. */
export interface DepositoFacturaResponse {
  facturaVentaId: string;
  folio: string | null;
  importeAplicado: number;
}

/** Mirror de `DepositoConfirmacionResponse` (GET /tesoreria/depositos, TES-PR7). */
export interface DepositoConfirmacionResponse {
  id: string;
  propuestaCxcId: string | null;
  cajaSesionId: string | null;
  clienteId: string | null;
  clienteClave: string | null;
  clienteRazonSocial: string | null;
  depositoRef: string | null;
  montoEsperado: number | null;
  moneda: string | null;
  estado: EstadoDepositoConfirmacion;
  motivoRechazo: string | null;
  reppTimbrado: boolean;
  movimientoId: string | null;
  facturas: DepositoFacturaResponse[];
  resueltaPor: string | null;
  resueltaEn: string | null;
  recibidoEn: string;
  version: number;
}

// ---------------------------------------------------------------- REPP recibido (TES-FE-PR6b)

/** Mirror de `ReppPendienteResponse` (GET /tesoreria/repp-pendientes, TES-PR8). */
export interface ReppPendienteResponse {
  facturaProveedorId: string;
  proveedorId: string;
  proveedorClave: string | null;
  proveedorRazonSocial: string | null;
  folioProveedor: string | null;
  uuidCfdi: string | null;
  metodoPago: string | null;
  montoPagado: number;
  moneda: string;
  fechaPrimerPago: string;
  diasSinRepp: number;
  vencidoSla: boolean;
}

/** Mirror de `ReppRecibidoResponse` (POST /tesoreria/repp-recibidos). */
export interface ReppRecibidoResponse {
  id: string;
  facturaProveedorId: string;
  uuidComplemento: string;
  fechaComplemento: string;
  xmlBlobRef: string | null;
  registradoEn: string;
}
