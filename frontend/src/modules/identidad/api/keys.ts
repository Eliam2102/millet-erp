/**
 * Query keys de TanStack Query para el módulo Identidad. Mismo shape
 * que <c>adminKeys</c>: <c>['identidad', recurso, acción, ...]</c>.
 *
 * <para>El primer slot es el módulo (invalidación masiva al cambiar de
 * empresa/usuario); el segundo es el recurso.</para>
 */

export interface ListarRolesFiltros {
  soloActivos?: boolean;
  offset?: number;
  limit?: number;
}

export interface ListarUsuariosFiltros {
  soloActivos?: boolean;
  empresaId?: string;
  rolId?: string;
  offset?: number;
  limit?: number;
}

export const identidadKeys = {
  all: ['identidad'] as const,

  roles: () => [...identidadKeys.all, 'roles'] as const,
  rolesList: (filtros: ListarRolesFiltros) =>
    [...identidadKeys.roles(), 'list', filtros] as const,
  rol: (id: string) => [...identidadKeys.roles(), 'detail', id] as const,

  permisos: () => [...identidadKeys.all, 'permisos'] as const,
  permisosList: (agrupado: boolean) =>
    [...identidadKeys.permisos(), 'list', { agrupado }] as const,

  usuarios: () => [...identidadKeys.all, 'usuarios'] as const,
  usuariosList: (filtros: ListarUsuariosFiltros) =>
    [...identidadKeys.usuarios(), 'list', filtros] as const,
  usuario: (id: string) =>
    [...identidadKeys.usuarios(), 'detail', id] as const,
  // Cuelga de usuario(id): invalidar el detalle refresca también los efectivos.
  permisosEfectivos: (usuarioId: string, empresaId: string) =>
    [...identidadKeys.usuario(usuarioId), 'permisos-efectivos', empresaId] as const,
} as const;
