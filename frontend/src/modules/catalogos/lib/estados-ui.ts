export type TonoEstado = 'success' | 'warning' | 'danger' | 'info' | 'neutral';
export interface EstadoUi {
  texto: string;
  tono: TonoEstado;
}
export const ESTADOS_UI: Record<string, Record<string, EstadoUi>> = {
  catalogo: {
    Activo: { texto: 'Activo', tono: 'success' },
    Inactivo: { texto: 'Inactivo', tono: 'neutral' },
    EnRevision: { texto: 'En revisión', tono: 'warning' },
  },
  moneda: {
    Activa: { texto: 'Activa', tono: 'success' },
    Inactiva: { texto: 'Inactiva', tono: 'neutral' },
  },

};
export function estadoUi(entidad: string, estado: string): EstadoUi {
  return ESTADOS_UI[entidad]?.[estado] ?? { texto: estado, tono: 'neutral' };
}
