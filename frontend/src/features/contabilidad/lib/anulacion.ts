import type { SolicitudAnulacion } from '../api/polizas-types';

/** M-212: aviso cuando quien pidió la anulación intenta decidirla (el servidor también lo impide). */
export const MENSAJE_MISMA_PERSONA = 'Otra persona con facultad debe aprobar la anulación.';

/** M-212: quien pidió la anulación no la decide. */
export function esPropia(s: Pick<SolicitudAnulacion, 'solicitadaPorId'>, usuarioId: string | null): boolean {
  return !!usuarioId && s.solicitadaPorId === usuarioId;
}
