import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/**
 * C1.7 · Diario, mayor, auxiliar y balanza de comprobación (F1-CON-06 / CON-07).
 * Espejo de `ReportesContablesQueries.cs` (GET /api/v1/contabilidad/reportes/{balanza|mayor|auxiliar|diario}).
 * Saldos con signo deudor (cargo − abono); fechas `yyyy-MM-dd`; enums como texto. Cuentan las pólizas autorizadas y
 * las anuladas con su anulación (suman cero); los borradores no.
 */
export interface ColumnaReporteContable {
  clave: string;
  etiqueta: string;
  tipo: 'texto' | 'numero' | 'moneda' | 'fecha';
  alineacion: string | null;
}

export type NaturalezaCuenta = 'Deudora' | 'Acreedora';
export type TipoAuxiliar = 'Cliente' | 'Proveedor' | 'Banco';
export type EstadoPolizaReporte = 'Borrador' | 'Autorizada' | 'Anulada';
export type OrigenPolizaReporte = 'Manual' | 'Automatica';

export interface CuentaReporteRef {
  id: string;
  codigo: string;
  nombre: string;
  nivel: number;
  naturaleza: NaturalezaCuenta | null;
}

interface EncabezadoReporte {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  anio: number;
  desde: number;
  hasta: number;
  sucursalId: string | null;
}

// ─── Balanza ───

export interface FilaBalanza {
  cuentaId: string;
  codigo: string;
  nombre: string;
  nivel: number;
  naturaleza: NaturalezaCuenta | null;
  tieneHijas: boolean;
  saldoInicial: number;
  cargos: number;
  abonos: number;
  saldoFinal: number;
  saldoDeudor: number;
  saldoAcreedor: number;
}

export interface TotalesBalanza {
  saldoInicialDeudor: number;
  saldoInicialAcreedor: number;
  cargos: number;
  abonos: number;
  saldoFinalDeudor: number;
  saldoFinalAcreedor: number;
  cuadra: boolean;
  cuentas: number;
}

export interface Balanza extends EncabezadoReporte {
  columnas: ColumnaReporteContable[];
  filas: FilaBalanza[];
  totales: TotalesBalanza;
  nivel: number | null;
}

export interface FiltrosBalanza {
  anio: number;
  periodo: number;
  acumulada: boolean;
  nivel?: number;
  sucursalId?: string;
  soloConSaldo: boolean;
}

// ─── Mayor ───

export interface MovimientoMayor {
  polizaId: string;
  polizaFolio: string | null;
  linea: number;
  fecha: string;
  periodoAnio: number;
  periodoNumero: number;
  concepto: string;
  cuentaCodigo: string;
  sucursalId: string;
  sucursalNombre: string | null;
  estadoPoliza: EstadoPolizaReporte;
  cargo: number;
  abono: number;
  saldo: number;
}

export interface Mayor extends EncabezadoReporte {
  columnas: ColumnaReporteContable[];
  cuenta: CuentaReporteRef;
  saldoInicial: number;
  cargos: number;
  abonos: number;
  saldoFinal: number;
  filas: MovimientoMayor[];
}

export interface FiltrosRango {
  cuentaId: string;
  anio: number;
  desde: number;
  hasta: number;
  sucursalId?: string;
}

// ─── Auxiliar ───

export interface FilaAuxiliar {
  auxiliarId: string | null;
  clave: string | null;
  nombre: string;
  saldoInicial: number;
  cargos: number;
  abonos: number;
  saldoFinal: number;
}

export interface TotalesAuxiliar {
  saldoInicial: number;
  cargos: number;
  abonos: number;
  saldoFinal: number;
  sinAuxiliar: number;
  cuadra: boolean;
}

export interface Auxiliar extends EncabezadoReporte {
  columnas: ColumnaReporteContable[];
  cuenta: CuentaReporteRef;
  tipo: TipoAuxiliar;
  filas: FilaAuxiliar[];
  totales: TotalesAuxiliar;
}

