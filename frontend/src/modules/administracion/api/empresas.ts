import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  adminKeys,
  type ListarEmpresasFiltros,
} from '@/modules/administracion/api/keys';
import type {
  ActualizarEmpresaPayload,
  CrearEmpresaCommand,
  EmpresaDetalleResponse,
  EmpresaResponse,
  ListarEmpresasResponse,
} from '@/modules/administracion/api/types';

/**
 * Hooks de TanStack Query del recurso Empresas (F-Admin-PR2.3 backend).
 *
 * <para>Endpoints consumidos:</para>
 * <list>
 *   <item><c>GET    /api/v1/admin/empresas</c> — lista paginada.</item>
 *   <item><c>GET    /api/v1/admin/empresas/{id}</c> — detalle con
 *         sucursales + departamentos del sistema.</item>
 *   <item><c>POST   /api/v1/admin/empresas</c> — alta (Idempotency-Key).</item>
 *   <item><c>PATCH  /api/v1/admin/empresas/{id}</c> — patch parcial.</item>
 *   <item><c>POST   /api/v1/admin/empresas/{id}/desactivar</c>.</item>
 * </list>
 *
 * <para>Convención de invalidación: cualquier mutación invalida
 * <c>adminKeys.empresas()</c> (familia entera) para refrescar lista y
 * detalle a la vez — el costo es bajo y elimina inconsistencias
 * temporales.</para>
 */

// ─── Queries ────────────────────────────────────────────────────────

export function useEmpresas(filtros: ListarEmpresasFiltros = {}) {
  return useQuery({
    queryKey: adminKeys.empresasList(filtros),
    queryFn: async ({ signal }) => {
      const path = buildListarPath(filtros);
      const { data } = await apiRequest<ListarEmpresasResponse>(path, {
        signal,
      });
      return data;
    },
  });
}

export function useEmpresa(id: string | null | undefined) {
  return useQuery({
    queryKey:
      id != null ? adminKeys.empresa(id) : (['admin', 'noop'] as const),
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useEmpresa invocado sin id');
      const { data } = await apiRequest<EmpresaDetalleResponse>(
        `/api/v1/admin/empresas/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

// ─── Mutations ──────────────────────────────────────────────────────

export interface CrearEmpresaArgs {
  command: CrearEmpresaCommand;
  idempotencyKey: string;
}

export function useCrearEmpresa() {
  const queryClient = useQueryClient();
  return useMutation<EmpresaResponse, Error, CrearEmpresaArgs>({
    mutationFn: async ({ command, idempotencyKey }) => {
      const { data } = await apiRequest<EmpresaResponse>(
        '/api/v1/admin/empresas',
        { method: 'POST', body: command, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminKeys.empresas() });
    },
  });
}

export interface ActualizarEmpresaArgs {
  id: string;
  payload: ActualizarEmpresaPayload;
  idempotencyKey: string;
}

export function useActualizarEmpresa() {
  const queryClient = useQueryClient();
  return useMutation<EmpresaResponse, Error, ActualizarEmpresaArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<EmpresaResponse>(
        `/api/v1/admin/empresas/${id}`,
        { method: 'PATCH', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({ queryKey: adminKeys.empresa(vars.id) });
      queryClient.invalidateQueries({
        queryKey: adminKeys.empresas(),
      });
    },
  });
}

export interface DesactivarEmpresaArgs {
  id: string;
  idempotencyKey: string;
}

export function useDesactivarEmpresa() {
  const queryClient = useQueryClient();
  return useMutation<EmpresaResponse, Error, DesactivarEmpresaArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      const { data } = await apiRequest<EmpresaResponse>(
        `/api/v1/admin/empresas/${id}/desactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({ queryKey: adminKeys.empresa(vars.id) });
      queryClient.invalidateQueries({ queryKey: adminKeys.empresas() });
    },
  });
}

// ─── Helpers ────────────────────────────────────────────────────────

function buildListarPath(filtros: ListarEmpresasFiltros): string {
  const params = new URLSearchParams();
  if (filtros.soloActivas != null)
    params.set('soloActivas', String(filtros.soloActivas));
  if (filtros.offset != null) params.set('offset', String(filtros.offset));
  if (filtros.limit != null) params.set('limit', String(filtros.limit));
  const query = params.toString();
  return query
    ? `/api/v1/admin/empresas?${query}`
    : '/api/v1/admin/empresas';
}
