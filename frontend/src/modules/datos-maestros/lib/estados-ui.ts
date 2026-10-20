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
  origen: { Aw: { texto: 'A+W', tono: 'info' }, Manual: { texto: 'Manual', tono: 'neutral' } },
  naturaleza: {
    Estandar: { texto: 'Estándar', tono: 'neutral' },
    Servicio: { texto: 'Servicio', tono: 'info' },
    Critico: { texto: 'Crítico', tono: 'warning' },
    Riesgo: { texto: 'Riesgo', tono: 'danger' },
  },
  corrida: {
    Completa: { texto: 'Completa', tono: 'success' },
    Parcial: { texto: 'Parcial', tono: 'warning' },
    Fallida: { texto: 'Fallida', tono: 'danger' },
    EnCurso: { texto: 'En curso', tono: 'info' },
    Pendiente: { texto: 'Pendiente', tono: 'warning' },
    Cancelada: { texto: 'Cancelada', tono: 'neutral' },
  },
  sincronizacion: {
    Aplicado: { texto: 'Sincronizado', tono: 'success' },
    SinCambios: { texto: 'Sincronizado', tono: 'success' },
    SinAplicar: { texto: 'Sin aplicar', tono: 'neutral' },
    Pendiente: { texto: 'Pendiente de validación', tono: 'warning' },
    Conflicto: { texto: 'Conflicto', tono: 'warning' },
    Error: { texto: 'Error de sincronización', tono: 'danger' },
  },
  fiscal: { Incompleto: { texto: 'Fiscales incompletos', tono: 'warning' } },
};
export function estadoUi(entidad: string, estado: string): EstadoUi {
  return ESTADOS_UI[entidad]?.[estado] ?? { texto: estado, tono: 'neutral' };
}
