import { z } from 'zod';
import { PaginaSearchShape } from '@/components/erp/data/paginacion';
import { EstadoIncidencia } from '@/features/incidencias/api/types';

/** Vistas de «Mis incidencias»: pendientes (por omisión), cada estado o todas. */
export const VistaIncidencias = {
  Pendientes: 'pendientes',
  Abierta: EstadoIncidencia.Abierta,
  EnAtencion: EstadoIncidencia.EnAtencion,
  Resuelta: EstadoIncidencia.Resuelta,
  Cancelada: EstadoIncidencia.Cancelada,
  Todas: 'todas',
} as const;
export type VistaIncidencias = (typeof VistaIncidencias)[keyof typeof VistaIncidencias];

export const IncidenciasSearchSchema = z.object({
  vista: z.enum(['pendientes', 'Abierta', 'EnAtencion', 'Resuelta', 'Cancelada', 'todas']).optional().catch(undefined),
  modulo: z.string().regex(/^[a-z0-9._-]{1,50}$/).optional().catch(undefined),
  ...PaginaSearchShape,
});

export type IncidenciasSearch = z.infer<typeof IncidenciasSearchSchema>;

/** Traduce la vista al parámetro `estado` del servidor (sin estado = pendientes). */
export function estadosDeVista(vista: VistaIncidencias | undefined): string[] | undefined {
  switch (vista ?? VistaIncidencias.Pendientes) {
    case VistaIncidencias.Pendientes:
      return undefined;
    case VistaIncidencias.Todas:
      return Object.values(EstadoIncidencia);
    default:
      return [vista as string];
  }
}
