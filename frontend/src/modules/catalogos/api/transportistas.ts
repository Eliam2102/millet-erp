import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { catalogosKeys } from '@/modules/catalogos/api/keys';
import type {
  ActualizarTransportistaPayload,
  CrearTransportistaPayload,
  TransportistaResponse,
} from '@/modules/catalogos/api/types';

/**
 * Hooks del catálogo Transportistas (UF-Admin-PR5.2). Email y Telefono
 * son opcionales; el PATCH backend acepta <c>limpiarEmail</c> /
 * <c>limpiarTelefono</c> al estilo Empresa cuando el usuario quiere
 * borrar el valor explícitamente.
 */

export function useTransportistasList() {
  return useQuery({
    queryKey: catalogosKeys.transportistas(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<TransportistaResponse[]>(
        '/api/v1/catalogos/transportistas',
        { signal },
      );
      return data;
    },
    staleTime: 30_000,
  });
}

export interface CrearTransportistaArgs {
  payload: CrearTransportistaPayload;
  idempotencyKey: string;
}

export function useCrearTransportista() {
  const queryClient = useQueryClient();
  return useMutation<TransportistaResponse, Error, CrearTransportistaArgs>({
    mutationFn: async ({ payload, idempotencyKey }) => {
      const { data } = await apiRequest<TransportistaResponse>(
        '/api/v1/catalogos/transportistas',
        { method: 'POST', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: catalogosKeys.transportistas(),
      });
    },
  });
}

export interface ActualizarTransportistaArgs {
  id: string;
  payload: ActualizarTransportistaPayload;
  idempotencyKey: string;
}

export function useActualizarTransportista() {
  const queryClient = useQueryClient();
  return useMutation<
    TransportistaResponse,
    Error,
    ActualizarTransportistaArgs
  >({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<TransportistaResponse>(
        `/api/v1/catalogos/transportistas/${id}`,
        { method: 'PATCH', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: catalogosKeys.transportistas(),
      });
    },
  });
}

export interface DesactivarTransportistaArgs {
  id: string;
  idempotencyKey: string;
}

export function useDesactivarTransportista() {
  const queryClient = useQueryClient();
  return useMutation<
    TransportistaResponse,
    Error,
    DesactivarTransportistaArgs
  >({
    mutationFn: async ({ id, idempotencyKey }) => {
      const { data } = await apiRequest<TransportistaResponse>(
        `/api/v1/catalogos/transportistas/${id}/desactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: catalogosKeys.transportistas(),
      });
    },
  });
}
