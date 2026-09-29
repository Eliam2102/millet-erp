import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, esApiError } from '@/lib/api';
import {
  datosMaestrosKeys,
  type ListarClientesFiltros,
  type ListarEjecucionesSyncFiltros,
} from '@/modules/datos-maestros/api/keys';
import type {
  ActualizarClientePayload,
  ClienteDetalle,
  CrearClientePayload,
  CrearClienteResponse,
  EjecucionSyncAceptada,
  EjecucionSyncDetalle,
  ListarClientesResponse,
  ListarEjecucionesSyncResponse,
} from '@/modules/datos-maestros/api/types';

/**
 * Hooks de TanStack Query del recurso Clientes (ADR-0048). Análogo a
 * <see cref="useProveedor"/>; misma convención de invalidación.
 *
 * <para>Endpoints consumidos (todos bajo el permiso granular
 * <c>datos_maestros.clientes.gestionar</c>):</para>
 * <list>
 *   <item><c>GET    /api/v1/datos-maestros/clientes</c> — list con
 *         filtros (rfc, razonSocial, origen, estatus,
 *         fiscalesIncompletos).</item>
 *   <item><c>GET    /api/v1/datos-maestros/clientes/{id}</c> — detalle.</item>
 *   <item><c>POST   /api/v1/datos-maestros/clientes</c> — alta manual.</item>
 *   <item><c>PATCH  /api/v1/datos-maestros/clientes/{id}</c> — patch parcial.</item>
 *   <item><c>DELETE /api/v1/datos-maestros/clientes/{id}</c> — soft delete.</item>
 * </list>
 */

// ─── Queries ────────────────────────────────────────────────────────

export function useClientes(filtros: ListarClientesFiltros = {}) {
  return useQuery({
    queryKey: datosMaestrosKeys.clientesList(filtros),
    queryFn: async ({ signal }) => {
      const path = buildListarClientesPath(filtros);
      const { data } = await apiRequest<ListarClientesResponse>(path, {
        signal,
      });
      return data;
    },
    staleTime: 30_000,
  });
}

