import {
  ESTADO_APLICACION_LABELS,
  ESTADO_CONCILIACION_LABELS,
  ESTADO_DEPOSITO_LABELS,
  ESTADO_DEVOLUCION_LABELS,
  SENTIDO_MOVIMIENTO_LABELS,
} from '../api/types';

export type TonoEstado = 'success' | 'warning' | 'danger' | 'info' | 'neutral';
export interface EstadoUi {
  tono: TonoEstado;
  texto: string;
}
function mapa(
  labels: Record<number, string>,
  tonos: Record<number, TonoEstado>,
): Record<string, EstadoUi> {
  return Object.fromEntries(
    Object.entries(labels).map(([valor, texto]) => [valor, { texto, tono: tonos[Number(valor)] }]),
  );
}
export const ESTADOS_UI = {
  sentido: mapa(SENTIDO_MOVIMIENTO_LABELS, { 1: 'success', 2: 'danger' }),
  aplicacion: mapa(ESTADO_APLICACION_LABELS, { 1: 'neutral', 2: 'info', 3: 'success' }),
  conciliacion: mapa(ESTADO_CONCILIACION_LABELS, { 1: 'warning', 2: 'success' }),
  deposito: mapa(ESTADO_DEPOSITO_LABELS, { 1: 'warning', 2: 'success', 3: 'danger' }),
  devolucion: mapa(ESTADO_DEVOLUCION_LABELS, {
    1: 'warning',
    2: 'info',
    3: 'danger',
    4: 'success',
    5: 'neutral',
  }),
  repp: {
    pendiente: { tono: 'warning', texto: 'REPP pendiente' },
    timbrado: { tono: 'success', texto: 'REPP timbrado' },
  },
  cuenta: {
    activa: { tono: 'success', texto: 'Activa' },
    inactiva: { tono: 'neutral', texto: 'Inactiva' },
  },
  concepto: {
    activo: { tono: 'success', texto: 'Activo' },
    inactivo: { tono: 'neutral', texto: 'Inactivo' },
  },
  aplicacionPago: {
    vigente: { tono: 'success', texto: 'Vigente' },
    revertida: { tono: 'neutral', texto: 'Revertida' },
  },
  metodoPago: {
    PPD: { tono: 'info', texto: 'PPD' },
    PUE: { tono: 'neutral', texto: 'PUE' },
    sinDato: { tono: 'neutral', texto: 'Sin dato' },
  },
} satisfies Record<string, Record<string, EstadoUi>>;
export type EntidadEstado = keyof typeof ESTADOS_UI;
export function estadoUi(entidad: EntidadEstado, estado: string | number): EstadoUi {
  return (
    (ESTADOS_UI[entidad] as Record<string, EstadoUi>)[estado] ?? {
      tono: 'neutral',
      texto: String(estado),
    }
  );
}
export function opcionesEstado(entidad: 'aplicacion' | 'conciliacion' | 'deposito' | 'devolucion') {
  const orden =
    entidad === 'devolucion' ? [1, 2, 4, 3, 5] : Object.keys(ESTADOS_UI[entidad]).map(Number);
  return orden.map((valor) => ({ valor, label: estadoUi(entidad, valor).texto }));
}
