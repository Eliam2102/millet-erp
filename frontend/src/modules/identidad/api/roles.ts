import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  identidadKeys,
  type ListarRolesFiltros,
} from '@/modules/identidad/api/keys';
import type {
  ActualizarRolPayload,
  AsignarPermisosCommand,
  AsociarGrupoEntraIdCommand,
  CrearRolCommand,
  ListarRolesResponse,
  RolDetalleResponse,
  RolGrupoEntraIdResponse,
  RolResponse,
} from '@/modules/identidad/api/types';

/**
 * Hooks de TanStack Query del recurso Roles (F-Admin-PR3.1 + PR3.2 +
 * PR3.3 backend).
 *
 * <para>Endpoints consumidos:</para>
 * <list>
 *   <item><c>GET    /api/v1/identidad/roles</c> — lista paginada.</item>
 *   <item><c>GET    /api/v1/identidad/roles/{id}</c> — detalle con
 *         permisoIds + gruposEntraId.</item>
 *   <item><c>POST   /api/v1/identidad/roles</c> — alta (Idempotency-Key).</item>
 *   <item><c>PATCH  /api/v1/identidad/roles/{id}</c> — patch parcial.</item>
 *   <item><c>DELETE /api/v1/identidad/roles/{id}</c> — soft-delete
 *         (cambia Activo=false).</item>
 *   <item><c>PUT    /api/v1/identidad/roles/{id}/permisos</c> — batch
 *         atómico del set completo de permisos.</item>
 *   <item><c>POST   /api/v1/identidad/roles/{id}/grupos-entra-id</c>.</item>
 *   <item><c>DELETE /api/v1/identidad/roles/grupos-entra-id/{id}</c>.</item>
 * </list>
 *
 * <para>Convención de invalidación: cualquier mutación invalida
 * <c>identidadKeys.roles()</c> (familia entera) — refresca lista y
 * detalle a la vez con costo bajo y sin inconsistencias temporales.</para>
 */

// ─── Queries ────────────────────────────────────────────────────────

export function useRoles(filtros: ListarRolesFiltros = {}, enabled = true) {
  return useQuery({
    queryKey: identidadKeys.rolesList(filtros),
    queryFn: async ({ signal }) => {
      const path = buildListarPath(filtros);
      const { data } = await apiRequest<ListarRolesResponse>(path, { signal });
      return data;
    },
    enabled,
  });
}

export function useRol(id: string | null | undefined) {
  return useQuery({
    queryKey:
      id != null ? identidadKeys.rol(id) : (['identidad', 'noop'] as const),
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useRol invocado sin id');
      const { data } = await apiRequest<RolDetalleResponse>(
        `/api/v1/identidad/roles/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

// ─── Mutations ──────────────────────────────────────────────────────

export interface CrearRolArgs {
  command: CrearRolCommand;
  idempotencyKey: string;
}

export function useCrearRol() {
  const queryClient = useQueryClient();
  return useMutation<RolResponse, Error, CrearRolArgs>({
    mutationFn: async ({ command, idempotencyKey }) => {
      const { data } = await apiRequest<RolResponse>(
        '/api/v1/identidad/roles',
        { method: 'POST', body: command, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: identidadKeys.roles() });
    },
  });
}

export interface ActualizarRolArgs {
  id: string;
  payload: ActualizarRolPayload;
  idempotencyKey: string;
}

export function useActualizarRol() {
  const queryClient = useQueryClient();
  return useMutation<RolResponse, Error, ActualizarRolArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<RolResponse>(
        `/api/v1/identidad/roles/${id}`,
        { method: 'PATCH', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({ queryKey: identidadKeys.rol(vars.id) });
      queryClient.invalidateQueries({ queryKey: identidadKeys.roles() });
    },
  });
}

export interface EliminarRolArgs {
  id: string;
  idempotencyKey: string;
}

export function useEliminarRol() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, EliminarRolArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      await apiRequest<void>(`/api/v1/identidad/roles/${id}`, {
        method: 'DELETE',
        idempotencyKey,
      });
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({ queryKey: identidadKeys.rol(vars.id) });
      queryClient.invalidateQueries({ queryKey: identidadKeys.roles() });
    },
  });
}

export interface AsignarPermisosArgs {
  id: string;
  command: AsignarPermisosCommand;
  idempotencyKey: string;
}

export function useAsignarPermisos() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, AsignarPermisosArgs>({
    mutationFn: async ({ id, command, idempotencyKey }) => {
      await apiRequest<void>(`/api/v1/identidad/roles/${id}/permisos`, {
        method: 'PUT',
        body: command,
        idempotencyKey,
      });
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({ queryKey: identidadKeys.rol(vars.id) });
    },
  });
}

export interface AsociarGrupoEntraIdArgs {
  rolId: string;
  command: AsociarGrupoEntraIdCommand;
  idempotencyKey: string;
}

export function useAsociarGrupoEntraId() {
  const queryClient = useQueryClient();
  return useMutation<RolGrupoEntraIdResponse, Error, AsociarGrupoEntraIdArgs>({
    mutationFn: async ({ rolId, command, idempotencyKey }) => {
      const { data } = await apiRequest<RolGrupoEntraIdResponse>(
        `/api/v1/identidad/roles/${rolId}/grupos-entra-id`,
        { method: 'POST', body: command, idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: identidadKeys.rol(vars.rolId),
      });
    },
  });
}

export interface DesasociarGrupoEntraIdArgs {
  /** Id del rol — se usa solo para invalidar el detalle. */
  rolId: string;
  /** Id del registro <c>RolGrupoEntraId</c> (no del rol). */
  grupoId: string;
  idempotencyKey: string;
}

export function useDesasociarGrupoEntraId() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, DesasociarGrupoEntraIdArgs>({
    mutationFn: async ({ grupoId, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/identidad/roles/grupos-entra-id/${grupoId}`,
        { method: 'DELETE', idempotencyKey },
      );
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: identidadKeys.rol(vars.rolId),
      });
    },
  });
}

// ─── Helpers ────────────────────────────────────────────────────────

function buildListarPath(filtros: ListarRolesFiltros): string {
  const params = new URLSearchParams();
  if (filtros.soloActivos != null)
    params.set('soloActivos', String(filtros.soloActivos));
  if (filtros.offset != null) params.set('offset', String(filtros.offset));
  if (filtros.limit != null) params.set('limit', String(filtros.limit));
  const query = params.toString();
  return query
    ? `/api/v1/identidad/roles?${query}`
    : '/api/v1/identidad/roles';
}
