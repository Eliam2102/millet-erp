import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, esApiError } from '@/lib/api';
import { F1A_QUERY_KEY } from '@/modules/datos-maestros/api/f1a';
import {
  datosMaestrosKeys,
  type ListarClientesFiltros,
  type ListarEjecucionesSyncFiltros,
} from '@/modules/datos-maestros/api/keys';
import type {
  ActualizarClientePayload,
  ClienteAwOrigenDetalle,
  ClienteDetalle,
  ClienteItem,
  CrearClientePayload,
  CrearClienteResponse,
  EjecucionSyncAceptada,
  EjecucionSyncDetalle,
  EjecucionSyncDetalleRespuesta,
  ListarClientesResponse,
  ListarEjecucionesSyncResponse,
} from '@/modules/datos-maestros/api/types';
import { EstatusCatalogo } from '@/modules/datos-maestros/api/types';

type EstatusClienteApi = EstatusCatalogo | keyof typeof EstatusCatalogo;
type ClienteApi<T extends { estatus: EstatusCatalogo }> = Omit<T, 'estatus'> & { estatus: EstatusClienteApi };
type ListarClientesApi = Omit<ListarClientesResponse, 'items'> & { items: ClienteApi<ClienteItem>[] };

/** El contrato HTTP de clientes usa texto; los controles compartidos usan el enum numérico. */
export function normalizarEstatusCliente(estatus: EstatusClienteApi): EstatusCatalogo {
  return typeof estatus === 'string' ? EstatusCatalogo[estatus] : estatus;
}

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
      const { data } = await apiRequest<ListarClientesApi>(path, {
        signal,
      });
      return { ...data, items: data.items.map(c => ({ ...c, estatus: normalizarEstatusCliente(c.estatus) })) };
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
      const { data } = await apiRequest<ClienteApi<ClienteDetalle>>(
        `/api/v1/datos-maestros/clientes/${id}`,
        { signal },
      );
      return { ...data, estatus: normalizarEstatusCliente(data.estatus) };
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
      // Un cambio de RFC puede cerrar o abrir huérfanos en el panel de ligados.
      queryClient.invalidateQueries({ queryKey: F1A_QUERY_KEY });
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

export function useReactivarCliente() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, DesactivarClienteArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      await apiRequest<void>(`/api/v1/datos-maestros/clientes/${id}/reactivar`, {
        method: 'POST',
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

/** Lectura en vivo de una referencia de A+W (solo al abrir el detalle de un error). */
export function useOrigenClienteAw(referencia: string, enabled: boolean) {
  return useQuery({
    queryKey: ['datos-maestros', 'clientes', 'origen-aw', referencia] as const,
    enabled,
    staleTime: 60_000,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<ClienteAwOrigenDetalle>(
          `${SYNC_BASE}/origen/${encodeURIComponent(referencia)}`,
          { signal },
        )
      ).data,
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
      const { data } = await apiRequest<EjecucionSyncDetalleRespuesta>(
        `${SYNC_BASE}/ejecuciones/${id}`,
        { signal },
      );
      return aplanarDetalle(data);
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
      queryClient.invalidateQueries({ queryKey: F1A_QUERY_KEY });
    },
  });
}

export function useReintentarCliente() {
  const queryClient = useQueryClient();
  return useMutation<EjecucionSyncDetalle, Error, { referencia: string }>({
    mutationFn: async ({ referencia }) => {
      const { data } = await apiRequest<EjecucionSyncDetalleRespuesta>(
        `${SYNC_BASE}/reintentos`,
        {
          method: 'POST',
          body: { referencia },
          idempotencyKey: crypto.randomUUID(),
        },
      );
      return aplanarDetalle(data);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: datosMaestrosKeys.clientes(),
      });
      queryClient.invalidateQueries({ queryKey: F1A_QUERY_KEY });
    },
  });
}

// ─── Helpers ────────────────────────────────────────────────────────

export function aplanarDetalle(r: EjecucionSyncDetalleRespuesta): EjecucionSyncDetalle {
  return { ...r.ejecucion, errores: r.errores, erroresTruncados: r.erroresTruncados };
}

function buildListarClientesPath(filtros: ListarClientesFiltros): string {
  const params = new URLSearchParams();
  if (filtros.sinClasificar != null) params.set('sinClasificar', String(filtros.sinClasificar));
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
      return 'La sincronización con A+W no está disponible ahora. Avisa a TI.';
    case 422:
      return 'La referencia del cliente no es válida para sincronizar.';
    case 403:
      return 'No tienes permiso para sincronizar clientes.';
    default:
      return error.problem.title;
  }
}
