import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import type { ReporteBackend } from '@/features/facturacion/api/types';

const BASE = '/api/v1/facturacion/reportes';

/** Fila genérica de reporte (ReporteShell la pinta por `columnas`). */
export type ReporteFila = Record<string, unknown>;

export interface LiquidacionCajaFiltros {
  sucursalId?: string;
  desde: string;
  hasta: string;
}

/**
 * <c>useLiquidacionCaja(filtros)</c> — reporte de liquidación de caja
 * (facturado por forma de pago). El backend devuelve el shape de reporte
 * (ADR-0036) listo para <c>&lt;ReporteShell&gt;</c>. Requiere rango de fechas.
 */
export function useLiquidacionCaja(filtros: LiquidacionCajaFiltros) {
  return useQuery<ReporteBackend<ReporteFila>>({
    queryKey: facturacionKeys.reporteLiquidacionCaja({ ...filtros }),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.sucursalId) params.set('sucursalId', filtros.sucursalId);
      params.set('desde', `${filtros.desde}T00:00:00Z`);
      params.set('hasta', `${filtros.hasta}T23:59:59Z`);
      const { data } = await apiRequest<ReporteBackend<ReporteFila>>(
        `${BASE}/liquidacion-caja?${params.toString()}`,
        { signal },
      );
      return data;
    },
    enabled: Boolean(filtros.desde && filtros.hasta),
  });
}

export interface EstadosAnticiposFiltros {
  clienteId?: string;
  estado?: number;
}

/** <c>useEstadosAnticipos(filtros)</c> — reporte estados de facturas de anticipo. */
export function useEstadosAnticipos(filtros: EstadosAnticiposFiltros) {
  return useQuery<ReporteBackend<ReporteFila>>({
    queryKey: facturacionKeys.reporteEstadosAnticipos({ ...filtros }),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.clienteId) params.set('clienteId', filtros.clienteId);
      if (filtros.estado != null) params.set('estado', String(filtros.estado));
      const qs = params.toString();
      const { data } = await apiRequest<ReporteBackend<ReporteFila>>(
        qs ? `${BASE}/estados-anticipos?${qs}` : `${BASE}/estados-anticipos`,
        { signal },
      );
      return data;
    },
  });
}
