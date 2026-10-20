import { EstatusCatalogo, TriEstado } from '../api/types';

type EstadoUi = { tono: 'success' | 'warning' | 'danger' | 'info' | 'neutral'; texto: string };

/** Presentación de los estados existentes; no sustituye los valores del dominio. */
export const ESTADOS_CATALOGO: Record<EstatusCatalogo, EstadoUi> = {
  [EstatusCatalogo.Activo]: { tono: 'success', texto: 'Activo' },
  [EstatusCatalogo.EnRevision]: { tono: 'warning', texto: 'En revisión' },
  [EstatusCatalogo.Inactivo]: { tono: 'neutral', texto: 'Inactivo' },
};
export const ESTADOS_ASIGNACION: Record<TriEstado, EstadoUi> = {
  [TriEstado.Ninguno]: { tono: 'neutral', texto: 'Ninguna' },
  [TriEstado.Parcial]: { tono: 'info', texto: 'Parcial' },
  [TriEstado.Todo]: { tono: 'success', texto: 'Todas' },
};
export const ESTADOS_ALCANCE = { total: { tono: 'warning', texto: 'Alcance total' } } as const;
export const ESTADOS_EQUIVALENCIA = {
  demo: { tono: 'warning', texto: 'DEMO · Por validar' },
} as const;
