import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxcKeys } from '@/features/cxc/api/keys';
import type { ReporteCxcBackend } from '@/features/cxc/api/types';

/**
 * C7.7 (86e3jvv4q): conciliación diaria de cartera contra Facturación.
 * Leer las corridas pide <c>cartera.leer</c>; conciliar a demanda pide
 * <c>conciliacion.ejecutar</c>.
 */
const BASE = '/api/v1/cuentas-por-cobrar/conciliaciones';

export type OrigenConciliacion = 'Programada' | 'Demanda';

export interface ConciliacionResumen {
  id: string;
  /** Día contable conciliado (DateOnly, `YYYY-MM-DD`). */
  fecha: string;
  origen: OrigenConciliacion;
  ejecutadaPor: string | null;
  ejecutadaEn: string;
  comprobantesRevisados: number;
  totalDiferencias: number;
}

export interface ConciliacionDetalle {
  corrida: ConciliacionResumen;
  reporte: ReporteCxcBackend;
}

export function useConciliaciones() {
  return useQuery<ConciliacionResumen[]>({
    queryKey: cxcKeys.conciliacionesList(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ConciliacionResumen[]>(BASE, { signal });
      return data;
    },
  });
}

export function useConciliacionDetalle(id: string | null) {
  return useQuery<ConciliacionDetalle>({
    queryKey: cxcKeys.conciliacionById(id ?? ''),
    enabled: id != null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ConciliacionDetalle>(`${BASE}/${id}`, { signal });
      return data;
    },
  });
}

export function useConciliarCartera() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { fecha: string; idempotencyKey: string }) => {
      const { data } = await apiRequest<ConciliacionResumen>(BASE, {
        method: 'POST',
        body: { fecha: args.fecha },
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: cxcKeys.conciliaciones() }),
  });
}
