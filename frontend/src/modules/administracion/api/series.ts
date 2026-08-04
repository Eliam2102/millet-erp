import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  adminKeys,
  type ListarSeriesFiltros,
} from '@/modules/administracion/api/keys';
import type {
  ActualizarSeriePayload,
  CrearSerieCommand,
  ListarSeriesResponse,
  SerieDetalleResponse,
  SerieResponse,
} from '@/modules/administracion/api/types';

/**
 * Hooks de TanStack Query del recurso Series (F-Admin-PR6.1 backend).
 *
 * <para>Endpoints consumidos:</para>
 * <list>
 *   <item><c>GET    /api/v1/admin/series</c> — lista paginada con
 *         filtros opcionales empresaId/tipoDocumento.</item>
 *   <item><c>GET    /api/v1/admin/series/{id}</c> — detalle con
 *         <c>proximoFolioPreview</c> calculado por el backend.</item>
 *   <item><c>POST   /api/v1/admin/series</c> — alta (Idempotency-Key).</item>
 *   <item><c>PATCH  /api/v1/admin/series/{id}</c> — patch parcial
 *         (Idempotency-Key).</item>
 *   <item><c>POST   /api/v1/admin/series/{id}/desactivar</c>
 *         (Idempotency-Key, soft-delete idempotente).</item>
 * </list>
 *
 * <para>Convención de invalidación: cualquier mutación invalida
 * <c>adminKeys.series()</c> (familia entera) para refrescar lista y
 * detalle a la vez. El detalle también recalcula el preview del
 * próximo folio, así que las invalidaciones tienen que ser amplias.</para>
 */

const STALE = 30_000;

// ─── Queries ────────────────────────────────────────────────────────

export function useSeries(filtros: ListarSeriesFiltros = {}) {
  return useQuery({
    queryKey: adminKeys.seriesList(filtros),
    queryFn: async ({ signal }) => {
      const path = buildListarPath(filtros);
      const { data } = await apiRequest<ListarSeriesResponse>(path, {
        signal,
      });
      return data;
    },
    staleTime: STALE,
  });
}

export function useSerie(id: string | null | undefined) {
  return useQuery({
    queryKey:
      id != null ? adminKeys.serie(id) : (['admin', 'noop'] as const),
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useSerie invocado sin id');
      const { data } = await apiRequest<SerieDetalleResponse>(
        `/api/v1/admin/series/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

// ─── Mutations ──────────────────────────────────────────────────────

export interface CrearSerieArgs {
  command: CrearSerieCommand;
  idempotencyKey: string;
}

export function useCrearSerie() {
  const queryClient = useQueryClient();
  return useMutation<SerieResponse, Error, CrearSerieArgs>({
    mutationFn: async ({ command, idempotencyKey }) => {
      const { data } = await apiRequest<SerieResponse>(
        '/api/v1/admin/series',
        { method: 'POST', body: command, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminKeys.series() });
    },
  });
}

export interface ActualizarSerieArgs {
  id: string;
  payload: ActualizarSeriePayload;
  idempotencyKey: string;
}

export function useActualizarSerie() {
  const queryClient = useQueryClient();
  return useMutation<SerieResponse, Error, ActualizarSerieArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<SerieResponse>(
        `/api/v1/admin/series/${id}`,
        { method: 'PATCH', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({ queryKey: adminKeys.serie(vars.id) });
      queryClient.invalidateQueries({ queryKey: adminKeys.series() });
    },
  });
}

export interface DesactivarSerieArgs {
  id: string;
  idempotencyKey: string;
}

export function useDesactivarSerie() {
  const queryClient = useQueryClient();
  return useMutation<SerieResponse, Error, DesactivarSerieArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      const { data } = await apiRequest<SerieResponse>(
        `/api/v1/admin/series/${id}/desactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({ queryKey: adminKeys.serie(vars.id) });
      queryClient.invalidateQueries({ queryKey: adminKeys.series() });
    },
  });
}

// ─── Helpers ────────────────────────────────────────────────────────

function buildListarPath(filtros: ListarSeriesFiltros): string {
  const params = new URLSearchParams();
  if (filtros.empresaId != null && filtros.empresaId.length > 0) {
    params.set('empresaId', filtros.empresaId);
  }
  if (filtros.tipoDocumento != null) {
    params.set('tipoDocumento', String(filtros.tipoDocumento));
  }
  if (filtros.offset != null) params.set('offset', String(filtros.offset));
  if (filtros.limit != null) params.set('limit', String(filtros.limit));
  const query = params.toString();
  return query
    ? `/api/v1/admin/series?${query}`
    : '/api/v1/admin/series';
}
