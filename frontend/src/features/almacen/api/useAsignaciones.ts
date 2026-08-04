import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  almacenKeys,
  type ListarAsignacionesFiltros,
} from '@/features/almacen/api/keys';
import type {
  AsignacionListItem,
  AsignacionResponse,
  AsignarArticuloAUbicacionPayload,
  PagedResponse,
} from '@/features/almacen/api/types';

/**
 * Hooks de "Ubicación de artículos" (asignación N4, ADR-0047 PR C). Bandeja
 * paginada + asignar/desasignar contra <c>/api/v1/almacen/asignaciones</c>. NO
 * hay editar (tras PR C la asignación es pura relación artículo↔ubicación; el
 * PATCH se eliminó). Las mutaciones exigen <c>Idempotency-Key</c> (ADR-0020),
 * que el caller genera FRESCO por submit (fix PR B); tras cada una invalida
 * <c>almacenKeys.asignaciones()</c>.
 */

function buildQuery(filtros: object): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(filtros)) {
    if (value !== undefined && value !== null && value !== '') {
      params.set(key, String(value));
    }
  }
  return params.toString();
}

export function useAsignacionesList(filtros: ListarAsignacionesFiltros = {}) {
  return useQuery({
    queryKey: almacenKeys.asignacionesList(filtros),
    queryFn: async ({ signal }) => {
      const query = buildQuery(filtros);
      const path = query
        ? `/api/v1/almacen/asignaciones?${query}`
        : '/api/v1/almacen/asignaciones';
      const { data } = await apiRequest<PagedResponse<AsignacionListItem>>(path, {
        signal,
      });
      return data;
    },
  });
}

export function useCrearAsignacion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: AsignarArticuloAUbicacionPayload;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<AsignacionResponse>(
        '/api/v1/almacen/asignaciones',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.asignaciones() });
    },
  });
}

export function useDesasignar() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { id: string; idempotencyKey: string }) => {
      const { data } = await apiRequest<AsignacionResponse>(
        `/api/v1/almacen/asignaciones/${args.id}/desasignar`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.asignaciones() });
    },
  });
}
