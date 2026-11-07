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

/** Incidencia que el servidor abrió con el rechazo (Problem Details `incidenciaId`/`incidenciaNumero`), si la hay. */
export function incidenciaDelRechazo(error: unknown): { id: string; numero: number } | null {
  if (!esApiError(error)) return null;
  const { incidenciaId, incidenciaNumero } = error.problem;
  return incidenciaId && incidenciaNumero ? { id: incidenciaId, numero: incidenciaNumero } : null;
}
