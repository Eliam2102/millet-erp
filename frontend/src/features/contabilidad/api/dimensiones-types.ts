/**
 * Mirror de los DTOs de dimensiones contables (F1-CON-02, docs/modulos/contabilidad/06-plan-dimensiones-contables.md).
 * Las fechas de vigencia y la fecha contable viajan como `yyyy-MM-dd` (DateOnly). Los enums viajan como texto.
 */
import type { OrigenMovimiento } from './types';

export type Dimension = 'Dim1' | 'Dim2' | 'Dim3';
export type Requerimiento = 'Obligatorio' | 'Opcional' | 'NoAplica';
export type EstadoRegla = 'Futura' | 'Vigente' | 'Cerrada';

export const DIMENSIONES: readonly Dimension[] = ['Dim1', 'Dim2', 'Dim3'];
export const REQUERIMIENTOS: readonly Requerimiento[] = ['Obligatorio', 'Opcional', 'NoAplica'];

export interface TipoDocumento {
  id: string;
  clave: string;
  nombre: string;
  activo: boolean;
  esPrueba: boolean;
  version: number;
}

export interface Regla {
  id: string;
  cuentaId: string;
  cuentaCodigo: string;
  cuentaNombre: string;
  tipoDocumentoId: string | null;
  tipoDocumentoClave: string | null;
  tipoDocumentoNombre: string | null;
  dimension: Dimension;
  nombreDimension: string;
  requerimiento: Requerimiento;
  vigenteDesde: string;
  vigenteHasta: string | null;
  estado: EstadoRegla;
  /** Solo las reglas que aún no inician se editan; una en vigor se cierra y se crea otra. */
  editable: boolean;
  esPrueba: boolean;
  nota: string | null;
  version: number;
}

export interface PaginaReglas {
  items: Regla[];
  total: number;
  offset: number;
  limit: number;
}

export interface FiltrosReglas {
  cuentaId?: string;
  tipoDocumentoId?: string;
  dimension?: Dimension;
  vigentesA?: string;
  esPrueba?: boolean;
  offset: number;
  limit: number;
}

export interface NuevaReglaBody {
  cuentaId: string;
  tipoDocumentoId: string | null;
  dimension: Dimension;
  requerimiento: Requerimiento;
  vigenteDesde: string;
  vigenteHasta: string | null;
  esPrueba: boolean;
  nota: string | null;
}

export interface EditarReglaBody {
  requerimiento: Requerimiento;
  vigenteDesde: string;
  vigenteHasta: string | null;
  nota: string | null;
}

/** Requerimiento efectivo de una dimensión; `reglaId` null = sin regla (se aplica la configuración). */
export interface RequerimientoEfectivo {
  dimension: Dimension;
  nombreDimension: string;
  requerimiento: Requerimiento;
  reglaId: string | null;
  cuentaOrigenCodigo: string | null;
  heredada: boolean;
  paraTodosLosTipos: boolean;
  vigenteDesde: string | null;
  vigenteHasta: string | null;
  esPrueba: boolean;
}

export interface Sucursal {
  id: string;
  clave: string;
  nombre: string;
  activa: boolean;
}

export interface CentroSucursales {
  dim2Id: string;
  clave: string;
  nombre: string;
  activo: boolean;
  dim1Clave: string;
  sucursales: Sucursal[];
}

export interface CentroOpcion {
  id: string;
  nivel: Dimension;
  clave: string;
  nombre: string;
  dim1Id: string;
  dim2Id: string | null;
  dim1Clave: string;
  dim2Clave: string | null;
}

export type CampoMovimiento = 'cuentaId' | 'tipoDocumentoId' | 'sucursalId' | 'dim1Id' | 'dim2Id' | 'dim3Id';

export interface ErrorDimension {
  codigo: string;
  mensaje: string;
  campo: CampoMovimiento;
  dimension: Dimension | null;
}

export interface Validacion {
  valido: boolean;
  errores: ErrorDimension[];
  requerimientos: RequerimientoEfectivo[];
  centros: { dim1Id: string | null; dim2Id: string | null; dim3Id: string | null };
}

export interface MovimientoBody {
  cuentaId: string;
  tipoDocumentoId: string;
  fechaContable: string;
  sucursalId: string;
  dim1Id: string | null;
  dim2Id: string | null;
  dim3Id: string | null;
  origen: OrigenMovimiento;
  referencia: string | null;
}

export interface CentroRef {
  id: string;
  clave: string;
  nombre: string;
  activo: boolean;
}

export interface MovimientoPrueba {
  id: string;
  sucursalId: string;
  sucursalNombre: string | null;
  cuentaId: string;
  cuentaCodigo: string;
  tipoDocumentoId: string;
  tipoDocumentoClave: string;
  fechaContable: string;
  dim1: CentroRef | null;
  dim2: CentroRef | null;
  dim3: CentroRef | null;
  referencia: string | null;
  reglasAplicadas: RequerimientoEfectivo[];
  confirmadoEn: string;
  confirmadoPor: string | null;
}

export interface PaginaMovimientos {
  items: MovimientoPrueba[];
  total: number;
  offset: number;
  limit: number;
}
