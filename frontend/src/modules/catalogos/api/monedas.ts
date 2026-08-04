import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  catalogosKeys,
  type ListarMonedasFiltros,
} from '@/modules/catalogos/api/keys';
import type {
  ActualizarMonedaPayload,
  CrearMonedaPayload,
  MonedaResponse,
} from '@/modules/catalogos/api/types';

/**
 * Hooks de TanStack Query del recurso Monedas (UF-Admin-PR5.1).
 *
 * <para>Endpoints:</para>
 * <list>
 *   <item><c>GET   /api/v1/catalogos/monedas?soloActivas</c> — lista
 *         (sin paginación, catálogo pequeño). Permiso
 *         <c>compartido.catalogos.leer</c>.</item>
 *   <item><c>POST  /api/v1/catalogos/monedas</c> — alta. Permiso
 *         <c>catalogos.monedas.gestionar</c>. Idempotency-Key.</item>
 *   <item><c>PATCH /api/v1/catalogos/monedas/{id}</c> — PATCH parcial
 *         (Codigo inmutable). Idempotency-Key. <b>"Desactivar" se hace
 *         con <c>Activa: false</c></b> — no hay endpoint dedicado.</item>
 * </list>
 */

// ─── Queries ────────────────────────────────────────────────────────

export function useMonedas(filtros: ListarMonedasFiltros = {}) {
  return useQuery({
    queryKey: catalogosKeys.monedasList(filtros),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.soloActivas === true) params.set('soloActivas', 'true');
      const qs = params.toString();
      const path = qs
        ? `/api/v1/catalogos/monedas?${qs}`
        : '/api/v1/catalogos/monedas';
      const { data } = await apiRequest<MonedaResponse[]>(path, { signal });
      return data;
    },
    staleTime: 30_000,
  });
}

/**
 * <c>useMoneda(id)</c> — el backend NO expone <c>GET /monedas/{id}</c>;
 * derivamos el detalle del listado completo con <c>select</c>. Mantiene
 * la API consistente con otros recursos (<c>useArticulo(id)</c>) sin
 * forzar al caller a re-implementar el lookup.
 */
export function useMoneda(id: string | null | undefined) {
  return useQuery({
    queryKey: catalogosKeys.monedasList({}),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<MonedaResponse[]>(
        '/api/v1/catalogos/monedas',
        { signal },
      );
      return data;
    },
    select: (items) =>
      id != null ? (items.find((m) => m.id === id) ?? null) : null,
    enabled: id != null,
    staleTime: 30_000,
  });
}

// ─── Mutations ──────────────────────────────────────────────────────

export interface CrearMonedaArgs {
  payload: CrearMonedaPayload;
  idempotencyKey: string;
}

export function useCrearMoneda() {
  const queryClient = useQueryClient();
  return useMutation<MonedaResponse, Error, CrearMonedaArgs>({
    mutationFn: async ({ payload, idempotencyKey }) => {
      const { data } = await apiRequest<MonedaResponse>(
        '/api/v1/catalogos/monedas',
        { method: 'POST', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: catalogosKeys.monedas() });
    },
  });
}

export interface ActualizarMonedaArgs {
  id: string;
  payload: ActualizarMonedaPayload;
  idempotencyKey: string;
}

export function useActualizarMoneda() {
  const queryClient = useQueryClient();
  return useMutation<MonedaResponse, Error, ActualizarMonedaArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<MonedaResponse>(
        `/api/v1/catalogos/monedas/${id}`,
        { method: 'PATCH', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: catalogosKeys.monedas() });
    },
  });
}

export interface DesactivarMonedaArgs {
  id: string;
  idempotencyKey: string;
}

/**
 * Helper de "desactivar moneda": atajo sobre el PATCH parcial con
 * <c>activa: false</c> (Monedas no tiene endpoint dedicado de
 * desactivar, a diferencia de los catálogos del Grupo 2). Espejo
 * <c>useReactivarMoneda</c> setea <c>activa: true</c>.
 */
export function useDesactivarMoneda() {
  const queryClient = useQueryClient();
  return useMutation<MonedaResponse, Error, DesactivarMonedaArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      const { data } = await apiRequest<MonedaResponse>(
        `/api/v1/catalogos/monedas/${id}`,
        {
          method: 'PATCH',
          body: { activa: false } satisfies ActualizarMonedaPayload,
          idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: catalogosKeys.monedas() });
    },
  });
}

export function useReactivarMoneda() {
  const queryClient = useQueryClient();
  return useMutation<MonedaResponse, Error, DesactivarMonedaArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      const { data } = await apiRequest<MonedaResponse>(
        `/api/v1/catalogos/monedas/${id}`,
        {
          method: 'PATCH',
          body: { activa: true } satisfies ActualizarMonedaPayload,
          idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: catalogosKeys.monedas() });
    },
  });
}