export function useCliente(id: string | null | undefined) {
  return useQuery({
    queryKey:
      id != null
        ? datosMaestrosKeys.cliente(id)
        : (['datos-maestros', 'noop'] as const),
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useCliente invocado sin id');
      const { data } = await apiRequest<ClienteDetalle>(
        `/api/v1/datos-maestros/clientes/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

// ─── Mutations ──────────────────────────────────────────────────────

export interface CrearClienteArgs {
  payload: CrearClientePayload;
  idempotencyKey: string;
}

export function useCrearCliente() {
  const queryClient = useQueryClient();
  return useMutation<CrearClienteResponse, Error, CrearClienteArgs>({
    mutationFn: async ({ payload, idempotencyKey }) => {
      const { data } = await apiRequest<CrearClienteResponse>(
        '/api/v1/datos-maestros/clientes',
        { method: 'POST', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.clientes(),
      });
    },
  });
}

export interface ActualizarClienteArgs {
  id: string;
  payload: ActualizarClientePayload;
  idempotencyKey: string;
}

export function useActualizarCliente() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, ActualizarClienteArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      await apiRequest<void>(`/api/v1/datos-maestros/clientes/${id}`, {
        method: 'PATCH',
        body: payload,
        idempotencyKey,
      });
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.cliente(vars.id),
      });
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.clientes(),
      });
    },
  });
}

export interface DesactivarClienteArgs {
  id: string;
  idempotencyKey: string;
}

export function useDesactivarCliente() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, DesactivarClienteArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      await apiRequest<void>(`/api/v1/datos-maestros/clientes/${id}`, {
        method: 'DELETE',
        idempotencyKey,
      });
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.cliente(vars.id),
      });
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.clientes(),
      });
    },
  });
}

// ─── Sincronización A+W (permiso `sincronizar`) ─────────────────────

const SYNC_BASE = '/api/v1/datos-maestros/clientes/sincronizacion';
const ESTADOS_VIVOS = ['Pendiente', 'EnCurso'];

export function useEjecucionesSync(
  filtros: ListarEjecucionesSyncFiltros = {},
  enabled = true,
) {
  return useQuery({
    queryKey: datosMaestrosKeys.clientesSyncList(filtros),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.estado != null) params.set('estado', filtros.estado);
      if (filtros.offset != null) params.set('offset', String(filtros.offset));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const { data } = await apiRequest<ListarEjecucionesSyncResponse>(
        `${SYNC_BASE}/ejecuciones${qs ? `?${qs}` : ''}`,
        { signal },
      );
      return data;
    },
    enabled,
    // Refresca la lista mientras alguna ejecución siga viva.
    refetchInterval: (q) =>
      q.state.data?.items.some((e) => ESTADOS_VIVOS.includes(e.estado))
        ? 3000
        : false,
  });
}

export function useEjecucionSync(id: string | null | undefined) {
  return useQuery({
    queryKey:
      id != null
        ? datosMaestrosKeys.clienteSync(id)
        : (['datos-maestros', 'noop'] as const),
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useEjecucionSync invocado sin id');
      const { data } = await apiRequest<EjecucionSyncDetalle>(
        `${SYNC_BASE}/ejecuciones/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    refetchInterval: (q) =>
      q.state.data != null && ESTADOS_VIVOS.includes(q.state.data.estado)
        ? 3000
        : false,
  });
}

export function useIniciarSincronizacion() {
  const queryClient = useQueryClient();
  return useMutation<EjecucionSyncAceptada, Error, void>({
    mutationFn: async () => {
      const { data } = await apiRequest<EjecucionSyncAceptada>(
        `${SYNC_BASE}/ejecuciones`,
        {
          method: 'POST',
          body: { tipo: 'Barrido' },
          // Key fresca por acción (ADR-0020).
          idempotencyKey: crypto.randomUUID(),
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.clientesSync(),
      });
    },
  });
}

export function useReintentarCliente() {
  const queryClient = useQueryClient();
  return useMutation<EjecucionSyncAceptada, Error, { referencia: string }>({
    mutationFn: async ({ referencia }) => {
      const { data } = await apiRequest<EjecucionSyncAceptada>(
        `${SYNC_BASE}/reintentos`,
        {
          method: 'POST',
          body: { referencia },
          idempotencyKey: crypto.randomUUID(),
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.clientes(),
      });
    },
  });
}

// ─── Helpers ────────────────────────────────────────────────────────

function buildListarClientesPath(filtros: ListarClientesFiltros): string {
  const params = new URLSearchParams();
  if (filtros.rfc != null && filtros.rfc.length > 0)
    params.set('rfc', filtros.rfc);
  if (filtros.razonSocial != null && filtros.razonSocial.length > 0)
    params.set('razonSocial', filtros.razonSocial);
  if (filtros.origen != null) params.set('origen', String(filtros.origen));
  if (filtros.estatus != null) params.set('estatus', String(filtros.estatus));
  if (filtros.fiscalesIncompletos != null)
    params.set('fiscalesIncompletos', String(filtros.fiscalesIncompletos));
  if (filtros.referenciaExterna)
    params.set('referenciaExterna', filtros.referenciaExterna);
  if (filtros.resultadoSincronizacion)
    params.set('resultadoSincronizacion', filtros.resultadoSincronizacion);
  if (filtros.offset != null) params.set('offset', String(filtros.offset));
  if (filtros.limit != null) params.set('limit', String(filtros.limit));
  const query = params.toString();
  return query
    ? `/api/v1/datos-maestros/clientes?${query}`
    : '/api/v1/datos-maestros/clientes';
}

/** Mensaje en español para los ProblemDetails `AW_CLIENTES_*` de la sincronización. */
export function mensajeErrorSincronizacion(error: unknown): string {
  if (!esApiError(error)) return 'No se pudo completar la operación.';
  switch (error.status) {
    case 409:
      return 'Ya hay una sincronización en curso. Espera a que termine.';
    case 503:
      return 'La sincronización con A+W no está disponible (lectura o aplicación deshabilitada, u origen sin configurar).';
    case 422:
      return 'La referencia del cliente no es válida para sincronizar.';
    case 403:
      return 'No tienes permiso para sincronizar clientes.';
    default:
      return error.problem.title;
  }
}
