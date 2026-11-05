import { esApiError, type ProblemDetails } from '@/lib/api';

/** Conserva el estado de consulta sin mostrar excepciones ni claves del servidor. */
export function problemaConsulta(error: unknown): ProblemDetails | undefined {
  if (!esApiError(error)) return undefined;
  const status = error.status;
  return {
    type: 'about:blank',
    status,
    title: status === 403
      ? 'No tienes acceso a estos datos. Pide al administrador que revise tus permisos.'
      : 'No se pudo completar la consulta. Revisa tu conexión y vuelve a cargar los datos.',
  };
}
