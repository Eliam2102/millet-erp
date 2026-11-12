/** Qué sigue según el motivo SAT de cancelación (misma regla que `GuiaMotivoCancelacion` del backend). */
export interface GuiaMotivo {
  cancelaPrimero: boolean;
  emiteNueva: boolean;
  texto: string;
}

export const GUIA_MOTIVO_CANCELACION: Record<string, GuiaMotivo> = {
  '01': {
    cancelaPrimero: false,
    emiteNueva: true,
    texto: 'Se emite la factura nueva con relación 04 y después se cancela esta con el UUID de la nueva.',
  },
  '02': {
    cancelaPrimero: true,
    emiteNueva: true,
    texto: 'Primero se cancela esta factura; cuando esté cancelada, se emite la nueva sin relación (botón «Reemitir»).',
  },
  '03': {
    cancelaPrimero: true,
    emiteNueva: false,
    texto: 'La operación no se llevó a cabo: solo se cancela, no se emite factura nueva.',
  },
  '04': {
    cancelaPrimero: true,
    emiteNueva: true,
    texto: 'Solo para factura global: se cancela y después se emite la factura nominativa sin relación.',
  },
};

/** Texto de aceptación del receptor: lo que la regla fiscal exime o lo que queda en seguimiento. */
export function textoAceptacion(requiere: boolean, razones: readonly string[]): string {
  return requiere
    ? 'El receptor debe aceptar la cancelación. Si no responde, la solicitud queda en seguimiento (en proceso) hasta que venza el plazo.'
    : `No requiere aceptación del receptor: ${razones.join('; ').toLowerCase()}.`;
}
