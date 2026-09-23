import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { adminKeys } from '@/modules/administracion/api/keys';
import type {
  ListarPuestosDeSucursalResponse,
  SucursalPuestoResponse,
} from '@/modules/administracion/api/types';

/**
 * Hooks de la asignación N:M Sucursal ↔ Puesto (F1-ADM-01 Fase 2/3
 * backend). Análogo exacto de <c>sucursal-departamentos.ts</c>.
 *
 * <para>Endpoint base:
 * <c>/api/v1/admin/empresas/sucursales/{sucursalId}/puestos</c>.
 * GET requiere <c>compartido.catalogos.leer</c>; las mutaciones
 * requieren <c>admin.sucursales.puestos-gestionar</c>. Las tres
 * mutaciones invalidan la lista de la sucursal afectada.</para>
 */

const BASE = '/api/v1/admin/empresas/sucursales';

/**
 * Lista los puestos asignados a una sucursal con su estatus en esa
 * sucursal. <c>sucursalId === null</c> deja la query deshabilitada.
 */
export function usePuestosDeSucursal(sucursalId: string | null) {
  return useQuery({
    queryKey:
      sucursalId != null
        ? adminKeys.sucursalPuestosList(sucursalId)
        : adminKeys.sucursalPuestos(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ListarPuestosDeSucursalResponse>(
        `${BASE}/${sucursalId}/puestos`,
        { signal },
      );
      return data;
    },
    enabled: sucursalId != null,
  });
}

export interface AsignarPuestoASucursalArgs {
  sucursalId: string;
  puestoId: string;
  departamentoId: string;
  idempotencyKey: string;
}

export function useAsignarPuestoASucursal() {
  const queryClient = useQueryClient();
  return useMutation<SucursalPuestoResponse, Error, AsignarPuestoASucursalArgs>({
    mutationFn: async ({ sucursalId, puestoId, departamentoId, idempotencyKey }) => {
      const { data } = await apiRequest<SucursalPuestoResponse>(
        `${BASE}/${sucursalId}/puestos/${puestoId}`,
        {
          method: 'POST',
          idempotencyKey,
          body: { departamentoId },
        },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.sucursalPuestosList(vars.sucursalId),
      });
    },
  });
}

export interface DesactivarAsignacionSucursalPuestoArgs {
  sucursalId: string;
  puestoId: string;
  idempotencyKey: string;
}

export function useDesactivarAsignacionSucursalPuesto() {
  const queryClient = useQueryClient();
  return useMutation<
    SucursalPuestoResponse,
    Error,
    DesactivarAsignacionSucursalPuestoArgs
  >({
    mutationFn: async ({ sucursalId, puestoId, idempotencyKey }) => {
      const { data } = await apiRequest<SucursalPuestoResponse>(
        `${BASE}/${sucursalId}/puestos/${puestoId}/desactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.sucursalPuestosList(vars.sucursalId),
      });
    },
  });
}

export interface ReactivarAsignacionSucursalPuestoArgs {
  sucursalId: string;
  puestoId: string;
  idempotencyKey: string;
}

export function useReactivarAsignacionSucursalPuesto() {
  const queryClient = useQueryClient();
  return useMutation<
    SucursalPuestoResponse,
    Error,
    ReactivarAsignacionSucursalPuestoArgs
  >({
    mutationFn: async ({ sucursalId, puestoId, idempotencyKey }) => {
      const { data } = await apiRequest<SucursalPuestoResponse>(
        `${BASE}/${sucursalId}/puestos/${puestoId}/reactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.sucursalPuestosList(vars.sucursalId),
      });
    },
  });
}
