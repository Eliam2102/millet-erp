import {
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { centrosCostoKeys, type RecursoCatalogo } from './keys';
import type { EstatusCatalogo, PagedResponse } from './types';

/**
 * Hooks CRUD del catálogo (CECO-FE-PR2). Concurrencia por
 * <b>If-Match/ETag</b> (ADR-0012, semántica Cajas — el backend exige el
 * header: 428 si falta, 409 en mismatch), con el molde
 * <c>useMutacionConIfMatch</c> de <c>facturacion/api/useCajas.ts</c>:
 * el GET de detalle cachea <c>{ data, etag }</c> y las mutaciones leen
 * el etag cacheado, con fallback al <c>version</c> del DTO si un proxy
 * filtró el header.
 */

const BASE = '/api/v1/centros-costo';

interface DetalleCacheado<T> {
  data: T;
  etag?: string;
}

/** GET detalle por id — captura el ETag para las mutaciones. */
export function useDetalleCatalogo<T extends { version: number }>(
  recurso: RecursoCatalogo,
  id: string | null,
) {
  return useQuery({
    queryKey: centrosCostoKeys.detalle(recurso, id ?? 'none'),
    enabled: id !== null,
    queryFn: async ({ signal }): Promise<DetalleCacheado<T>> => {
      const { data, etag } = await apiRequest<T>(`${BASE}/${recurso}/${id}`, {
        signal,
      });
      return { data, etag };
    },
  });
}

/** GET lista plana paginada con filtros por estatus/q (+ filtros extra por recurso). */
export function useListaCatalogo<TItem>(
  recurso: RecursoCatalogo,
  filtros: {
    estatus?: EstatusCatalogo;
    q?: string;
    offset?: number;
    limit?: number;
    dim1Id?: string;
    dim2Id?: string;
  } = {},
  opciones: { enabled?: boolean } = {},
) {
  return useQuery({
    queryKey: centrosCostoKeys.lista(recurso, filtros),
    enabled: opciones.enabled ?? true,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      for (const [key, value] of Object.entries(filtros)) {
        if (value !== undefined && value !== null && value !== '') {
          params.set(key, String(value));
        }
      }
      const query = params.toString();
      const { data } = await apiRequest<PagedResponse<TItem>>(
        query ? `${BASE}/${recurso}/?${query}` : `${BASE}/${recurso}/`,
        { signal },
      );
      return data;
    },
  });
}

function useInvalidarModulo() {
  const queryClient = useQueryClient();
  return () =>
    void queryClient.invalidateQueries({ queryKey: centrosCostoKeys.all });
}

/** POST crear — Idempotency-Key fresca por submit. */
export function useCrearCatalogo<TBody, TResp = unknown>(
  recurso: RecursoCatalogo,
) {
  const invalidar = useInvalidarModulo();
  return useMutation({
    mutationFn: async (body: TBody) => {
      const { data } = await apiRequest<TResp>(`${BASE}/${recurso}/`, {
        method: 'POST',
        body,
        idempotencyKey: crypto.randomUUID(),
      });
      return data;
    },
    onSuccess: invalidar,
  });
}

/**
 * Resuelve el If-Match desde el detalle cacheado (etag → fallback al
 * version del DTO). Las mutaciones lo exigen: sin él, el backend
 * responde 428 y el handler de conflicto pide recargar.
 */
function resolverIfMatch(
  cached: DetalleCacheado<{ version: number }> | undefined,
): string | undefined {
  return cached?.etag ?? (cached ? String(cached.data.version) : undefined);
}

/** PATCH editar — If-Match del detalle cacheado. */
export function useEditarCatalogo<TBody, TResp = unknown>(
  recurso: RecursoCatalogo,
) {
  const queryClient = useQueryClient();
  const invalidar = useInvalidarModulo();
  return useMutation({
    mutationFn: async (args: { id: string; body: TBody }) => {
      const cached = queryClient.getQueryData<
        DetalleCacheado<{ version: number }>
      >(centrosCostoKeys.detalle(recurso, args.id));
      const { data } = await apiRequest<TResp>(
        `${BASE}/${recurso}/${args.id}`,
        {
          method: 'PATCH',
          body: args.body,
          idempotencyKey: crypto.randomUUID(),
          ifMatch: resolverIfMatch(cached),
        },
      );
      return data;
    },
    onSuccess: invalidar,
  });
}

/**
 * POST /{id}/desactivar | /{id}/reactivar — If-Match del detalle
 * cacheado. Desactivar Dim1/Dim2 CASCADA (el response trae los conteos
 * reales, ADR-0049); reactivar nunca cascada.
 */
export function useCambiarEstatusCatalogo<TResp = unknown>(
  recurso: RecursoCatalogo,
) {
  const queryClient = useQueryClient();
  const invalidar = useInvalidarModulo();
  return useMutation({
    mutationFn: async (args: { id: string; accion: 'desactivar' | 'reactivar' }) => {
      const cached = queryClient.getQueryData<
        DetalleCacheado<{ version: number }>
      >(centrosCostoKeys.detalle(recurso, args.id));
      const { data } = await apiRequest<TResp>(
        `${BASE}/${recurso}/${args.id}/${args.accion}`,
        {
          method: 'POST',
          idempotencyKey: crypto.randomUUID(),
          ifMatch: resolverIfMatch(cached),
        },
      );
      return data;
    },
    onSuccess: invalidar,
  });
}