export interface FiltrosAuxiliar extends FiltrosRango {
  tipo: TipoAuxiliar;
  soloConSaldo: boolean;
}

// ─── Diario ───

export interface PartidaDiario {
  linea: number;
  cuentaId: string;
  cuentaCodigo: string;
  cuentaNombre: string | null;
  concepto: string | null;
  sucursalId: string;
  sucursalNombre: string | null;
  cargo: number;
  abono: number;
}

export interface PolizaDiario {
  id: string;
  folio: string | null;
  tipoClave: string;
  fecha: string;
  periodoAnio: number;
  periodoNumero: number;
  concepto: string;
  origen: OrigenPolizaReporte;
  estado: EstadoPolizaReporte;
  origenModulo: string | null;
  origenFolio: string | null;
  cargos: number;
  abonos: number;
  partidas: PartidaDiario[];
}

export interface TotalesDiario {
  polizas: number;
  partidas: number;
  cargos: number;
  abonos: number;
  cuadra: boolean;
}

export interface Diario extends EncabezadoReporte {
  polizas: PolizaDiario[];
  totales: TotalesDiario;
  total: number;
  offset: number;
  limit: number;
}

export interface FiltrosDiario {
  anio: number;
  desde: number;
  hasta: number;
  sucursalId?: string;
  origen?: OrigenPolizaReporte;
  offset: number;
  limit: number;
}

const BASE = '/api/v1/contabilidad/reportes';

export const reportesContablesKeys = {
  all: ['contabilidad', 'reportes'] as const,
  balanza: (f: FiltrosBalanza) => ['contabilidad', 'reportes', 'balanza', f] as const,
  mayor: (f: FiltrosRango) => ['contabilidad', 'reportes', 'mayor', f] as const,
  auxiliar: (f: FiltrosAuxiliar) => ['contabilidad', 'reportes', 'auxiliar', f] as const,
  diario: (f: FiltrosDiario) => ['contabilidad', 'reportes', 'diario', f] as const,
};

function qs(params: object): string {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== null && v !== '') p.set(k, String(v));
  }
  const s = p.toString();
  return s ? `?${s}` : '';
}

/** Balanza del periodo o acumulada del ejercicio. Sin filtros aplicados no se consulta. */
export function useBalanza(filtros: FiltrosBalanza | null) {
  return useQuery({
    queryKey: reportesContablesKeys.balanza(filtros ?? ({} as FiltrosBalanza)),
    enabled: !!filtros,
    queryFn: async ({ signal }) => (await apiRequest<Balanza>(`${BASE}/balanza${qs(filtros!)}`, { signal })).data,
  });
}

/** Mayor de una cuenta en un rango de periodos del ejercicio. */
export function useMayor(filtros: FiltrosRango | null) {
  return useQuery({
    queryKey: reportesContablesKeys.mayor(filtros ?? ({} as FiltrosRango)),
    enabled: !!filtros,
    queryFn: async ({ signal }) => (await apiRequest<Mayor>(`${BASE}/mayor${qs(filtros!)}`, { signal })).data,
  });
}

/** Auxiliar de una cuenta por cliente, proveedor o banco. */
export function useAuxiliar(filtros: FiltrosAuxiliar | null) {
  return useQuery({
    queryKey: reportesContablesKeys.auxiliar(filtros ?? ({} as FiltrosAuxiliar)),
    enabled: !!filtros,
    queryFn: async ({ signal }) => (await apiRequest<Auxiliar>(`${BASE}/auxiliar${qs(filtros!)}`, { signal })).data,
  });
}

/** Libro diario paginado por póliza (límite máximo 1000 por página). */
export function useDiario(filtros: FiltrosDiario | null) {
  return useQuery({
    queryKey: reportesContablesKeys.diario(filtros ?? ({} as FiltrosDiario)),
    enabled: !!filtros,
    queryFn: async ({ signal }) => (await apiRequest<Diario>(`${BASE}/diario${qs(filtros!)}`, { signal })).data,
  });
}
