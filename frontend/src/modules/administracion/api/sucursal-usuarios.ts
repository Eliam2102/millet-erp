import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { adminKeys } from '@/modules/administracion/api/keys';
import type {
  ListarUsuariosPorSucursalResponse,
  UsuarioSucursalResponse,
} from '@/modules/administracion/api/types';

/**
 * Hooks de la asignación N:M Usuario ↔ Sucursal (F1-ADM-01 Fase 2/3
 * backend, módulo Identidad). Análogo exacto de
 * <c>sucursal-departamentos.ts</c> / <c>sucursal-puestos.ts</c> —
 * vive en <c>administracion/api</c> porque el endpoint cuelga del
 * mismo namespace <c>/admin/empresas/sucursales/...</c>.
 *
 * <para>Endpoint base:
 * <c>/api/v1/admin/empresas/sucursales/{sucursalId}/usuarios</c>.
 * GET requiere <c>compartido.catalogos.leer</c>; las mutaciones
 * requieren <c>admin.sucursales.usuarios-gestionar</c>. Las tres
 * mutaciones invalidan la lista de la sucursal afectada.</para>
 */

const BASE = '/api/v1/admin/empresas/sucursales';

/**
 * Lista los usuarios asignados a una sucursal con su estatus en esa
 * sucursal. <c>sucursalId === null</c> deja la query deshabilitada.
 */
export function useUsuariosDeSucursal(sucursalId: string | null) {
  return useQuery({
    queryKey:
      sucursalId != null
        ? adminKeys.sucursalUsuariosList(sucursalId)
        : adminKeys.sucursalUsuarios(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ListarUsuariosPorSucursalResponse>(
        `${BASE}/${sucursalId}/usuarios`,
        { signal },
      );
      return data;
    },
    enabled: sucursalId != null,
  });
}

export interface AsignarUsuarioASucursalArgs {
  sucursalId: string;
  usuarioId: string;
  idempotencyKey: string;
}

export function useAsignarUsuarioASucursal() {
  const queryClient = useQueryClient();
  return useMutation<UsuarioSucursalResponse, Error, AsignarUsuarioASucursalArgs>({
    mutationFn: async ({ sucursalId, usuarioId, idempotencyKey }) => {
      const { data } = await apiRequest<UsuarioSucursalResponse>(
        `${BASE}/${sucursalId}/usuarios/${usuarioId}`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.sucursalUsuariosList(vars.sucursalId),
      });
    },
  });
}

export interface DesactivarAsignacionUsuarioSucursalArgs {
  sucursalId: string;
  usuarioId: string;
  idempotencyKey: string;
}

export function useDesactivarAsignacionUsuarioSucursal() {
  const queryClient = useQueryClient();
  return useMutation<
    UsuarioSucursalResponse,
    Error,
    DesactivarAsignacionUsuarioSucursalArgs
  >({
    mutationFn: async ({ sucursalId, usuarioId, idempotencyKey }) => {
      const { data } = await apiRequest<UsuarioSucursalResponse>(
        `${BASE}/${sucursalId}/usuarios/${usuarioId}/desactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.sucursalUsuariosList(vars.sucursalId),
      });
    },
  });
}

export interface ReactivarAsignacionUsuarioSucursalArgs {
  sucursalId: string;
  usuarioId: string;
  idempotencyKey: string;
}

export function useReactivarAsignacionUsuarioSucursal() {
  const queryClient = useQueryClient();
  return useMutation<
    UsuarioSucursalResponse,
    Error,
    ReactivarAsignacionUsuarioSucursalArgs
  >({
    mutationFn: async ({ sucursalId, usuarioId, idempotencyKey }) => {
      const { data } = await apiRequest<UsuarioSucursalResponse>(
        `${BASE}/${sucursalId}/usuarios/${usuarioId}/reactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.sucursalUsuariosList(vars.sucursalId),
      });
    },
  });
}
