import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  datosMaestrosKeys,
  type ListarArticulosFiltros,
} from '@/modules/datos-maestros/api/keys';
import type {
  ActualizarArticuloPayload,
  ArticuloDetalle,
  CrearArticuloPayload,
  CrearArticuloResponse,
  ListarArticulosResponse,
} from '@/modules/datos-maestros/api/types';

/**
 * Hooks de TanStack Query del recurso Artículos. Análogo a
 * <see cref="useProveedor"/>; misma convención de invalidación.
 *
 * <para>Endpoints consumidos:</para>
 * <list>
 *   <item><c>GET    /api/v1/datos-maestros/articulos</c> — list
 *         enriquecida (codigo, descripcion, naturaleza,
 *         unidadMedidaDefault, estatus). Permiso
 *         <c>datos_maestros.articulos.gestionar</c>.</item>
 *   <item><c>GET    /api/v1/datos-maestros/articulos/{id}</c> — detalle.</item>
 *   <item><c>POST   /api/v1/catalogos/articulos</c> — alta legacy B.5.</item>
 *   <item><c>PATCH  /api/v1/catalogos/articulos/{id}</c> — patch parcial.</item>
 *   <item><c>DELETE /api/v1/catalogos/articulos/{id}</c> — soft delete.</item>
 * </list>
 */

// ─── Queries ────────────────────────────────────────────────────────

export function useArticulos(filtros: ListarArticulosFiltros = {}) {
  return useQuery({
    queryKey: datosMaestrosKeys.articulosList(filtros),
    queryFn: async ({ signal }) => {
      const path = buildListarArticulosPath(filtros);
      const { data } = await apiRequest<ListarArticulosResponse>(path, {
        signal,
      });
      return data;
    },
    staleTime: 30_000,
  });
}

export function useArticulo(id: string | null | undefined) {
  return useQuery({
    queryKey:
      id != null
        ? datosMaestrosKeys.articulo(id)
        : (['datos-maestros', 'noop'] as const),
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useArticulo invocado sin id');
      const { data } = await apiRequest<ArticuloDetalle>(
        `/api/v1/datos-maestros/articulos/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

// ─── Mutations ──────────────────────────────────────────────────────

export interface CrearArticuloArgs {
  payload: CrearArticuloPayload;
  idempotencyKey: string;
}

export function useCrearArticulo() {
  const queryClient = useQueryClient();
  return useMutation<CrearArticuloResponse, Error, CrearArticuloArgs>({
    mutationFn: async ({ payload, idempotencyKey }) => {
      const { data } = await apiRequest<CrearArticuloResponse>(
        '/api/v1/catalogos/articulos',
        { method: 'POST', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.articulos(),
      });
    },
  });
}

export interface ActualizarArticuloArgs {
  id: string;
  payload: ActualizarArticuloPayload;
  idempotencyKey: string;
}

export function useActualizarArticulo() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, ActualizarArticuloArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      await apiRequest<void>(`/api/v1/catalogos/articulos/${id}`, {
        method: 'PATCH',
        body: payload,
        idempotencyKey,
      });
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.articulo(vars.id),
      });
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.articulos(),
      });
    },
  });
}

export interface DesactivarArticuloArgs {
  id: string;
  idempotencyKey: string;
}

export function useDesactivarArticulo() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, DesactivarArticuloArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      await apiRequest<void>(`/api/v1/catalogos/articulos/${id}`, {
        method: 'DELETE',
        idempotencyKey,
      });
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.articulo(vars.id),
      });
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.articulos(),
      });
    },
  });
}

// ─── Helpers ────────────────────────────────────────────────────────

function buildListarArticulosPath(filtros: ListarArticulosFiltros): string {
  const params = new URLSearchParams();
  if (filtros.codigo != null && filtros.codigo.length > 0)
    params.set('codigo', filtros.codigo);
  if (filtros.descripcion != null && filtros.descripcion.length > 0)
    params.set('descripcion', filtros.descripcion);
  if (filtros.naturaleza != null)
    params.set('naturaleza', String(filtros.naturaleza));
  if (
    filtros.unidadMedidaDefault != null &&
    filtros.unidadMedidaDefault.length > 0
  )
    params.set('unidadMedidaDefault', filtros.unidadMedidaDefault);
  if (filtros.estatus != null) params.set('estatus', String(filtros.estatus));
  if (filtros.offset != null) params.set('offset', String(filtros.offset));
  if (filtros.limit != null) params.set('limit', String(filtros.limit));
  const query = params.toString();
  return query
    ? `/api/v1/datos-maestros/articulos?${query}`
    : '/api/v1/datos-maestros/articulos';
}
