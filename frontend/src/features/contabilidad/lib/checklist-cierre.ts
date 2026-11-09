import type { EstadoPasoChecklist } from '../api/cierre';

type Tono = 'success' | 'warning' | 'danger' | 'info' | 'neutral';

/** CON-12 · estado de cada paso del checklist de cierre: texto y tono de su insignia. */
export const ESTADOS_PASO: Record<EstadoPasoChecklist, { tono: Tono; texto: string }> = {
  Cumple: { tono: 'success', texto: 'Cumple' },
  Pendiente: { tono: 'warning', texto: 'Pendiente' },
  NoAplica: { tono: 'neutral', texto: 'No aplica' },
  Excepcion: { tono: 'info', texto: 'Excepción autorizada' },
  Confirmado: { tono: 'success', texto: 'Confirmado' },
};
