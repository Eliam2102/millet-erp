/**
 * Stub del hook que consultará Microsoft Entra ID para autocompletar
 * datos de un usuario por email al darlo de alta en
 * <c>SheetNuevoUsuario</c>.
 *
 * <para>El resolver real vive en el backend
 * (<c>IEntraIdResolverPort</c> — actualmente
 * <c>LocalEntraIdResolverNoOp</c>). Mientras no exista la
 * implementación HTTP, este hook devuelve siempre <c>{ data:
 * undefined, isLoading: false }</c> para que el form no quede
 * bloqueado esperando una respuesta inexistente.</para>
 */

// PLATFORM-TODO(<EntraIdResolver>): este hook devuelve no-op hasta que
// el resolver de Entra ID esté implementado backend-side. Cuando
// llegue, conectar a GET /api/v1/admin/entra-id/usuarios?email=... y
// devolver { id, nombre, departamento } para autocompletar el form.
export interface EntraIdUsuarioSearchResult {
  /** ObjectId del usuario en Microsoft Entra ID. */
  objectId: string;
  /** Nombre legible (<c>displayName</c> de Graph). */
  nombre: string;
  /** Email primario reportado por Entra ID. */
  email: string;
}

export interface UseEntraIdUsuarioSearchResult {
  data: EntraIdUsuarioSearchResult | undefined;
  isLoading: boolean;
}

export function useEntraIdUsuarioSearch(
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  _email: string | null | undefined,
): UseEntraIdUsuarioSearchResult {
  return { data: undefined, isLoading: false };
}
