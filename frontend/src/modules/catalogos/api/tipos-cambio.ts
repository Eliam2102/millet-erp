import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  catalogosKeys,
  type ListarTiposCambioFiltros,
} from '@/modules/catalogos/api/keys';
import type {
  ListarTiposCambioResponse,
  RegistrarTipoCambioPayload,
  TipoCambioResponse,
} from '@/modules/catalogos/api/types';

/**
 * Hooks del histórico de tipos de cambio por moneda (UF-Admin-PR5.1).
 *
 * <para>Endpoints:</para>
 * <list>
 *   <item><c>GET  /api/v1/catalogos/monedas/{id}/tipos-cambio?offset&amp;limit</c>
 *         — paginado por fecha desc, tope 200, default 50.</item>
 *   <item><c>POST /api/v1/catalogos/monedas/{id}/tipos-cambio</c> —
 *         registrar valor. Permiso
 *         <c>catalogos.tipos-cambio.gestionar</c>. Idempotency-Key.</item>
 * </list>
 */

export function useTiposCambio(
  monedaId: string | null | undefined,
  filtros: ListarTiposCambioFiltros = {},
) {
  return useQuery({
    queryKey:
      monedaId != null
        ? catalogosKeys.tiposCambioList(monedaId, filtros)
        : (['catalogos-admin', 'noop'] as const),
    queryFn: async ({ signal }) => {
      if (monedaId == null) {
        throw new Error('useTiposCambio invocado sin monedaId');
      }
      const params = new URLSearchParams();
      if (filtros.offset != null) params.set('offset', String(filtros.offset));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const path = qs
        ? `/api/v1/catalogos/monedas/${monedaId}/tipos-cambio?${qs}`
        : `/api/v1/catalogos/monedas/${monedaId}/tipos-cambio`;
      const { data } = await apiRequest<ListarTiposCambioResponse>(path, {
        signal,
      });
      return data;
    },
    enabled: monedaId != null,
    staleTime: 30_000,
  });
}

export interface RegistrarTipoCambioArgs {
  monedaId: string;
  payload: RegistrarTipoCambioPayload;
  idempotencyKey: string;
}

export function useRegistrarTipoCambio() {
  const queryClient = useQueryClient();
  return useMutation<TipoCambioResponse, Error, RegistrarTipoCambioArgs>({
    mutationFn: async ({ monedaId, payload, idempotencyKey }) => {
      const { data } = await apiRequest<TipoCambioResponse>(
        `/api/v1/catalogos/monedas/${monedaId}/tipos-cambio`,
        { method: 'POST', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: catalogosKeys.tiposCambio(vars.monedaId),
      });
    },
  });
}
