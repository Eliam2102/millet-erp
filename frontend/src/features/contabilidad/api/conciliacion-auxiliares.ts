import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/**
 * F1-CON-09 · Conciliación de auxiliares contra cuentas de control.
 * Espejo de `ConciliacionAuxiliaresQueries.cs` (GET /api/v1/contabilidad/reportes/conciliacion-auxiliares): fechas
 * `yyyy-MM-dd`, importes en MXN y enums como texto. Saldos en el sentido natural del control (clientes y bancos deudor,
 * proveedores acreedor); diferencia = contable − auxiliar.
 */
export type ControlConciliacion = 'Clientes' | 'Proveedores' | 'Bancos';
export type EstadoConciliacion = 'Cuadra' | 'Diferencia' | 'SinCuenta' | 'NoComparable';
export type TipoDiferenciaAuxiliar =
  | 'Proveedor'
  | 'DocumentoSinPoliza'
  | 'PartidaSinAuxiliar'
  | 'CuentaBancaria';

export interface CuentaControlRef {
  id: string;
  codigo: string;
  nombre: string;
}

export interface ControlConciliacionResumen {
  control: ControlConciliacion;
  etiqueta: string;
  estado: EstadoConciliacion;
  estadoEtiqueta: string;
  saldoContable: number | null;
  saldoAuxiliar: number | null;
  diferencia: number | null;
  cuentas: CuentaControlRef[];
  /** Rol del motor que configurar cuando el control no tiene cuenta. */
  rolFaltante: string | null;
  rechazosPendientes: number;
  notas: string[];
  diferenciasPorDocumento: number;
  /** Ruta del detalle que vive en otro módulo (clientes → CxC › Conciliación con pólizas). */
  enlaceDetalleExterno: string | null;
}

export interface ConciliacionAuxiliares {
  corte: string;
  sucursalId: string | null;
  generadoEn: string;
  todoCuadra: boolean;
  controles: ControlConciliacionResumen[];
}

export interface ColumnaConciliacionAuxiliar {
  clave: string;
  etiqueta: string;
  tipo: 'texto' | 'numero' | 'moneda' | 'fecha';
  alineacion: string | null;
}

export interface DiferenciaAuxiliar {
  tipo: TipoDiferenciaAuxiliar;
  tipoEtiqueta: string;
  terceroId: string | null;
  terceroClave: string | null;
  terceroNombre: string | null;
  documentoId: string | null;
  folio: string | null;
  uuid: string | null;
  moneda: string | null;
  saldoAuxiliar: number | null;
  saldoContable: number | null;
  diferencia: number;
  causa: string;
}

export interface ConciliacionAuxiliarDetalle {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: ColumnaConciliacionAuxiliar[];
  filas: DiferenciaAuxiliar[];
  totales: ControlConciliacionResumen;
}

export interface FiltrosConciliacionAuxiliares {
  /** `yyyy-MM-dd`; vacío = hoy (lo resuelve la API). */
  corte?: string;
  sucursalId?: string;
}

const BASE = '/api/v1/contabilidad/reportes/conciliacion-auxiliares';

export const conciliacionAuxiliaresKeys = {
  all: ['contabilidad', 'conciliacion-auxiliares'] as const,
  resumen: (f: FiltrosConciliacionAuxiliares) => ['contabilidad', 'conciliacion-auxiliares', 'resumen', f] as const,
  detalle: (control: ControlConciliacion, f: FiltrosConciliacionAuxiliares) =>
    ['contabilidad', 'conciliacion-auxiliares', 'detalle', control, f] as const,
};

function qs(params: Record<string, unknown>): string {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== null && v !== '') p.set(k, String(v));
  }
  const s = p.toString();
  return s ? `?${s}` : '';
}

export function useConciliacionAuxiliares(filtros: FiltrosConciliacionAuxiliares) {
  return useQuery({
    queryKey: conciliacionAuxiliaresKeys.resumen(filtros),
    queryFn: async ({ signal }) =>
      (
        await apiRequest<ConciliacionAuxiliares>(
          `${BASE}${qs({ corte: filtros.corte, sucursalId: filtros.sucursalId })}`,
          { signal },
        )
      ).data,
  });
}

/** Detalle por documento de proveedores o bancos. Sin control (o clientes) no se consulta. */
export function useConciliacionAuxiliarDetalle(
  control: ControlConciliacion | null,
  filtros: FiltrosConciliacionAuxiliares,
) {
  const habilitado = control === 'Proveedores' || control === 'Bancos';
  return useQuery({
    queryKey: conciliacionAuxiliaresKeys.detalle(control ?? 'Proveedores', filtros),
    enabled: habilitado,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<ConciliacionAuxiliarDetalle>(
          `${BASE}/${(control ?? '').toLowerCase()}${qs({ corte: filtros.corte, sucursalId: filtros.sucursalId })}`,
          { signal },
        )
      ).data,
  });
}
