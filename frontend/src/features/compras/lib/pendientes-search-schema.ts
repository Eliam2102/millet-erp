import { z } from 'zod';

/**
 * Schema Zod de los <c>search params</c> de la bandeja P2 — pendientes
 * de autorización. Doc 05 §13.9.
 *
 * <para>Subset del schema de la bandeja general: solo
 * <c>departamentoId</c>, <c>q</c>, <c>offset</c>, <c>limit</c>. El
 * estado siempre es <c>EnAutorizacion</c> (lo decide el endpoint
 * <c>/pendientes-autorizacion</c>), así que no se expone como
 * filtro.</para>
 */
export const PendientesSearchSchema = z.object({
  departamentoId: z.string().min(1).optional(),
  q: z.string().min(1).optional(),
  // Nivel de autorización pendiente (PR-A): 1 = falta N1, 2 = falta N2.
  // Filtro server-side; ausente = todos.
  nivelPendiente: z.coerce.number().int().min(1).max(2).optional(),
  offset: z.coerce.number().int().min(0).optional().default(0),
  limit: z.coerce.number().int().min(1).max(200).optional().default(50),
});

export type PendientesSearch = z.infer<typeof PendientesSearchSchema>;

export const DEFAULT_PENDIENTES_SEARCH: PendientesSearch = {
  offset: 0,
  limit: 50,
};
