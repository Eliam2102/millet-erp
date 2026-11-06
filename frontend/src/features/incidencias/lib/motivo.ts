import { esApiError } from '@/lib/api';
import { errorUsuario } from '@/lib/error-usuario';

export const MAX_MOTIVO = 1000;

/** Motivo prellenado desde el rechazo: el `detail` del servidor o, si no trae, el mensaje para el usuario. */
export function motivoDesdeError(error: unknown): { codigoMotivo: string | null; motivo: string } {
  if (!esApiError(error)) return { codigoMotivo: null, motivo: '' };
  const detalle = error.problem.detail?.trim();
  return {
    codigoMotivo: error.code ?? null,
    motivo: (detalle || errorUsuario(error)).slice(0, MAX_MOTIVO),
  };
}
