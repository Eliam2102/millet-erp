import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  identidadKeys,
  type ListarUsuariosFiltros,
} from '@/modules/identidad/api/keys';
import type {
  ActualizarUsuarioPayload,
  AsignarRolPayload,
  CrearUsuarioCommand,
  ListarUsuariosResponse,
  UsuarioDetalleResponse,
  UsuarioEmpresaRolResponse,
  UsuarioResponse,
} from '@/modules/identidad/api/types';

/**
 * Hooks de TanStack Query del recurso Usuarios (F-Admin-PR4.2 backend).
 *
 * <para>Endpoints consumidos (todos bajo
 * <c>/api/v1/identidad/usuarios</c>):</para>
 * <list>
 *   <item><c>GET    /admin</c> — lista admin con <c>EntraOid</c>.</item>
 *   <item><c>GET    /{id}</c> — detalle con asignaciones expandidas.</item>
 *   <item><c>POST   /</c> — alta (Idempotency-Key).</item>
 *   <item><c>PATCH  /{id}</c> — patch parcial.</item>
 *   <item><c>POST   /{id}/desactivar</c> — soft-delete.</item>
 *   <item><c>POST   /{id}/reactivar</c>.</item>
 *   <item><c>POST   /{id}/asignaciones</c> — asignar rol×empresa.</item>
 *   <item><c>DELETE /asignaciones/{usuarioEmpresaRolId}</c> — revocar.</item>
 * </list>
 *
 * <para>Convención de invalidación: las mutaciones invalidan la
 * familia entera <c>identidadKeys.usuarios()</c> para refrescar lista
 * y detalle a la vez — costo bajo, elimina inconsistencias temporales.</para>
 */

// ─── Queries ────────────────────────────────────────────────────────

export function useUsuarios(filtros: ListarUsuariosFiltros = {}) {
  return useQuery({
    queryKey: identidadKeys.usuariosList(filtros),
    queryFn: async ({ signal }) => {
      const path = buildListarPath(filtros);
      const { data } = await apiRequest<ListarUsuariosResponse>(path, {
        signal,
      });
      return data;
    },
  });
}

export function useUsuario(id: string | null | undefined) {
  return useQuery({
    queryKey:
      id != null ? identidadKeys.usuario(id) : (['identidad', 'noop'] as const),
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useUsuario invocado sin id');
      const { data } = await apiRequest<UsuarioDetalleResponse>(
        `/api/v1/identidad/usuarios/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

// ─── Mutations ──────────────────────────────────────────────────────

export interface CrearUsuarioArgs {
  command: CrearUsuarioCommand;
  idempotencyKey: string;
}

export function useCrearUsuario() {
  const queryClient = useQueryClient();
  return useMutation<UsuarioResponse, Error, CrearUsuarioArgs>({
    mutationFn: async ({ command, idempotencyKey }) => {
      const { data } = await apiRequest<UsuarioResponse>(
        '/api/v1/identidad/usuarios',
        { method: 'POST', body: command, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: identidadKeys.usuarios() });
    },
  });
}

export interface ActualizarUsuarioArgs {
  id: string;
  payload: ActualizarUsuarioPayload;
  idempotencyKey: string;
}

export function useActualizarUsuario() {
  const queryClient = useQueryClient();
  return useMutation<UsuarioResponse, Error, ActualizarUsuarioArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<UsuarioResponse>(
        `/api/v1/identidad/usuarios/${id}`,
        { method: 'PATCH', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: identidadKeys.usuario(vars.id),
      });
      queryClient.invalidateQueries({ queryKey: identidadKeys.usuarios() });
    },
  });
}

export interface DesactivarUsuarioArgs {
  id: string;
  idempotencyKey: string;
}

export function useDesactivarUsuario() {
  const queryClient = useQueryClient();
  return useMutation<UsuarioResponse, Error, DesactivarUsuarioArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      const { data } = await apiRequest<UsuarioResponse>(
        `/api/v1/identidad/usuarios/${id}/desactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: identidadKeys.usuario(vars.id),
      });
      queryClient.invalidateQueries({ queryKey: identidadKeys.usuarios() });
      queryClient.invalidateQueries({ queryKey: ['admin', 'empleados'] });
      queryClient.invalidateQueries({ queryKey: ['catalogos', 'empleados'] });
      queryClient.invalidateQueries({ queryKey: ['admin', 'colaboradores'] });
    },
  });
}

export interface ReactivarUsuarioArgs {
  id: string;
  idempotencyKey: string;
}

export function useReactivarUsuario() {
  const queryClient = useQueryClient();
  return useMutation<UsuarioResponse, Error, ReactivarUsuarioArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      const { data } = await apiRequest<UsuarioResponse>(
        `/api/v1/identidad/usuarios/${id}/reactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: identidadKeys.usuario(vars.id),
      });
      queryClient.invalidateQueries({ queryKey: identidadKeys.usuarios() });
      queryClient.invalidateQueries({ queryKey: ['admin', 'empleados'] });
      queryClient.invalidateQueries({ queryKey: ['catalogos', 'empleados'] });
      queryClient.invalidateQueries({ queryKey: ['admin', 'colaboradores'] });
    },
  });
}

export interface AsignarRolArgs {
  usuarioId: string;
  payload: AsignarRolPayload;
  idempotencyKey: string;
}

export function useAsignarRol() {
  const queryClient = useQueryClient();
  return useMutation<UsuarioEmpresaRolResponse, Error, AsignarRolArgs>({
    mutationFn: async ({ usuarioId, payload, idempotencyKey }) => {
      const { data } = await apiRequest<UsuarioEmpresaRolResponse>(
        `/api/v1/identidad/usuarios/${usuarioId}/asignaciones`,
        { method: 'POST', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: identidadKeys.usuario(vars.usuarioId),
      });
    },
  });
}

export interface RevocarAsignacionArgs {
  /** Id de la asignación <c>UsuarioEmpresaRol</c> a borrar. */
  asignacionId: string;
  /** Id del usuario — usado solo para invalidar su detalle. */
  usuarioId: string;
  idempotencyKey: string;
}

export function useRevocarAsignacion() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, RevocarAsignacionArgs>({
    mutationFn: async ({ asignacionId, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/identidad/usuarios/asignaciones/${asignacionId}`,
        { method: 'DELETE', idempotencyKey },
      );
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: identidadKeys.usuario(vars.usuarioId),
      });
    },
  });
}

// ─── Helpers ────────────────────────────────────────────────────────

function buildListarPath(filtros: ListarUsuariosFiltros): string {
  const params = new URLSearchParams();
  if (filtros.soloActivos != null)
    params.set('soloActivos', String(filtros.soloActivos));
  if (filtros.offset != null) params.set('offset', String(filtros.offset));
  if (filtros.limit != null) params.set('limit', String(filtros.limit));
  if (filtros.empresaId != null) params.set('empresaId', filtros.empresaId);
  if (filtros.rolId != null) params.set('rolId', filtros.rolId);
  const query = params.toString();
  return query
    ? `/api/v1/identidad/usuarios/admin?${query}`
    : '/api/v1/identidad/usuarios/admin';
}
