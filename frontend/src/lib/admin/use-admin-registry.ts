import { useMemo } from 'react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { adminRegistry, type AdminSection } from '@/lib/admin/registry';

/**
 * Retorna las <see cref="AdminSection"/> registradas en
 * <see cref="adminRegistry"/> que el usuario actual puede ver, según
 * sus permisos en la empresa activa.
 *
 * <para>El filtrado es defensivo: cards sin <c>permisoRequerido</c> en
 * la lista de permisos del usuario quedan fuera, así que el landing
 * <c>/admin</c> nunca expone botones sin permiso.</para>
 *
 * <para>Memoizado contra la lista de permisos del store; mientras la
 * sesión no cambie, no se recalcula.</para>
 */
export function useAdminRegistry(): readonly AdminSection[] {
  const permisos = useAuthStore((s) => s.permisos);
  return useMemo(
    () => adminRegistry.filter((s) => permisos.includes(s.permisoRequerido)),
    [permisos],
  );
}

/**
 * <c>true</c> si el usuario tiene al menos una card visible en
 * <c>/admin</c>. Lo consume el engrane del Topbar para decidir si se
 * muestra o se oculta.
 */
export function useAdminAccess(): boolean {
  return useAdminRegistry().length > 0;
}
