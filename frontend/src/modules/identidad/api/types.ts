/**
 * Tipos del API REST del módulo Identidad — espejo de los DTOs del
 * backend en <c>Millet.Identidad.Application.*</c> (F-Admin-PR3.1 +
 * PR3.2 + PR3.3 + F-Admin-PR4.2 / RolesEndpoints + PermisosEndpoints
 * + UsuariosEndpoints).
 */

// ─── Responses ──────────────────────────────────────────────────────

export interface RolResponse {
  id: string;
  codigo: string;
  nombre: string;
  descripcion: string | null;
  esDelSistema: boolean;
  activo: boolean;
  version: number;
}

export interface RolGrupoEntraIdResponse {
  id: string;
  rolId: string;
  objectId: string;
  nombre: string;
}

export interface RolDetalleResponse {
  rol: RolResponse;
  permisoIds: string[];
  gruposEntraId: RolGrupoEntraIdResponse[];
}

export interface ListarRolesResponse {
  items: RolResponse[];
  total: number;
}

export interface PermisoResponse {
  id: string;
  codigo: string;
  modulo: string;
  recurso: string;
  accion: string;
  descripcion: string;
}

export interface PermisosPorModulo {
  modulo: string;
  items: PermisoResponse[];
}

export interface ListarPermisosResponse {
  items: PermisoResponse[];
  grupos: PermisosPorModulo[] | null;
}

// ─── Commands (request bodies) ──────────────────────────────────────

export interface CrearRolCommand {
  /** <c>Guid.Empty</c> ⇒ backend autogenera; el frontend lo deja vacío. */
  id?: string;
  codigo: string;
  nombre: string;
  descripcion: string | null;
}

export interface ActualizarRolPayload {
  nombre?: string | null;
  descripcion?: string | null;
  limpiarDescripcion?: boolean | null;
}

export interface AsignarPermisosCommand {
  permisoIds: string[];
}

export interface AsociarGrupoEntraIdCommand {
  objectId: string;
  nombre: string;
}

// ─── Usuarios (F-Admin-PR4.2) ───────────────────────────────────────

/**
 * Shape público de un usuario (admin). El listado catálogo legacy
 * (B.1) omite <c>entraOid</c>; este shape es el del endpoint
 * <c>/admin</c> que sí lo expone.
 */
export interface UsuarioResponse {
  id: string;
  email: string;
  entraOid: string;
  nombre: string;
  departamentoId: string | null;
  activo: boolean;
  version: number;
  empleadoId?: string | null;
}

/**
 * Asignación expandida con RFC de la empresa y código del rol —
 * resuelta server-side para evitar joins adicionales en la UI.
 */
export interface AsignacionDetalleResponse {
  id: string;
  empresaId: string;
  empresaRfc: string;
  rolId: string;
  rolCodigo: string;
  fechaAsignacion: string;
}

export interface UsuarioDetalleResponse {
  usuario: UsuarioResponse;
  asignaciones: AsignacionDetalleResponse[];
}

export interface ListarUsuariosResponse {
  items: UsuarioResponse[];
  total: number;
}

export interface UsuarioEmpresaRolResponse {
  id: string;
  usuarioId: string;
  empresaId: string;
  rolId: string;
  fechaAsignacion: string;
}

export interface CrearUsuarioCommand {
  /** <c>Guid.Empty</c> ⇒ backend autogenera. */
  id?: string;
  email: string;
  /** Si <c>null</c>, el backend resuelve con <c>IEntraIdResolverPort</c>
   *  o genera el OID pendiente <c>pending:{email}</c> (plan 15). */
  entraIdObjectId?: string | null;
  nombreCompleto: string;
  departamentoId?: string | null;
}

export interface ActualizarUsuarioPayload {
  email?: string | null;
  nombreCompleto?: string | null;
  departamentoId?: string | null;
  /** Cuando <c>true</c> + <c>departamentoId == null</c>, el backend
   *  borra la asignación de departamento. */
  limpiarDepartamento?: boolean | null;
}

export interface AsignarRolPayload {
  empresaId: string;
  rolId: string;
}
