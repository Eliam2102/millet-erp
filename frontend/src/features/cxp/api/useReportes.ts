import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxpKeys } from '@/features/cxp/api/keys';

/**
 * Hooks de reportes CxP (FE-F7-PR1, ADR-0036). Cada reporte devuelve
 * <see cref="BackendReporteResponse"/> con shape estandarizado del
 * backend (PascalCase serializado a camelCase). El adapter en
 * <c>lib/reportes-adapter.ts</c> convierte a la forma que
 * <c>&lt;ReporteShell&gt;</c> consume.
 *
 * <para>PLATFORM-TODO(&lt;ReporteShellTipoColumnaUnificado&gt;): el
 * backend usa enum numérico (1-8) para tipo de columna; ReporteShell
 * usa unión string ('texto'|'numero'|'moneda'|'fecha'). El adapter
 * mapea; un follow-up podría unificar ambos lados.</para>
 */

export interface BackendColumnaReporte {
  key: string;
  label: string;
  /** TipoColumnaReporte: 1=Texto, 2=Entero, 3=Numerico, 4=Moneda, 5=Fecha, 6=FechaHora, 7=Booleano, 8=Enum */
  tipo: number;
  /** AlineacionColumna: 1=Izquierda, 2=Centro, 3=Derecha */
  alineacion: number;
}

export interface BackendFiltroAplicado {
  label: string;
  valor: string;
}

export interface BackendReporteResponse {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: BackendFiltroAplicado[];
  columnas: BackendColumnaReporte[];
  filas: Record<string, unknown>[];
  totales: Record<string, unknown> | null;
}

function buildQuery(params: Record<string, unknown>): string {
  const sp = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      sp.set(key, String(value));
    }
  }
  return sp.toString();
}

function makeReporteHook<TParams extends Record<string, unknown>>(
  reporteKey: string,
  endpointPath: string,
) {
  return (params: TParams, enabled = true) =>
    useQuery({
      queryKey: [...cxpKeys.all, 'reportes', reporteKey, params] as const,
      queryFn: async ({ signal }) => {
        const q = buildQuery(params);
        const path = q ? `${endpointPath}?${q}` : endpointPath;
        const { data } = await apiRequest<BackendReporteResponse>(path, {
          signal,
        });
        return data;
      },
      enabled,
    });
}

// Antigüedad de saldos
export const useReporteAntiguedadSaldos = makeReporteHook<{
  fechaCorte?: string;
  proveedorId?: string;
  sucursalId?: string;
}>(
  'antiguedad-saldos',
  '/api/v1/cuentas-por-pagar/reportes/antiguedad-saldos',
);

// Antigüedad de anticipos
export const useReporteAntiguedadAnticipos = makeReporteHook<{
  fechaCorte?: string;
  proveedorId?: string;
}>(
  'antiguedad-anticipos',
  '/api/v1/cuentas-por-pagar/reportes/antiguedad-anticipos',
);

// Cartera por categoría de revisión
export const useReporteCartera = makeReporteHook<{
  fechaCorte?: string;
  proveedorId?: string;
  sucursalId?: string;
  soloEnRevision?: boolean;
}>('cartera', '/api/v1/cuentas-por-pagar/reportes/cartera');

// Movimientos TC pendientes
export const useReporteMovimientosTcPendientes = makeReporteHook<{
  tarjetaId?: string;
  usuarioQueUsoId?: string;
  fechaDesde?: string;
  fechaHasta?: string;
}>(
  'movimientos-tc-pendientes',
  '/api/v1/cuentas-por-pagar/reportes/movimientos-tc-pendientes',
);

// Estados de cuenta TC consolidado
export const useReporteEstadosCuentaTcConsolidado = makeReporteHook<{
  tarjetaId?: string;
  estado?: number;
  periodoDesde?: string;
  periodoHasta?: string;
}>(
  'estados-cuenta-tc-consolidado',
  '/api/v1/cuentas-por-pagar/reportes/estados-cuenta-tc-consolidado',
);

// Pasivos por obra
export const useReportePasivosObras = makeReporteHook<{
  sucursalId: string;
  fechaCorte?: string;
  proveedorId?: string;
}>('pasivos-obras', '/api/v1/cuentas-por-pagar/reportes/pasivos-obras');
