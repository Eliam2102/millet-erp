import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { catalogosKeys } from '@/modules/catalogos/api/keys';
import type {
  ActualizarUsoPrincipalPayload,
  CrearUsoPrincipalPayload,
  UsoPrincipalResponse,
} from '@/modules/catalogos/api/types';

/**
 * Hooks del catálogo Usos Principales (UF-Admin-PR5.2). El backend
 * actualmente reusa el permiso grueso <c>compartido.catalogos.administrar</c>
 * para mutación (no hay <c>catalogos.usos-principales.gestionar</c>
 * todavía); el frontend usa el mismo para guardar el guard de cada
 * card. Si el backend agrega el granular después, basta con cambiar el
 * permiso en <c>admin.ts</c> + en cada ruta.
 */

export function useUsosPrincipalesList() {
  return useQuery({
    queryKey: catalogosKeys.usosPrincipales(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<UsoPrincipalResponse[]>(
        '/api/v1/catalogos/usos-principales',
        { signal },
      );
      return data;
    },
    staleTime: 30_000,
  });
}

export interface CrearUsoPrincipalArgs {
  payload: CrearUsoPrincipalPayload;
  idempotencyKey: string;
}

export function useCrearUsoPrincipal() {
  const queryClient = useQueryClient();
  return useMutation<UsoPrincipalResponse, Error, CrearUsoPrincipalArgs>({
    mutationFn: async ({ payload, idempotencyKey }) => {
      const { data } = await apiRequest<UsoPrincipalResponse>(
        '/api/v1/catalogos/usos-principales',
        { method: 'POST', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: catalogosKeys.usosPrincipales(),
      });
    },
  });
}

export interface ActualizarUsoPrincipalArgs {
  id: string;
  payload: ActualizarUsoPrincipalPayload;
  idempotencyKey: string;
}

export function useActualizarUsoPrincipal() {
  const queryClient = useQueryClient();
  return useMutation<UsoPrincipalResponse, Error, ActualizarUsoPrincipalArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<UsoPrincipalResponse>(
        `/api/v1/catalogos/usos-principales/${id}`,
        { method: 'PATCH', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: catalogosKeys.usosPrincipales(),
      });
    },
  });
}

export interface DesactivarUsoPrincipalArgs {
  id: string;
  idempotencyKey: string;
}

export function useDesactivarUsoPrincipal() {
  const queryClient = useQueryClient();
  return useMutation<UsoPrincipalResponse, Error, DesactivarUsoPrincipalArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      const { data } = await apiRequest<UsoPrincipalResponse>(
        `/api/v1/catalogos/usos-principales/${id}/desactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: catalogosKeys.usosPrincipales(),
      });
    },
  });
}
