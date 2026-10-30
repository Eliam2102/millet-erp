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
  /** Corte de caja con fichas que aún no suman el efectivo. */
  Parcial: 4,
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
  [EstadoDepositoConfirmacion.Parcial]: 'Parcial',
};

/** Mirror de `SoporteFicha`: documento que respalda la 2ª ficha en adelante de un corte. */
export const SoporteFicha = { ValeCaja: 1, CorteParcial: 2 } as const;
export type SoporteFicha = (typeof SoporteFicha)[keyof typeof SoporteFicha];
export const SOPORTE_FICHA_LABELS: Record<SoporteFicha, string> = {
  [SoporteFicha.ValeCaja]: 'Vale de caja',
  [SoporteFicha.CorteParcial]: 'Corte parcial',
};

/** Mirror de `DepositoFichaResponse`. */
export interface DepositoFichaResponse {
  id: string;
  movimientoId: string;
  importe: number;
  fechaFicha: string;
  numeroFicha: string | null;
  soporteTipo: SoporteFicha | null;
  soporteFolio: string | null;
  confirmadaEn: string;
}

/** Mirror de `EstadoDevolucionCliente` (ck_devolucion_cliente_estado). */
export const EstadoDevolucionCliente = {
  Solicitada: 1,
  Autorizada: 2,
  Rechazada: 3,
  Pagada: 4,
  Cancelada: 5,
} as const;
export type EstadoDevolucionCliente =
  (typeof EstadoDevolucionCliente)[keyof typeof EstadoDevolucionCliente];

export const ESTADO_DEVOLUCION_LABELS: Record<EstadoDevolucionCliente, string> = {
  [EstadoDevolucionCliente.Solicitada]: 'Solicitada',
  [EstadoDevolucionCliente.Autorizada]: 'Autorizada',
  [EstadoDevolucionCliente.Rechazada]: 'Rechazada',
  [EstadoDevolucionCliente.Pagada]: 'Pagada',
  [EstadoDevolucionCliente.Cancelada]: 'Cancelada',
};

/** Mirror de `OrigenDevolucionCliente`. */
export const OrigenDevolucionCliente = {
  DepositoErroneo: 1,
  NotaCredito: 2,
} as const;
export type OrigenDevolucionCliente =
  (typeof OrigenDevolucionCliente)[keyof typeof OrigenDevolucionCliente];

export const ORIGEN_DEVOLUCION_LABELS: Record<OrigenDevolucionCliente, string> = {
  [OrigenDevolucionCliente.DepositoErroneo]: 'Depósito erróneo',
  [OrigenDevolucionCliente.NotaCredito]: 'Nota de crédito',
};

/** Espejo de `DevolucionClienteResponse` (T9.6). */
export interface DevolucionClienteResponse {
  id: string;
  /** Folio propio «DEV-T-0001» (secuencia por empresa). */
  folio: string;
  /** Último editor: quien edita la solicitud tampoco la autoriza ni la rechaza. */
  editadaPor: string | null;
  origen: OrigenDevolucionCliente;
  estado: EstadoDevolucionCliente;
  clienteId: string;
  clienteClave: string | null;
  clienteRazonSocial: string | null;
  importe: number;
  importeMaximoOrigen: number;
  /** Máximo del origen menos lo comprometido en OTRAS devoluciones (excluye esta). */
  disponibleOrigen: number;
  moneda: string;
  motivo: string;
  movimientoOrigenId: string | null;
  notaCreditoId: string | null;
  notaCreditoFolio: string | null;
  notaCreditoUuid: string | null;
  destinoBanco: string | null;
  destinoClabe: string | null;
  destinoTitular: string | null;
  solicitadaPor: string | null;
  solicitadaEn: string;
  autorizadaPor: string | null;
  autorizadaEn: string | null;
  motivoRechazo: string | null;
  motivoCancelacion: string | null;
  movimientoPagoId: string | null;
  pagadaEn: string | null;
  sucursalId: string | null;
  version: number;
}

