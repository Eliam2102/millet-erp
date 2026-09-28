import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { adminKeys } from '@/modules/administracion/api/keys';
import type {
  ListarPuestosDeSucursalResponse,
  SucursalPuestoResponse,
} from '@/modules/administracion/api/types';

/**
 * Hooks de la asignación N:M Sucursal ↔ Puesto ↔ Departamento
 * (F1-ADM-01 Fase 2/3 backend + Parte E "un puesto en varios
 * departamentos de la sucursal"). Análogo de
 * <c>sucursal-departamentos.ts</c>, con la unicidad ahora en
 * <c>(sucursal, puesto, departamento)</c>: el listado trae UNA fila
 * por asignación (no por puesto).
 *
 * <para>Endpoint base:
 * <c>/api/v1/admin/empresas/sucursales/{sucursalId}/puestos</c>.
 * GET requiere <c>compartido.catalogos.leer</c>; las mutaciones
 * requieren <c>admin.sucursales.puestos-gestionar</c>. Las cuatro
 * mutaciones invalidan TODAS las variantes cacheadas de la lista de
 * la sucursal afectada (con y sin filtro por departamento).</para>
 */

const BASE = '/api/v1/admin/empresas/sucursales';

/**
 * Lista los puestos asignados a una sucursal con su estatus en esa
 * sucursal — una fila por asignación (puesto+departamento).
 * <c>sucursalId === null</c> deja la query deshabilitada.
 * <c>departamentoId</c> filtra del lado del servidor las asignaciones
 * de un solo departamento (p.ej. <c>PuestoSelector</c> dependiente).
 */
export function usePuestosDeSucursal(
  sucursalId: string | null,
  departamentoId?: string | null,
) {
  return useQuery({
    queryKey:
      sucursalId != null
        ? adminKeys.sucursalPuestosList(sucursalId, departamentoId)
        : adminKeys.sucursalPuestos(),
    queryFn: async ({ signal }) => {
      const query = departamentoId
        ? `?departamentoId=${encodeURIComponent(departamentoId)}`
        : '';
      const { data } = await apiRequest<ListarPuestosDeSucursalResponse>(
        `${BASE}/${sucursalId}/puestos${query}`,
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
  /**
   * Excepción opcional de rol sugerido para esta asignación puntual
   * (sucursal+puesto+departamento). <c>null</c>/ausente = sin
   * excepción, hereda el rol sugerido del puesto (01-04: siempre solo
   * sugerencia editable).
   */
  rolSugeridoId?: string | null;
  idempotencyKey: string;
}

export function useAsignarPuestoASucursal() {
  const queryClient = useQueryClient();
  return useMutation<SucursalPuestoResponse, Error, AsignarPuestoASucursalArgs>({
    mutationFn: async ({
      sucursalId,
      puestoId,
      departamentoId,
      rolSugeridoId,
      idempotencyKey,
    }) => {
      const { data } = await apiRequest<SucursalPuestoResponse>(
        `${BASE}/${sucursalId}/puestos/${puestoId}`,
        {
          method: 'POST',
          idempotencyKey,
          body:
            rolSugeridoId !== undefined
              ? { departamentoId, rolSugeridoId }
              : { departamentoId },
        },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.sucursalPuestosSucursal(vars.sucursalId),
      });
    },
  });
}

export interface DesactivarAsignacionSucursalPuestoArgs {
  sucursalId: string;
  puestoId: string;
  departamentoId: string;
  idempotencyKey: string;
}

export function useDesactivarAsignacionSucursalPuesto() {
  const queryClient = useQueryClient();
  return useMutation<
    SucursalPuestoResponse,
    Error,
    DesactivarAsignacionSucursalPuestoArgs
  >({
    mutationFn: async ({ sucursalId, puestoId, departamentoId, idempotencyKey }) => {
      const { data } = await apiRequest<SucursalPuestoResponse>(
        `${BASE}/${sucursalId}/puestos/${puestoId}/departamentos/${departamentoId}/desactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.sucursalPuestosSucursal(vars.sucursalId),
      });
    },
  });
}

export interface ReactivarAsignacionSucursalPuestoArgs {
  sucursalId: string;
  puestoId: string;
  departamentoId: string;
  idempotencyKey: string;
}

export function useReactivarAsignacionSucursalPuesto() {
  const queryClient = useQueryClient();
  return useMutation<
    SucursalPuestoResponse,
    Error,
    ReactivarAsignacionSucursalPuestoArgs
  >({
    mutationFn: async ({ sucursalId, puestoId, departamentoId, idempotencyKey }) => {
      const { data } = await apiRequest<SucursalPuestoResponse>(
        `${BASE}/${sucursalId}/puestos/${puestoId}/departamentos/${departamentoId}/reactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.sucursalPuestosSucursal(vars.sucursalId),
      });
    },
  });
}

export interface ActualizarRolSugeridoAsignacionArgs {
  sucursalId: string;
  puestoId: string;
  departamentoId: string;
  /** <c>null</c> = limpiar la excepción y volver a heredar del puesto. */
  rolSugeridoId: string | null;
  idempotencyKey: string;
}

/**
 * PATCH del rol sugerido propio de una asignación puntual
 * (sucursal+puesto+departamento) — la excepción opcional sobre el rol
 * sugerido del puesto (decisión del owner 2026-09-24). Sigue siendo
 * solo sugerencia editable en el wizard de alta (01-04): esto NO
 * asigna rol a nadie, solo cambia qué se precarga como sugerencia.
 */
export function useActualizarRolSugeridoAsignacion() {
  const queryClient = useQueryClient();
  return useMutation<
    SucursalPuestoResponse,
    Error,
    ActualizarRolSugeridoAsignacionArgs
  >({
    mutationFn: async ({
      sucursalId,
      puestoId,
      departamentoId,
      rolSugeridoId,
      idempotencyKey,
    }) => {
      const { data } = await apiRequest<SucursalPuestoResponse>(
        `${BASE}/${sucursalId}/puestos/${puestoId}/departamentos/${departamentoId}`,
        { method: 'PATCH', idempotencyKey, body: { rolSugeridoId } },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.sucursalPuestosSucursal(vars.sucursalId),
      });
    },
  });
}
