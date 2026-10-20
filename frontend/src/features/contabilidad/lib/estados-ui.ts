import type { EstadoPoliza } from '../api/polizas-types';
import type { EstadoPlantillaRecurrente } from '../api/recurrentes-types';
import type { EstadoPeriodo } from '../api/periodos';

type EstadoUi = { tono: 'success' | 'warning' | 'danger' | 'info' | 'neutral'; texto: string };
export const ESTADOS_POLIZA: Record<EstadoPoliza, EstadoUi> = {
  Borrador: { tono: 'neutral', texto: 'Borrador' },
  Autorizada: { tono: 'success', texto: 'Autorizada' },
  Anulada: { tono: 'danger', texto: 'Anulada' },
};
export const ESTADOS_PLANTILLA: Record<EstadoPlantillaRecurrente, EstadoUi> = {
  Borrador: { tono: 'neutral', texto: 'Borrador' },
  Activa: { tono: 'success', texto: 'Activa' },
  Suspendida: { tono: 'warning', texto: 'Suspendida' },
  Terminada: { tono: 'info', texto: 'Terminada' },
};
export const ESTADOS_PERIODO: Record<EstadoPeriodo, EstadoUi> = {
  NoAbierto: { tono: 'neutral', texto: 'No abierto' },
  Abierto: { tono: 'info', texto: 'Abierto' },
  Cerrado: { tono: 'success', texto: 'Cerrado' },
};

export const ESTADOS_REGLA: Record<string, EstadoUi> = {
  Futura: { tono: 'info', texto: 'Futura' },
  Vigente: { tono: 'success', texto: 'Vigente' },
  Cerrada: { tono: 'neutral', texto: 'Cerrada' },
};
export const ESTADOS_SOLICITUD: Record<string, EstadoUi> = {
  Pendiente: { tono: 'warning', texto: 'Pendiente' },
  Autorizada: { tono: 'success', texto: 'Autorizada' },
  Rechazada: { tono: 'danger', texto: 'Rechazada' },
};
export const ESTADOS_ACTIVIDAD: Record<string, EstadoUi> = {
  Activo: { tono: 'success', texto: 'Activo' },
  Inactivo: { tono: 'neutral', texto: 'Inactivo' },
};
export const ESTADOS_IMPORTACION: Record<string, EstadoUi> = {
  Crear: { tono: 'success', texto: 'Crear' },
  Actualizar: { tono: 'success', texto: 'Actualizar' },
  SinCambios: { tono: 'neutral', texto: 'Sin cambios' },
  Rechazar: { tono: 'danger', texto: 'Rechazada' },
  Omitida: { tono: 'neutral', texto: 'Omitida (título de reporte)' },
};
