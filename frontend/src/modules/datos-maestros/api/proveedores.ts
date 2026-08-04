import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  datosMaestrosKeys,
  type ListarProveedoresFiltros,
} from '@/modules/datos-maestros/api/keys';
import type {
  ActualizarProveedorPayload,
  CrearProveedorPayload,
  CrearProveedorResponse,
  ListarProveedoresResponse,
  ProveedorDetalle,
} from '@/modules/datos-maestros/api/types';

/**
 * Hooks de TanStack Query del recurso Proveedores.
 *
 * <para>Endpoints consumidos:</para>
 * <list>
 *   <item><c>GET    /api/v1/datos-maestros/proveedores</c> — list
 *         enriquecida con filtros (rfc, razonSocial, tipoPersona,
 *         estatus). Permiso <c>datos_maestros.proveedores.gestionar</c>.</item>
 *   <item><c>GET    /api/v1/datos-maestros/proveedores/{id}</c> — detalle.</item>
 *   <item><c>POST   /api/v1/catalogos/proveedores</c> — alta legacy B.5.
 *         Permiso <c>compartido.catalogos.administrar</c>.</item>
 *   <item><c>PATCH  /api/v1/catalogos/proveedores/{id}</c> — patch parcial.</item>
 *   <item><c>DELETE /api/v1/catalogos/proveedores/{id}</c> — soft delete.</item>
 * </list>
 *
 * <para>Convención de invalidación: cualquier mutación invalida
 * <c>datosMaestrosKeys.proveedores()</c> (familia entera) para
 * refrescar lista y detalle a la vez — costo bajo, evita
 * inconsistencias temporales.</para>
 */

// ─── Queries ────────────────────────────────────────────────────────

export function useProveedores(filtros: ListarProveedoresFiltros = {}) {
  return useQuery({
    queryKey: datosMaestrosKeys.proveedoresList(filtros),
    queryFn: async ({ signal }) => {
      const path = buildListarProveedoresPath(filtros);
      const { data } = await apiRequest<ListarProveedoresResponse>(path, {
        signal,
      });
      return data;
    },
    staleTime: 30_000,
  });
}

export function useProveedor(id: string | null | undefined) {
  return useQuery({
    queryKey:
      id != null
        ? datosMaestrosKeys.proveedor(id)
        : (['datos-maestros', 'noop'] as const),
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useProveedor invocado sin id');
      const { data } = await apiRequest<ProveedorDetalle>(
        `/api/v1/datos-maestros/proveedores/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

// ─── Mutations ──────────────────────────────────────────────────────

export interface CrearProveedorArgs {
  payload: CrearProveedorPayload;
  idempotencyKey: string;
}

export function useCrearProveedor() {
  const queryClient = useQueryClient();
  return useMutation<CrearProveedorResponse, Error, CrearProveedorArgs>({
    mutationFn: async ({ payload, idempotencyKey }) => {
      const { data } = await apiRequest<CrearProveedorResponse>(
        '/api/v1/catalogos/proveedores',
        { method: 'POST', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.proveedores(),
      });
    },
  });
}

export interface ActualizarProveedorArgs {
  id: string;
  payload: ActualizarProveedorPayload;
  idempotencyKey: string;
}

export function useActualizarProveedor() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, ActualizarProveedorArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      await apiRequest<void>(`/api/v1/catalogos/proveedores/${id}`, {
        method: 'PATCH',
        body: payload,
        idempotencyKey,
      });
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.proveedor(vars.id),
      });
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.proveedores(),
      });
    },
  });
}

export interface DesactivarProveedorArgs {
  id: string;
  idempotencyKey: string;
}

export function useDesactivarProveedor() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, DesactivarProveedorArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      await apiRequest<void>(`/api/v1/catalogos/proveedores/${id}`, {
        method: 'DELETE',
        idempotencyKey,
      });
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.proveedor(vars.id),
      });
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.proveedores(),
      });
    },
  });
}

// ─── Helpers ────────────────────────────────────────────────────────

function buildListarProveedoresPath(
  filtros: ListarProveedoresFiltros,
): string {
  const params = new URLSearchParams();
  if (filtros.rfc != null && filtros.rfc.length > 0)
    params.set('rfc', filtros.rfc);
  if (filtros.razonSocial != null && filtros.razonSocial.length > 0)
    params.set('razonSocial', filtros.razonSocial);
  if (filtros.tipoPersona != null)
    params.set('tipoPersona', String(filtros.tipoPersona));
  if (filtros.estatus != null) params.set('estatus', String(filtros.estatus));
  if (filtros.offset != null) params.set('offset', String(filtros.offset));
  if (filtros.limit != null) params.set('limit', String(filtros.limit));
  const query = params.toString();
  return query
    ? `/api/v1/datos-maestros/proveedores?${query}`
    : '/api/v1/datos-maestros/proveedores';
}
