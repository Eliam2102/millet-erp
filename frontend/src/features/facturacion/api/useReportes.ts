import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { parseLocalToUtc } from '@/lib/datetime';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import type {
  ConciliacionFacturadoIngresadoResponse,
  EstadoComparativaAnticipo,
  MonitorCfdiResponse,
  ReporteBackend,
  VerificarEstatusSatResponse,
} from '@/features/facturacion/api/types';

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

export interface ComparativaAnticiposFiltros {
  sucursalId?: string;
  clienteId?: string;
  /** Fecha calendario `YYYY-MM-DD` (inicio del día en hora de México). */
  desde?: string;
  /** Fecha calendario `YYYY-MM-DD` (fin del día en hora de México). */
  hasta?: string;
  /** Fecha calendario `YYYY-MM-DD` de corte (fin del día); vacío = hoy. */
  corte?: string;
  estado?: EstadoComparativaAnticipo;
}

/** `YYYY-MM-DD` + hora local de México → instante ISO UTC para el backend. */
function instanteLocal(fecha: string, hora: string): string {
  return parseLocalToUtc(`${fecha}T${hora}`).toISOString();
}

/**
 * <c>useComparativaAnticipos(filtros)</c> — comparativa de anticipos contra
 * aplicaciones y relación CFDI (F6.2). El periodo filtra por fecha de timbrado
 * del anticipo; la fecha de corte fija el saldo (egresos timbrados hasta ese
 * día). Shape ADR-0036 listo para <c>&lt;ReporteShell&gt;</c>.
 */
export function useComparativaAnticipos(filtros: ComparativaAnticiposFiltros) {
  return useQuery<ReporteBackend<ReporteFila>>({
    queryKey: facturacionKeys.reporteComparativaAnticipos({ ...filtros }),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.sucursalId) params.set('sucursalId', filtros.sucursalId);
      if (filtros.clienteId) params.set('clienteId', filtros.clienteId);
      if (filtros.desde) params.set('desde', instanteLocal(filtros.desde, '00:00:00'));
      if (filtros.hasta) params.set('hasta', instanteLocal(filtros.hasta, '23:59:59.999'));
      if (filtros.corte) params.set('corte', instanteLocal(filtros.corte, '23:59:59.999'));
      if (filtros.estado != null) params.set('estado', String(filtros.estado));
      const qs = params.toString();
      const { data } = await apiRequest<ReporteBackend<ReporteFila>>(
        qs ? `${BASE}/comparativa-anticipos?${qs}` : `${BASE}/comparativa-anticipos`,
        { signal },
      );
      return data;
    },
  });
}

// ─── Cierre del día por sucursal (F6.7) ───────────────────────────────

export interface CierreDiaFiltros {
  /** Sin sucursal no se consulta (el reporte es por sucursal). */
  sucursalId?: string;
  /** Día calendario `YYYY-MM-DD` (hora de México; el backend arma el rango). */
  dia: string;
  cajeroId?: string;
}

function cierreDiaParams(filtros: CierreDiaFiltros): string {
  const params = new URLSearchParams();
  if (filtros.sucursalId) params.set('sucursalId', filtros.sucursalId);
  params.set('dia', filtros.dia);
  if (filtros.cajeroId) params.set('cajeroId', filtros.cajeroId);
  return params.toString();
}

/**
 * <c>useMonitorCfdi(filtros)</c> — monitor de estatus de CFDI del día en una
 * sucursal: timbrados, cancelados, sin timbrar, pedidos pendientes de
 * timbrar y estatus SAT de cada comprobante.
 */
export function useMonitorCfdi(filtros: CierreDiaFiltros) {
  return useQuery<MonitorCfdiResponse>({
    queryKey: facturacionKeys.reporteMonitorCfdi({ ...filtros }),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<MonitorCfdiResponse>(
        `${BASE}/monitor-cfdi?${cierreDiaParams(filtros)}`,
        { signal },
      );
      return data;
    },
    enabled: Boolean(filtros.sucursalId && filtros.dia),
  });
}

/**
 * <c>useConciliacionFacturadoIngresado(filtros)</c> — facturado de contado
 * contra lo ingresado en caja del día, por cajero y por partida.
 */
export function useConciliacionFacturadoIngresado(filtros: CierreDiaFiltros) {
  return useQuery<ConciliacionFacturadoIngresadoResponse>({
    queryKey: facturacionKeys.reporteConciliacionFacturadoIngresado({ ...filtros }),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ConciliacionFacturadoIngresadoResponse>(
        `${BASE}/conciliacion-facturado-ingresado?${cierreDiaParams(filtros)}`,
        { signal },
      );
      return data;
    },
    enabled: Boolean(filtros.sucursalId && filtros.dia),
  });
}

/**
 * <c>useVerificarEstatusSat()</c> — POST que consulta en el SAT el estatus
 * de los CFDI del día (Idempotency-Key, ADR-0020). Al terminar invalida el
 * monitor para mostrar el estatus actualizado.
 */
export function useVerificarEstatusSat() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      sucursalId: string;
      dia: string;
      idempotencyKey: string;
    }) => {
      const qs = cierreDiaParams({ sucursalId: args.sucursalId, dia: args.dia });
      const { data } = await apiRequest<VerificarEstatusSatResponse>(
        `${BASE}/monitor-cfdi/verificar-sat?${qs}`,
        { method: 'POST', idempotencyKey: args.idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: facturacionKeys.reporteMonitorCfdiTodos(),
      });
    },
  });
}
