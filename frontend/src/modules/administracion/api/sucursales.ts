import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { adminKeys } from '@/modules/administracion/api/keys';
import type {
  ActualizarSucursalPayload,
  CrearSucursalCommand,
  SucursalResponse,
} from '@/modules/administracion/api/types';

/**
 * Mutations de Sucursales (F-Admin-PR2.3 backend). Reading sigue
 * pasando por el detalle de empresa (<c>GET /admin/empresas/{id}</c>)
 * o por el catálogo público (<c>GET /api/v1/catalogos/sucursales</c>).
 *
 * <para>Tras cualquier mutación, invalidamos la familia
 * <c>adminKeys.empresas()</c> para que el master-detail refresque la
 * tab "Sucursales".</para>
 */

export interface CrearSucursalArgs {
  empresaId: string;
  command: CrearSucursalCommand;
  idempotencyKey: string;
}

export function useCrearSucursal() {
  const queryClient = useQueryClient();
  return useMutation<SucursalResponse, Error, CrearSucursalArgs>({
    mutationFn: async ({ command, idempotencyKey }) => {
      const { data } = await apiRequest<SucursalResponse>(
        '/api/v1/admin/empresas/sucursales',
        { method: 'POST', body: command, idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.empresa(vars.empresaId),
      });
      // Las sucursales también las consume el catálogo
      // <c>/api/v1/catalogos/sucursales</c> (selector en RQ/OC). El
      // cache de ese catálogo vive bajo <c>['catalogos', ...]</c> —
      // lo invalidamos parcialmente con una clave laxa.
      queryClient.invalidateQueries({ queryKey: ['catalogos', 'sucursales'] });
    },
  });
}

export interface ActualizarSucursalArgs {
  empresaId: string;
  id: string;
  payload: ActualizarSucursalPayload;
  idempotencyKey: string;
}

export function useActualizarSucursal() {
  const queryClient = useQueryClient();
  return useMutation<SucursalResponse, Error, ActualizarSucursalArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<SucursalResponse>(
        `/api/v1/admin/empresas/sucursales/${id}`,
        { method: 'PATCH', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.empresa(vars.empresaId),
      });
      queryClient.invalidateQueries({ queryKey: ['catalogos', 'sucursales'] });
    },
  });
}

export interface DesactivarSucursalArgs {
  empresaId: string;
  id: string;
  idempotencyKey: string;
}

export function useDesactivarSucursal() {
  const queryClient = useQueryClient();
  return useMutation<SucursalResponse, Error, DesactivarSucursalArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      const { data } = await apiRequest<SucursalResponse>(
        `/api/v1/admin/empresas/sucursales/${id}/desactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.empresa(vars.empresaId),
      });
      queryClient.invalidateQueries({ queryKey: ['catalogos', 'sucursales'] });
    },
  });
}
