import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, esConflictoConcurrencia, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import type { PtSearch } from '../lib/search';
export interface PtPiece {
  id: string;
  pedido: string;
  posicion: number;
  numeroPieza: number;
  sucursalId: string;
  escaneadaAt: string;
  estado: string;
  estatusAw: number;
}
export interface PtScan {
  id: string;
  numeroPedido: string;
  posicion: number;
  numeroPieza: number | null;
  sucursalId: string | null;
  resultado: 'Pospuesto' | 'Excepcion';
  motivo: string;
  intentos: number;
  version: number;
}
const BASE = '/api/v1/producto-terminado';
export const ptKeys = ['producto-terminado'] as const;
export function usePtList<T>(resource: 'piezas' | 'escaneos', filters: PtSearch) {
  return useQuery({
    queryKey: [...ptKeys, resource, filters],
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams({ page: String(filters.page) });
      if (filters.pedido) params.set('pedido', filters.pedido);
      if (filters.sucursalId) params.set('sucursalId', filters.sucursalId);
      return (
        await apiRequest<{ items: T[]; total: number }>(`${BASE}/${resource}?${params}`, { signal })
      ).data;
    },
  });
}
export function useResolveScan() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  const conflict = useConflictDialog();
  return useMutation({
    mutationFn: ({
      scan,
      action,
      reason,
    }: {
      scan: PtScan;
      action: 'reintentar' | 'descartar';
      reason: string;
    }) =>
      apiRequest(`${BASE}/escaneos/${scan.id}/${action}`, {
        method: 'POST',
        body: { motivo: reason.trim() },
        ifMatch: String(scan.version),
        idempotencyKey: keyFor({
          id: scan.id,
          version: scan.version,
          action,
          reason: reason.trim(),
        }),
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ptKeys }),
    onError: (error) => {
      if (esConflictoConcurrencia(error))
        conflict.openSimple({ onRefrescar: () => qc.invalidateQueries({ queryKey: ptKeys }) });
    },
  });
}
