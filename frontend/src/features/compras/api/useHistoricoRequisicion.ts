import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { comprasKeys } from '@/features/compras/api/keys';
import type { HistoricoEntryResponse } from '@/features/compras/api/types';

/**
 * <c>useHistoricoRequisicion(id)</c> — timeline completo de
 * transiciones del agregado <c>Requisicion</c>. Doc 05 §14.1.
 *
 * <para>Endpoint: <c>GET /api/v1/compras/requisiciones/{id}/historico</c>.
 * Permiso requerido: <c>compras.requisiciones.leer</c> (mismo gate que
 * el detalle). Devuelve hasta 500 entradas ordenadas por timestamp
 * ascendente; en la práctica una RQ típica tiene ~10-30 eventos.</para>
 *
 * <para>El cache se anida bajo <c>requisiciones</c> (no bajo una
 * familia propia) para que las invalidaciones masivas tras una
 * mutation refresquen también el timeline sin trabajo extra. Ver
 * <c>comprasKeys.historicoRequisicion(id)</c>.</para>
 */
export function useHistoricoRequisicion(id: string | null | undefined) {
  return useQuery({
    queryKey:
      id != null
        ? comprasKeys.historicoRequisicion(id)
        : (['compras', 'historico-noop'] as const),
    queryFn: async ({ signal }) => {
      if (id == null) {
        throw new Error('useHistoricoRequisicion invocado sin id');
      }
      const { data } = await apiRequest<HistoricoEntryResponse[]>(
        `/api/v1/compras/requisiciones/${id}/historico`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}
