import { useAuthStore } from '@/lib/auth/auth-store';

/**
 * Hook que retorna <c>true</c> si el usuario tiene el permiso especificado
 * en la empresa actualmente seleccionada. Lee del store local; los permisos
 * se cargaron en el último login o cambio de empresa (ver
 * <c>LoginOrchestrator</c> en el backend).
 *
 * <para>Para gating de UI declarativo, ver <see cref="RequirePermission" />.</para>
 *
 * @example
 * ```tsx
 * const canCreateUsers = useHasPermission(PermisosCanonicos.IdentidadUsuariosCrear);
 * return canCreateUsers ? <button>Crear usuario</button> : null;
 * ```
 */
export function useHasPermission(code: string): boolean {
  return useAuthStore((s) => s.permisos.includes(code));
}

/**
 * Versión que acepta múltiples códigos: devuelve <c>true</c> si el usuario
 * tiene AL MENOS UNO de los permisos. Útil para botones que abren una
 * sección con varias acciones donde basta una.
 */
export function useHasAnyPermission(codes: readonly string[]): boolean {
  return useAuthStore((s) => codes.some((code) => s.permisos.includes(code)));
}

/**
 * Versión estricta: devuelve <c>true</c> solo si el usuario tiene TODOS los
 * permisos especificados.
 */
export function useHasAllPermissions(codes: readonly string[]): boolean {
  return useAuthStore((s) => codes.every((code) => s.permisos.includes(code)));
}
