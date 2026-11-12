import { z } from 'zod';
import type { EstadoIncidencia } from '../api/incidencias';

/** Límites del dominio (IncidenciaPosterior): el formulario los valida antes de enviar. */
export const HALLAZGO_MINIMO = 5;
export const HALLAZGO_MAXIMO = 1000;
export const IMPACTO_MINIMO = 3;
export const IMPACTO_MAXIMO = 500;

export const ESTADOS_INCIDENCIA: Record<EstadoIncidencia, { tono: 'warning' | 'success'; texto: string }> = {
  Abierta: { tono: 'warning', texto: 'Abierta' },
  Resuelta: { tono: 'success', texto: 'Resuelta' },
};

export const IncidenciasSearchSchema = z.object({
  estado: z.enum(['Abierta', 'Resuelta']).optional().catch(undefined),
});
export type IncidenciasSearch = z.infer<typeof IncidenciasSearchSchema>;

export function fechaHora(iso: string): string {
  return new Date(iso).toLocaleString('es-MX', { dateStyle: 'medium', timeStyle: 'short' });
}
