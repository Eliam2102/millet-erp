/**
 * Mirror de los DTOs del motor de pólizas automáticas (C1.4): roles de cuenta, plantillas por tipo de evento y
 * rechazos. Fechas `yyyy-MM-dd` (DateOnly), instantes ISO y enums como texto.
 */

export interface RolCuenta {
  id: string;
  codigo: string;
  descripcion: string;
  cuentaId: string | null;
  cuentaCodigo: string | null;
  cuentaNombre: string | null;
  /** Filtro opcional: el rol aplica solo a esa sucursal (null = todas). */
  sucursalId: string | null;
  /** Filtro opcional: el rol aplica solo a esa moneda (null = todas). */
  moneda: string | null;
  vigenteDesde: string;
  vigenteHasta: string | null;
  version: number;
}

export interface CrearRolRequest {
  codigo: string;
  descripcion: string;
  cuentaId?: string | null;
  sucursalId?: string | null;
  moneda?: string | null;
  vigenteDesde?: string | null;
}

export interface CambiarCuentaRolRequest {
  cuentaId: string;
  motivo?: string | null;
}

export interface CambioCuentaRol {
  id: string;
  cuentaAnterior: string | null;
  cuentaNueva: string;
  usuario: string;
  cambiadoEn: string;
  motivo: string | null;
}

export type LadoLinea = 'Cargo' | 'Abono';
export type AuxiliarLinea = 'Ninguno' | 'Proveedor' | 'Cliente';

export interface LineaPlantillaContable {
  orden: number;
  rol: string;
  lado: LadoLinea;
  campo: string;
  auxiliar: AuxiliarLinea;
  porLinea: boolean;
  soloServicios: boolean | null;
  conCentroCosto: boolean;
  concepto: string | null;
}

export interface PlantillaContable {
  id: string;
  tipoEvento: string;
  version: number;
  vigenteDesde: string;
  tipoOperacionClave: string;
  concepto: string;
  descripcion: string;
  lineas: LineaPlantillaContable[];
}

export type EstadoRechazo = 'Pendiente' | 'Reprocesado' | 'Descartado';
export type CausaRechazo =
  | 'SinPlantilla'
  | 'RolSinCuenta'
  | 'PeriodoCerrado'
  | 'CuentaNoValida'
  | 'DatoFaltante'
  | 'NoCuadra';

export const ESTADOS_RECHAZO: readonly EstadoRechazo[] = ['Pendiente', 'Reprocesado', 'Descartado'];

export interface RechazoContable {
  id: string;
  tipoEvento: string;
  eventoId: string;
  causa: CausaRechazo;
  detalle: string | null;
  mensaje: string;
  intentos: number;
  estado: EstadoRechazo;
  creadoEn: string;
  ultimoIntentoEn: string;
  polizaId: string | null;
  resueltoPor: string | null;
  resueltoEn: string | null;
  motivoDescarte: string | null;
  /** JSON del evento original, tal como llegó. */
  payload: string;
  version: number;
}

export interface PolizaGenerada {
  id: string;
  folio: string | null;
  estado: string;
  idempotente: boolean;
}

export interface ResultadoReproceso {
  poliza: PolizaGenerada | null;
  rechazoId: string | null;
  causa: CausaRechazo | null;
  detalle: string | null;
  mensaje: string | null;
  generada: boolean;
}
