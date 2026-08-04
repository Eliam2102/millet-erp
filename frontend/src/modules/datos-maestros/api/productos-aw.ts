import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  datosMaestrosKeys,
  type ListarProductosAwFiltros,
} from '@/modules/datos-maestros/api/keys';
import type {
  ActualizarProductoAwPayload,
  CrearProductoAwPayload,
  CrearProductoAwResponse,
  ListarProductosAwResponse,
  ProductoAwDetalle,
} from '@/modules/datos-maestros/api/types';

/**
 * Hooks de TanStack Query del recurso Productos A+W (ADR-0048). Análogo
 * a <see cref="useCliente"/>; misma convención de invalidación.
 *
 * <para>Endpoints consumidos (todos bajo el permiso granular
 * <c>datos_maestros.productos-aw.gestionar</c>):</para>
 * <list>
 *   <item><c>GET    /api/v1/datos-maestros/productos-aw</c> — list con
 *         filtros (referencia, descripcion, origen, estatus,
 *         fiscalesIncompletos).</item>
 *   <item><c>GET    /api/v1/datos-maestros/productos-aw/{id}</c> — detalle.</item>
 *   <item><c>POST   /api/v1/datos-maestros/productos-aw</c> — alta manual.</item>
 *   <item><c>PATCH  /api/v1/datos-maestros/productos-aw/{id}</c> — patch parcial.</item>
 *   <item><c>DELETE /api/v1/datos-maestros/productos-aw/{id}</c> — soft delete.</item>
 * </list>
 */

// ─── Queries ────────────────────────────────────────────────────────

export function useProductosAw(filtros: ListarProductosAwFiltros = {}) {
  return useQuery({
    queryKey: datosMaestrosKeys.productosAwList(filtros),
    queryFn: async ({ signal }) => {
      const path = buildListarProductosAwPath(filtros);
      const { data } = await apiRequest<ListarProductosAwResponse>(path, {
        signal,
      });
      return data;
    },
    staleTime: 30_000,
  });
}

export function useProductoAw(id: string | null | undefined) {
  return useQuery({
    queryKey:
      id != null
        ? datosMaestrosKeys.productoAw(id)
        : (['datos-maestros', 'noop'] as const),
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useProductoAw invocado sin id');
      const { data } = await apiRequest<ProductoAwDetalle>(
        `/api/v1/datos-maestros/productos-aw/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

// ─── Mutations ──────────────────────────────────────────────────────

export interface CrearProductoAwArgs {
  payload: CrearProductoAwPayload;
  idempotencyKey: string;
}

export function useCrearProductoAw() {
  const queryClient = useQueryClient();
  return useMutation<CrearProductoAwResponse, Error, CrearProductoAwArgs>({
    mutationFn: async ({ payload, idempotencyKey }) => {
      const { data } = await apiRequest<CrearProductoAwResponse>(
        '/api/v1/datos-maestros/productos-aw',
        { method: 'POST', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.productosAw(),
      });
    },
  });
}

export interface ActualizarProductoAwArgs {
  id: string;
  payload: ActualizarProductoAwPayload;
  idempotencyKey: string;
}

export function useActualizarProductoAw() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, ActualizarProductoAwArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      await apiRequest<void>(`/api/v1/datos-maestros/productos-aw/${id}`, {
        method: 'PATCH',
        body: payload,
        idempotencyKey,
      });
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.productoAw(vars.id),
      });
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.productosAw(),
      });
    },
  });
}

export interface DesactivarProductoAwArgs {
  id: string;
  idempotencyKey: string;
}

export function useDesactivarProductoAw() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, DesactivarProductoAwArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      await apiRequest<void>(`/api/v1/datos-maestros/productos-aw/${id}`, {
        method: 'DELETE',
        idempotencyKey,
      });
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.productoAw(vars.id),
      });
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.productosAw(),
      });
    },
  });
}

// ─── Helpers ────────────────────────────────────────────────────────

function buildListarProductosAwPath(
  filtros: ListarProductosAwFiltros,
): string {
  const params = new URLSearchParams();
  if (filtros.referencia != null && filtros.referencia.length > 0)
    params.set('referencia', filtros.referencia);
  if (filtros.descripcion != null && filtros.descripcion.length > 0)
    params.set('descripcion', filtros.descripcion);
  if (filtros.origen != null) params.set('origen', String(filtros.origen));
  if (filtros.estatus != null) params.set('estatus', String(filtros.estatus));
  if (filtros.fiscalesIncompletos != null)
    params.set('fiscalesIncompletos', String(filtros.fiscalesIncompletos));
  if (filtros.offset != null) params.set('offset', String(filtros.offset));
  if (filtros.limit != null) params.set('limit', String(filtros.limit));
  const query = params.toString();
  return query
    ? `/api/v1/datos-maestros/productos-aw?${query}`
    : '/api/v1/datos-maestros/productos-aw';
}
