import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { catalogosKeys } from '@/modules/catalogos/api/keys';
import type {
  ActualizarIncotermPayload,
  CrearIncotermPayload,
  IncotermResponse,
} from '@/modules/catalogos/api/types';

/**
 * Hooks del catálogo Incoterms (UF-Admin-PR5.2). Codigo (2-4 chars
 * uppercase) es inmutable; el PATCH solo permite renombrar.
 */

export function useIncotermsList() {
  return useQuery({
    queryKey: catalogosKeys.incoterms(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<IncotermResponse[]>(
        '/api/v1/catalogos/incoterms',
        { signal },
      );
      return data;
    },
    staleTime: 30_000,
  });
}

export interface CrearIncotermArgs {
  payload: CrearIncotermPayload;
  idempotencyKey: string;
}

export function useCrearIncoterm() {
  const queryClient = useQueryClient();
  return useMutation<IncotermResponse, Error, CrearIncotermArgs>({
    mutationFn: async ({ payload, idempotencyKey }) => {
      const { data } = await apiRequest<IncotermResponse>(
        '/api/v1/catalogos/incoterms',
        { method: 'POST', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: catalogosKeys.incoterms() });
    },
  });
}

export interface ActualizarIncotermArgs {
  id: string;
  payload: ActualizarIncotermPayload;
  idempotencyKey: string;
}

export function useActualizarIncoterm() {
  const queryClient = useQueryClient();
  return useMutation<IncotermResponse, Error, ActualizarIncotermArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<IncotermResponse>(
        `/api/v1/catalogos/incoterms/${id}`,
        { method: 'PATCH', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: catalogosKeys.incoterms() });
    },
  });
}

export interface DesactivarIncotermArgs {
  id: string;
  idempotencyKey: string;
}

export function useDesactivarIncoterm() {
  const queryClient = useQueryClient();
  return useMutation<IncotermResponse, Error, DesactivarIncotermArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      const { data } = await apiRequest<IncotermResponse>(
        `/api/v1/catalogos/incoterms/${id}/desactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: catalogosKeys.incoterms() });
    },
  });
}
