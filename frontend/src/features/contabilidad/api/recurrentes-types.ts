/**
 * Mirror de los DTOs de pólizas recurrentes (K10.4). Fechas `yyyy-MM-dd` (DateOnly), instantes ISO y enums como texto.
 * `diaDelMes` 0 = último día del mes. Importes en MXN.
 */
import type { Auxiliar, CentroRef } from './dimensiones-types';
import type { EstadoPoliza, PartidaRequest } from './polizas-types';

export type EstadoPlantillaRecurrente = 'Borrador' | 'Activa' | 'Suspendida' | 'Terminada';

export const ESTADOS_PLANTILLA: readonly EstadoPlantillaRecurrente[] = ['Borrador', 'Activa', 'Suspendida', 'Terminada'];

/** Día del mes que representa «último día del mes» (mismo valor que el backend). */
export const ULTIMO_DIA_DEL_MES = 0;

export interface PlantillaRecurrenteResumen {
  id: string;
  nombre: string;
  tipoClave: string;
  anioInicio: number;
  mesInicio: number;
  numeroPeriodos: number;
  reversaAutomatica: boolean;
  naceAutorizada: boolean;
  estado: EstadoPlantillaRecurrente;
  importe: number;
  generadas: number;
  creadaPor: string;
  version: number;
}

/**
 * Partida fija de la plantilla. El backend hoy devuelve el mismo `PartidaRequest` (solo ids); los campos descriptivos son
 * opcionales para aprovecharlos si el DTO se enriquece (cuenta, sucursal y centros con clave y nombre).
 */
export interface PartidaPlantilla extends PartidaRequest {
  cuentaCodigo?: string | null;
  cuentaNombre?: string | null;
  sucursalNombre?: string | null;
  dim1?: CentroRef | null;
  dim2?: CentroRef | null;
  dim3?: CentroRef | null;
  cliente?: Auxiliar | null;
  proveedor?: Auxiliar | null;
  cuentaBancaria?: Auxiliar | null;
}

export interface AplicacionRecurrente {
  anio: number;
  mes: number;
  fecha: string;
  polizaId: string | null;
  folio: string | null;
  estado: EstadoPoliza | null;
  fechaReversa: string | null;
  reversaId: string | null;
  reversaFolio: string | null;
}

export interface PlantillaRecurrente {
  id: string;
  nombre: string;
  tipoOperacionId: string;
  tipoClave: string;
  concepto: string;
  anioInicio: number;
  mesInicio: number;
  numeroPeriodos: number;
  diaDelMes: number;
  reversaAutomatica: boolean;
  naceAutorizada: boolean;
  estado: EstadoPlantillaRecurrente;
  importe: number;
  partidas: PartidaPlantilla[];
  creadaPorId: string | null;
  creadaPor: string;
  creadaEn: string;
  activadaPorId: string | null;
  activadaPor: string | null;
  activadaEn: string | null;
  generadas: number;
  aplicaciones: AplicacionRecurrente[];
  version: number;
}

export interface PlantillaRecurrenteRequest {
  nombre: string;
  tipoOperacionId: string;
  concepto: string;
  anioInicio: number;
  mesInicio: number;
  numeroPeriodos: number;
  diaDelMes: number;
  reversaAutomatica: boolean;
  naceAutorizada: boolean;
  partidas: PartidaRequest[];
}

export type AccionPlantilla = 'activar' | 'suspender' | 'reanudar';

export interface AplicacionGenerada {
  plantillaId: string;
  plantilla: string;
  anio: number;
  mes: number;
  polizaId: string;
  folio: string | null;
  estado: EstadoPoliza;
  esReversa: boolean;
}

export interface AplicacionPendiente {
  plantillaId: string;
  plantilla: string;
  anio: number;
  mes: number;
  esReversa: boolean;
  codigo: string;
  motivo: string;
}

export interface ResultadoGeneracion {
  generadas: AplicacionGenerada[];
  pendientes: AplicacionPendiente[];
}
