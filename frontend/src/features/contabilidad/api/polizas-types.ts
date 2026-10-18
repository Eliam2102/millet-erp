/**
 * Mirror de los DTOs de pólizas contables (F1-CON-04: C1.3 captura/autorización, K10.9 folio por tipo, K10.3 anulación).
 * Fechas `yyyy-MM-dd` (DateOnly), instantes ISO y enums como texto. `folio` es null mientras la póliza es borrador.
 */
import type { Auxiliar, CentroRef } from './dimensiones-types';

export type EstadoPoliza = 'Borrador' | 'Autorizada' | 'Anulada';
export type OrigenPoliza = 'Manual' | 'Automatica';
export type TipoPolizaSat = 'Ingreso' | 'Egreso' | 'Diario';

/** K10.4: relación de una póliza derivada con su original (reversa automática de recurrente o reclasificación ligada). */
export type RelacionPoliza = 'Reversa' | 'Reclasificacion';

export const ESTADOS_POLIZA: readonly EstadoPoliza[] = ['Borrador', 'Autorizada', 'Anulada'];
export const TIPOS_SAT: readonly TipoPolizaSat[] = ['Ingreso', 'Egreso', 'Diario'];

export interface PolizaResumen {
  id: string;
  folio: string | null;
  tipoOperacionId: string;
  tipoClave: string;
  numero: number | null;
  fecha: string;
  periodoAnio: number;
  periodoNumero: number;
  concepto: string;
  origen: OrigenPoliza;
  estado: EstadoPoliza;
  esAnulacion: boolean;
  totalCargos: number;
  totalAbonos: number;
  capturadaPor: string;
  autorizadaPor: string | null;
  version: number;
  /** K10.4: la póliza es reversa o reclasificación de otra. */
  relacion?: RelacionPoliza | null;
  /** K10.4: generada por una plantilla recurrente. */
  esRecurrente?: boolean;
}

export interface PaginaPolizas {
  items: PolizaResumen[];
  total: number;
  offset: number;
  limit: number;
}

export interface DocumentoOrigen {
  modulo: string;
  tipo: string;
  id: string;
  folio: string | null;
  uuid: string | null;
}

export interface Partida {
  id: string;
  linea: number;
  cuentaId: string;
  cuentaCodigo: string;
  cuentaNombre: string | null;
  concepto: string | null;
  cargo: number;
  abono: number;
  moneda: string;
  tipoCambio: number;
  importeOriginal: number | null;
  sucursalId: string;
  sucursalNombre: string | null;
  dim1: CentroRef | null;
  dim2: CentroRef | null;
  dim3: CentroRef | null;
  proyecto: string | null;
  cliente: Auxiliar | null;
  proveedor: Auxiliar | null;
  cuentaBancaria: Auxiliar | null;
}

export interface Poliza {
  id: string;
  folio: string | null;
  tipoOperacionId: string;
  tipoClave: string;
  tipoNombre: string | null;
  tipoSat: TipoPolizaSat;
  numero: number | null;
  fecha: string;
  periodoAnio: number;
  periodoNumero: number;
  concepto: string;
  origen: OrigenPoliza;
  estado: EstadoPoliza;
  documentoOrigen: DocumentoOrigen | null;
  /** En la póliza de anulación: la original que anula. */
  polizaAnuladaId: string | null;
  polizaAnuladaFolio: string | null;
  /** En la original anulada: su póliza de anulación. */
  anuladaPorPolizaId: string | null;
  anuladaPorFolio: string | null;
  motivoAnulacion: string | null;
  anuladaPor: string | null;
  anuladaEn: string | null;
  capturadaPorId: string | null;
  capturadaPor: string;
  capturadaEn: string;
  autorizadaPorId: string | null;
  autorizadaPor: string | null;
  autorizadaEn: string | null;
  totalCargos: number;
  totalAbonos: number;
  diferencia: number;
  cuadra: boolean;
  partidas: Partida[];
  version: number;
  /** K10.4: en la reversa o la reclasificación, la póliza original con la que se liga. */
  polizaRelacionadaId?: string | null;
  polizaRelacionadaFolio?: string | null;
  relacion?: RelacionPoliza | null;
  /** K10.4: plantilla recurrente que generó la póliza (o su reversa). */
  plantillaRecurrenteId?: string | null;
  plantillaRecurrenteNombre?: string | null;
  /** K10.4: pólizas derivadas de esta (su reversa automática y sus reclasificaciones). */
  ligadas?: PolizaLigada[];
}

export interface PolizaLigada {
  id: string;
  folio: string | null;
  relacion: RelacionPoliza;
  estado: EstadoPoliza;
  fecha: string;
  concepto: string;
}

export interface PartidaRequest {
  cuentaId: string;
  concepto: string | null;
  cargo: number;
  abono: number;
  sucursalId: string;
  dim1Id: string | null;
  dim2Id: string | null;
  dim3Id: string | null;
  proyecto: string | null;
  clienteId: string | null;
  proveedorId: string | null;
  cuentaBancariaId: string | null;
  moneda?: string | null;
  tipoCambio?: number | null;
  importeOriginal?: number | null;
}

export interface PolizaManualRequest {
  tipoOperacionId: string;
  fecha: string;
  concepto: string;
  partidas: PartidaRequest[];
  /** Solo para el periodo 13 de ajustes (fecha 31-dic); los demás periodos salen de la fecha. */
  periodoNumero: 13 | null;
  documentoOrigen: null;
  /** K10.4: póliza autorizada que esta reclasifica (solo al crear; queda ligada en ambos sentidos). */
  reclasificaPolizaId?: string | null;
}

export interface AnularPolizaRequest {
  motivo: string;
  periodoAnio: number | null;
  periodoNumero: number | null;
}

export interface FiltrosPolizas {
  anio?: number;
  numero?: number;
  tipoOperacionId?: string;
  estado?: EstadoPoliza;
  origen?: OrigenPoliza;
  q?: string;
  offset: number;
  limit: number;
}

export interface SaldoCuentaPeriodo {
  cuentaId: string;
  cuentaCodigo: string;
  cuentaNombre: string | null;
  cargos: number;
  abonos: number;
  saldo: number;
}
