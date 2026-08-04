import { esApiError } from '@/lib/api';

/**
 * Mensaje de error para el dropdown de un selector/picker de catálogo.
 * Con 403, prefiere el <c>detail</c> del Problem Details — el backend
 * (PermisoFaltanteResultHandler) nombra ahí el permiso canónico exacto.
 */
export function mensajeErrorCatalogo(error: unknown): string {
  if (esApiError(error) && error.status === 403) {
    return (
      error.problem.detail ??
      'Sin permiso para consultar este catálogo. Pídelo al administrador.'
    );
  }
  return 'No se pudo cargar el catálogo. Reintenta más tarde.';
}