export interface ClienteRefDto {
  id: string;
  clave: string;
  razonSocial: string;
}

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
  sucursal?: string | null;
  finalidad?: string | null;
  titular?: string | null;
  firmantes?: string | null;
  saldoInicial?: number | null;
  fechaCorteSaldoInicial?: string | null;
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
  /** Razón social del beneficiario (cliente o proveedor), resuelta en el servidor. */
  beneficiarioNombre?: string | null;
  beneficiarioClave?: string | null;
  contramovimientoDe: string | null;
  motivoNoAplicado: string | null;
  motivoReversa?: string | null;
  motivoReclasificacion?: string | null;
  /** Cuenta destino del pago a proveedor (número enmascarado salvo permiso PII). */
  destinoBanco?: string | null;
  destinoNumeroCuenta?: string | null;
  destinoTitular?: string | null;
  fondosVerificados?: boolean | null;
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
  motivoReversa?: string | null;
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
  /** 86e3tactf: bloqueo total; el pago se rechaza con PROVEEDOR_BLOQUEADO. */
  proveedorBloqueado?: boolean;
  proveedorMotivoBloqueo?: string | null;
  banco: string | null;
  clabe: string | null;
  beneficiario: string | null;
  /** «Proveedor» en facturas; otro valor en pasivos internos (reposición de caja, viáticos), que no usan cuenta de proveedor. */
  tipoBeneficiario?: string;
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

/** Cuenta bancaria del proveedor (GET /tesoreria/proveedores/{id}/cuentas). */
export interface CuentaProveedorPago {
  id: string;
  banco: string;
  numero: string;
  esClabe: boolean;
  swift: string | null;
  moneda: string;
  titular: string | null;
  predeterminada: boolean;
  estado: 'Activa' | 'PorValidar' | 'Inhabilitada';
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
  motivoReversa?: string | null;
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
  /** Usuario que hizo la propuesta en CxC (R12: no puede confirmarla). */
  propuestoPor?: string | null;
  /** C7.4: movimiento que CxC identificó desde «Sin identificar»; se preselecciona al confirmar. */
  movimientoSugeridoId?: string | null;
  saldoAFavorPorIdentificar?: number;
  /** Corte de caja: lo que falta por depositar (efectivo menos fichas). */
  saldoPorDepositar?: number | null;
  /** Corte de caja: historial de fichas. */
  fichas?: DepositoFichaResponse[] | null;
}

// ---------------------------------------------------------------- REPP recibido (TES-FE-PR6b)

/** Mirror de `ReppPendienteResponse` (GET /tesoreria/repp-pendientes, TES-PR8). */
export interface ReppPendienteResponse {
  pagoId: string;
  importePendiente: number;
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

export interface ConceptoResponse {
  id: string;
  nombre: string;
  clasificacionFlujo: ClasificacionFlujo;
  activo: boolean;
  esEjemplo: boolean;
  version: number;
}

// ─── Corridas de pago (T9.1-A, 86e3ke5bt) ───────────────────────────

/** El backend serializa el estado de la corrida por nombre (no por el número del mirror de arriba). */
export type EstadoCorridaNombre =
  | 'Borrador'
  | 'EnAutorizacion'
  | 'Autorizada'
  | 'Ejecutada'
  | 'Cerrada'
  | 'Rechazada'
  | 'Cancelada';

/** Mirror de `PasivoParaCorridaResponse` (GET /tesoreria/corridas/candidatos). */
export interface PasivoParaCorridaResponse {
  facturaProveedorId: string;
  proveedorId: string;
  proveedorRazonSocial: string | null;
  folioProveedor: string | null;
  fechaVencimiento: string;
  moneda: string;
  bruto: number;
  retenciones: number;
  neto: number;
  cuentaValidada: boolean;
}

export interface CorridaProveedorResponse {
  proveedorId: string;
  proveedorRazonSocial: string | null;
  facturas: number;
  bruto: number;
  retenciones: number;
  neto: number;
}

export interface CorridaLineaResponse {
  facturaProveedorId: string;
  proveedorId: string;
  folioProveedor: string | null;
  fechaVencimiento: string | null;
  bruto: number;
  retenciones: number;
  neto: number;
  ejecutada: boolean;
  referenciaBancaria: string | null;
  fechaEjecucion: string | null;
}

export interface CorridaResumenResponse {
  id: string;
  nombre: string;
  estado: EstadoCorridaNombre;
  moneda: string;
  total: number;
  pasivos: number;
  firmasRequeridas: number;
  firmasRecibidas: number;
  creadaEn: string;
  version: number;
}

export interface CorridaResponse {
  id: string;
  nombre: string;
  estado: EstadoCorridaNombre;
  moneda: string;
  cuentaBancariaId: string;
  bruto: number;
  retenciones: number;
  total: number;
  solicitadaPor: string;
  autorizadaPor: string | null;
  autorizadaEn: string | null;
  creadaEn: string;
  firmasRequeridas: number;
  firmasRecibidas: number;
  rolRequerido: string | null;
  motivoCierre: string | null;
  avisos: string | null;
  version: number;
  proveedores: CorridaProveedorResponse[];
  lineas: CorridaLineaResponse[];
  firmas: { usuarioId: string; roles: string; firmadaEn: string }[];
  ejecutado: number;
  pendiente: number;
  oficioGeneradoEn: string | null;
}
