import type { DictamenCaso, EstadoCaso, EtapaCaso } from '../api/types';

export const etapaTexto: Record<EtapaCaso, string> = {
  AntesDeCarga: 'Daño antes de cargar',
  RechazoEnSitio: 'Rechazo en sitio',
  DevolucionCliente: 'Devolución del cliente',
};
export const estadoCasoUi: Record<EstadoCaso, { texto: string; tono: 'warning' | 'info' | 'success' }> = {
  Abierto: { texto: 'Abierto', tono: 'warning' },
  Dictaminado: { texto: 'Dictaminado', tono: 'info' },
  Cerrado: { texto: 'Cerrado', tono: 'success' },
};
export const dictamenTexto: Record<DictamenCaso, string> = {
  RegresarDisponible: 'Regresar a disponible',
  Reproceso: 'Reproceso',
  Refabricacion: 'Refabricación',
  Desecho: 'Desecho',
};
