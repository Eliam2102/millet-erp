import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, esApiError } from '@/lib/api';
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
  AwProductosResumen,
  ProductoAwSincronizacionEstado,
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
  /** Versión del detalle con que se armó el form (If-Match, ADR-0012). */
  version: number;
  idempotencyKey: string;
}

export function useActualizarProductoAw() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, ActualizarProductoAwArgs>({
    mutationFn: async ({ id, payload, version, idempotencyKey }) => {
      await apiRequest<void>(`/api/v1/datos-maestros/productos-aw/${id}`, {
        method: 'PATCH',
        body: payload,
        idempotencyKey,
        ifMatch: String(version),
      });
    },
    // También tras un 409: recarga el detalle para que el form parta de lo vigente.
    onSettled: (_data, _error, vars) => {
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

// ─── Sincronización A+W (F1-ADM-07; permiso `productos-aw.gestionar`) ──

const SYNC_BASE = '/api/v1/datos-maestros/productos-aw/sincronizacion';

/** Estado de origen del producto; el `etag` es la versión para If-Match. */
export function useProductoAwSincronizacion(id: string | null | undefined) {
  return useQuery({
    queryKey:
      id != null
        ? datosMaestrosKeys.productoAwSync(id)
        : (['datos-maestros', 'noop'] as const),
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useProductoAwSincronizacion sin id');
      const { data } = await apiRequest<ProductoAwSincronizacionEstado>(
        `/api/v1/datos-maestros/productos-aw/${id}/sincronizacion`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

function invalidarProductos(queryClient: ReturnType<typeof useQueryClient>) {
  return queryClient.invalidateQueries({
    queryKey: datosMaestrosKeys.productosAw(),
  });
}

/** Barrido síncrono; responde el resumen real (200). */
export function useSincronizarProductosAw() {
  const queryClient = useQueryClient();
  return useMutation<AwProductosResumen, Error, void>({
    mutationFn: async () => {
      const { data } = await apiRequest<AwProductosResumen>(SYNC_BASE, {
        method: 'POST',
        // Key fresca por acción (ADR-0020).
        idempotencyKey: crypto.randomUUID(),
      });
      return data;
    },
    onSuccess: () => invalidarProductos(queryClient),
  });
}

/**
 * Relee una referencia. Con `version` manda If-Match: si el producto
 * cambió, el backend responde 409 y NO sobrescribe.
 */
export function useReintentarProductoAw() {
  const queryClient = useQueryClient();
  return useMutation<
    AwProductosResumen,
    Error,
    { referencia: string; version?: number | null }
  >({
    mutationFn: async ({ referencia, version }) => {
      const { data } = await apiRequest<AwProductosResumen>(
        `${SYNC_BASE}/${encodeURIComponent(referencia)}`,
        {
          method: 'POST',
          idempotencyKey: crypto.randomUUID(),
          ifMatch: version != null ? String(version) : undefined,
        },
      );
      return data;
    },
    // También tras un 409: relee versión y estado para que el siguiente intento parta de lo vigente.
    onSettled: () => invalidarProductos(queryClient),
  });
}

/** Mensaje en español para los errores de sincronización de productos. */
export function mensajeErrorSincronizacionProductos(error: unknown): string {
  if (!esApiError(error)) return 'No se pudo completar la operación.';
  switch (error.status) {
    case 409:
      return 'El producto cambió o es manual; no se sobrescribió. Se recargó el estado: revisa y vuelve a intentar.';
    case 404:
      return 'La referencia ya no existe en A+W; no se modificó el producto.';
    case 503:
      return 'La sincronización con A+W no está disponible (origen deshabilitado o sin configurar).';
    case 403:
      return 'No tienes permiso para sincronizar productos A+W.';
    default:
      return error.problem.title;
  }
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
  if (filtros.tipo) params.set('tipo', filtros.tipo);
  if (filtros.offset != null) params.set('offset', String(filtros.offset));
  if (filtros.limit != null) params.set('limit', String(filtros.limit));
  const query = params.toString();
  return query
    ? `/api/v1/datos-maestros/productos-aw?${query}`
    : '/api/v1/datos-maestros/productos-aw';
}
