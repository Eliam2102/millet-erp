import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  almacenKeys,
  type ListarUbicacionesFiltros,
} from '@/features/almacen/api/keys';
import type {
  CrearUbicacionPayload,
  CrearUbicacionResponse,
  EditarUbicacionPayload,
  PagedResponse,
  UbicacionEstatusResponse,
  UbicacionListItem,
} from '@/features/almacen/api/types';

/**
 * Hooks de ubicaciones N4 (racks/pasillos, ADR-0047 PR C7.1). Lectura eager
 * (puebla el <c>UbicacionSelector</c> y la pantalla de gestión) + mutaciones
 * crear/editar/desactivar/reactivar contra <c>/api/v1/almacen/ubicaciones</c>.
 * Las cuatro mutaciones exigen <c>Idempotency-Key</c> (ADR-0020). Tras cada una
 * invalida <c>almacenKeys.ubicaciones()</c> para refrescar la bandeja sin
 * importar filtros activos. Molde <c>useReorden</c>.
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

export function useUbicaciones(filtros: ListarUbicacionesFiltros = {}) {
  return useQuery({
    queryKey: almacenKeys.ubicacionesList(filtros),
    queryFn: async ({ signal }) => {
      const query = buildQuery(filtros);
      const path = query
        ? `/api/v1/almacen/ubicaciones?${query}`
        : '/api/v1/almacen/ubicaciones';
      const { data } = await apiRequest<PagedResponse<UbicacionListItem>>(path, {
        signal,
      });
      return data;
    },
  });
}

export function useCrearUbicacion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CrearUbicacionPayload;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CrearUbicacionResponse>(
        '/api/v1/almacen/ubicaciones',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.ubicaciones() });
    },
  });
}

export function useEditarUbicacion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: EditarUbicacionPayload;
      idempotencyKey: string;
    }) => {
      await apiRequest<void>(
        `/api/v1/almacen/ubicaciones/${args.command.id}`,
        {
          method: 'PATCH',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.ubicaciones() });
    },
  });
}

export function useDesactivarUbicacion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { id: string; idempotencyKey: string }) => {
      const { data } = await apiRequest<UbicacionEstatusResponse>(
        `/api/v1/almacen/ubicaciones/${args.id}/desactivar`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.ubicaciones() });
    },
  });
}

export function useReactivarUbicacion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { id: string; idempotencyKey: string }) => {
      const { data } = await apiRequest<UbicacionEstatusResponse>(
        `/api/v1/almacen/ubicaciones/${args.id}/reactivar`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.ubicaciones() });
    },
  });
}
