import { esApiError } from '@/lib/api';

/** Explica el acceso al catálogo sin exponer permisos canónicos ni excepciones. */
export function mensajeErrorCatalogo(error: unknown): string {
  if (esApiError(error) && error.status === 403) {
    return 'Sin permiso para consultar este catálogo. Pídelo al administrador.';
  }
  return 'No se pudo cargar el catálogo. Reintenta más tarde.';
}
