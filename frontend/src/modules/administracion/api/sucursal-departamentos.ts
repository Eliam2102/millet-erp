import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { adminKeys } from '@/modules/administracion/api/keys';
import type {
  ListarDepartamentosDeSucursalResponse,
  SucursalDepartamentoResponse,
} from '@/modules/administracion/api/types';

/**
 * Hooks de la asignación N:M Sucursal ↔ Departamento (PR-A1 backend).
 *
 * <para>Endpoint base:
 * <c>/api/v1/admin/empresas/sucursales/{sucursalId}/departamentos</c>.
 * Permiso: <c>admin.sucursales.departamentos-gestionar</c>. Las cuatro
 * mutaciones invalidan la lista de la sucursal afectada para que el
 * Sheet de admin y el <see cref="DepartamentoSelectorPorSucursal"/> de
 * captura de RQs refresquen en simultáneo.</para>
 */

const BASE = '/api/v1/admin/empresas/sucursales';

/**
 * Lista los departamentos asignados a una sucursal con su estatus
 * en esa sucursal. <c>sucursalId === null</c> deja la query
 * deshabilitada — útil cuando el caller aún no ha resuelto la sucursal
 * (p.ej. en la captura de RQ antes de seleccionarla).
 */
export function useDepartamentosDeSucursal(sucursalId: string | null) {
  return useQuery({
    queryKey:
      sucursalId != null
        ? adminKeys.sucursalDepartamentosList(sucursalId)
        : adminKeys.sucursalDepartamentos(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ListarDepartamentosDeSucursalResponse>(
        `${BASE}/${sucursalId}/departamentos`,
        { signal },
      );
      return data;
    },
    enabled: sucursalId != null,
  });
}

export interface AsignarDepartamentoASucursalArgs {
  sucursalId: string;
  departamentoId: string;
  idempotencyKey: string;
}

export function useAsignarDepartamentoASucursal() {
  const queryClient = useQueryClient();
  return useMutation<
    SucursalDepartamentoResponse,
    Error,
    AsignarDepartamentoASucursalArgs
  >({
    mutationFn: async ({ sucursalId, departamentoId, idempotencyKey }) => {
      const { data } = await apiRequest<SucursalDepartamentoResponse>(
        `${BASE}/${sucursalId}/departamentos/${departamentoId}`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.sucursalDepartamentosList(vars.sucursalId),
      });
    },
  });
}

export interface DesactivarAsignacionSucursalDepartamentoArgs {
  sucursalId: string;
  departamentoId: string;
  idempotencyKey: string;
}

export function useDesactivarAsignacionSucursalDepartamento() {
  const queryClient = useQueryClient();
  return useMutation<
    SucursalDepartamentoResponse,
    Error,
    DesactivarAsignacionSucursalDepartamentoArgs
  >({
    mutationFn: async ({ sucursalId, departamentoId, idempotencyKey }) => {
      const { data } = await apiRequest<SucursalDepartamentoResponse>(
        `${BASE}/${sucursalId}/departamentos/${departamentoId}/desactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.sucursalDepartamentosList(vars.sucursalId),
      });
    },
  });
}

export interface ReactivarAsignacionSucursalDepartamentoArgs {
  sucursalId: string;
  departamentoId: string;
  idempotencyKey: string;
}

export function useReactivarAsignacionSucursalDepartamento() {
  const queryClient = useQueryClient();
  return useMutation<
    SucursalDepartamentoResponse,
    Error,
    ReactivarAsignacionSucursalDepartamentoArgs
  >({
    mutationFn: async ({ sucursalId, departamentoId, idempotencyKey }) => {
      const { data } = await apiRequest<SucursalDepartamentoResponse>(
        `${BASE}/${sucursalId}/departamentos/${departamentoId}/reactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.sucursalDepartamentosList(vars.sucursalId),
      });
    },
  });
}
