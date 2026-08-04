import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxcKeys } from '@/features/cxc/api/keys';
import type {
  AnticipoSaldoClienteItem,
  ReporteCxcBackend,
} from '@/features/cxc/api/types';

const BASE = '/api/v1/cuentas-por-cobrar';

export interface AntiguedadFiltros {
  /** "yyyy-MM-dd"; omitido = hoy (default backend). */
  fechaCorte?: string;
  clienteId?: string;
  moneda?: string;
}

/**
 * <c>useAntiguedadSaldos(filtros)</c> — antigüedad de saldos (CXC-PR6,
 * ADR-0036). Los buckets de días vencidos son CONFIGURABLES en backend
 * (<c>CuentasPorCobrar:Reportes:BucketLimites</c>) — las columnas
 * llegan dinámicas y el FE nunca las hardcodea.
 */
export function useAntiguedadSaldos(
  filtros: AntiguedadFiltros = {},
  opts: { enabled?: boolean } = {},
) {
  return useQuery<ReporteCxcBackend>({
    queryKey: cxcKeys.antiguedad({
      fechaCorte: filtros.fechaCorte ?? null,
      clienteId: filtros.clienteId ?? null,
      moneda: filtros.moneda ?? null,
    }),
    enabled: opts.enabled ?? true,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.fechaCorte) params.set('fechaCorte', filtros.fechaCorte);
      if (filtros.clienteId) params.set('clienteId', filtros.clienteId);
      if (filtros.moneda) params.set('moneda', filtros.moneda);
      const qs = params.toString();
      const { data } = await apiRequest<ReporteCxcBackend>(
        qs ? `${BASE}/cartera/antiguedad?${qs}` : `${BASE}/cartera/antiguedad`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useEstadoCuentaCliente(clienteId)</c> — estado de cuenta ADR-0036
 * (facturas, pagos, NCs y saldo corriente del cliente).
 */
export function useEstadoCuentaCliente(clienteId: string | null) {
  return useQuery<ReporteCxcBackend>({
    queryKey: cxcKeys.estadoCuenta(clienteId ?? ''),
    enabled: clienteId != null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ReporteCxcBackend>(
        `${BASE}/cartera/estado-cuenta/${clienteId}`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useAnticiposCliente(clienteId)</c> — saldos de anticipo del read
 * port de Facturación (§6.1 del 01-diseño). El cliente es obligatorio
 * en el endpoint; sin él la query no se dispara.
 */
export function useAnticiposCliente(clienteId: string | null) {
  return useQuery<AnticipoSaldoClienteItem[]>({
    queryKey: cxcKeys.anticipos(clienteId ?? ''),
    enabled: clienteId != null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<AnticipoSaldoClienteItem[]>(
        `${BASE}/anticipos?clienteId=${clienteId}`,
        { signal },
      );
      return data;
    },
  });
}
