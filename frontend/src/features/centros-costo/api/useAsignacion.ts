import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { centrosCostoKeys } from './keys';
import type {
  ArbolAsignacionResponse,
  MarcarAlcanceRequest,
  MarcarAlcanceResponse,
} from './types';

/**
 * Hooks del árbol de asignación (FE-PR3). El árbol es FULL-TREE (una query
 * devuelve los 5 niveles con el tri-estado ya calculado por el backend).
 * Reconciliación REFETCH-ONLY: tras marcar se invalida el árbol del
 * usuario y se re-fetchea — la UI nunca re-deriva tri-estado (el backend
 * es la fuente de verdad, 05 §4.2).
 */

const BASE = '/api/v1/centros-costo/asignaciones';

/** GET del árbol de asignación de un usuario (tri-estado + resumen + esAlcanceTotal). */
export function useArbolAsignacion(usuarioId: string | null) {
  return useQuery({
    queryKey: centrosCostoKeys.arbolAsignacion(usuarioId ?? 'none'),
    enabled: usuarioId !== null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ArbolAsignacionResponse>(
        `${BASE}/${usuarioId}/arbol`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * POST del marcado: el backend expande a las máquinas vivas bajo el nodo y
 * guarda/borra hojas. Idempotency-Key fresca por submit; al terminar
 * invalida el árbol del usuario → refetch (repinta con el tri-estado real).
 */
export function useMarcarAlcance(usuarioId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: MarcarAlcanceRequest) => {
      const { data } = await apiRequest<MarcarAlcanceResponse>(
        `${BASE}/${usuarioId}/marcar`,
        { method: 'POST', body, idempotencyKey: crypto.randomUUID() },
      );
      return data;
    },
    onSuccess: () =>
      void queryClient.invalidateQueries({
        queryKey: centrosCostoKeys.arbolAsignacion(usuarioId),
      }),
  });
}
