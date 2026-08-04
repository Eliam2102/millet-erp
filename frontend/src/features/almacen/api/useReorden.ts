import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  almacenKeys,
  type ListarReordenFiltros,
} from '@/features/almacen/api/keys';
import type {
  ConfiguracionReordenListItem,
  ConfiguracionReordenResponse,
  CrearConfiguracionReordenPayload,
  EditarConfiguracionReordenPayload,
  PagedResponse,
} from '@/features/almacen/api/types';

/**
 * Hooks del recurso "reabasto" (código = reorden, ADR-0047 PR5.A/5.F).
 * Bandeja paginada con filtros + mutaciones crear/editar/desactivar contra
 * <c>/api/v1/almacen/reorden</c>. Las TRES mutaciones exigen
 * <c>Idempotency-Key</c> (ADR-0020, a diferencia del catálogo de almacenes).
 * Tras cada mutación invalida <c>almacenKeys.reorden()</c> para refrescar la
 * bandeja sin importar filtros activos.
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

export function useReordenList(filtros: ListarReordenFiltros = {}) {
  return useQuery({
    queryKey: almacenKeys.reordenList(filtros),
    queryFn: async ({ signal }) => {
      const query = buildQuery(filtros);
      const path = query
        ? `/api/v1/almacen/reorden?${query}`
        : '/api/v1/almacen/reorden';
      const { data } = await apiRequest<
        PagedResponse<ConfiguracionReordenListItem>
      >(path, { signal });
      return data;
    },
  });
}

export function useCrearReorden() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CrearConfiguracionReordenPayload;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<ConfiguracionReordenResponse>(
        '/api/v1/almacen/reorden',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.reorden() });
    },
  });
}

export function useEditarReorden() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: EditarConfiguracionReordenPayload;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<ConfiguracionReordenResponse>(
        `/api/v1/almacen/reorden/${args.command.id}`,
        {
          method: 'PATCH',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_, args) => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.reorden() });
      queryClient.invalidateQueries({
        queryKey: almacenKeys.reordenById(args.command.id),
      });
    },
  });
}

export function useDesactivarReorden() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { id: string; idempotencyKey: string }) => {
      const { data } = await apiRequest<ConfiguracionReordenResponse>(
        `/api/v1/almacen/reorden/${args.id}/desactivar`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.reorden() });
    },
  });
}
