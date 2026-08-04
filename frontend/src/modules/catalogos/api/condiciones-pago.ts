import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { catalogosKeys } from '@/modules/catalogos/api/keys';
import type {
  ActualizarCondicionesPagoPayload,
  CondicionesPagoResponse,
  CrearCondicionesPagoPayload,
} from '@/modules/catalogos/api/types';

/**
 * Hooks del catálogo Condiciones de Pago (UF-Admin-PR5.2). Mismos
 * endpoints que <c>features/catalogos/api/hooks.ts</c> consume read-only
 * desde Compras; las mutaciones nuevas (POST/PATCH/desactivar) van
 * acá. Permiso <c>catalogos.condiciones-pago.gestionar</c> para
 * mutación; <c>compartido.catalogos.leer</c> para GET.
 *
 * <para>"Desactivar" es POST <c>/{id}/desactivar</c> dedicado (no PATCH
 * con flag — es como lo expone el backend Grupo 2).</para>
 */

export function useCondicionesPagoList() {
  return useQuery({
    queryKey: catalogosKeys.condicionesPago(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<CondicionesPagoResponse[]>(
        '/api/v1/catalogos/condiciones-pago',
        { signal },
      );
      return data;
    },
    staleTime: 30_000,
  });
}

export interface CrearCondicionesPagoArgs {
  payload: CrearCondicionesPagoPayload;
  idempotencyKey: string;
}

export function useCrearCondicionesPago() {
  const queryClient = useQueryClient();
  return useMutation<CondicionesPagoResponse, Error, CrearCondicionesPagoArgs>({
    mutationFn: async ({ payload, idempotencyKey }) => {
      const { data } = await apiRequest<CondicionesPagoResponse>(
        '/api/v1/catalogos/condiciones-pago',
        { method: 'POST', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: catalogosKeys.condicionesPago(),
      });
    },
  });
}

export interface ActualizarCondicionesPagoArgs {
  id: string;
  payload: ActualizarCondicionesPagoPayload;
  idempotencyKey: string;
}

export function useActualizarCondicionesPago() {
  const queryClient = useQueryClient();
  return useMutation<
    CondicionesPagoResponse,
    Error,
    ActualizarCondicionesPagoArgs
  >({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<CondicionesPagoResponse>(
        `/api/v1/catalogos/condiciones-pago/${id}`,
        { method: 'PATCH', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: catalogosKeys.condicionesPago(),
      });
    },
  });
}

export interface DesactivarCondicionesPagoArgs {
  id: string;
  idempotencyKey: string;
}

export function useDesactivarCondicionesPago() {
  const queryClient = useQueryClient();
  return useMutation<
    CondicionesPagoResponse,
    Error,
    DesactivarCondicionesPagoArgs
  >({
    mutationFn: async ({ id, idempotencyKey }) => {
      const { data } = await apiRequest<CondicionesPagoResponse>(
        `/api/v1/catalogos/condiciones-pago/${id}/desactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: catalogosKeys.condicionesPago(),
      });
    },
  });
}
