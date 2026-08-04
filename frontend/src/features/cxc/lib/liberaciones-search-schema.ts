import { z } from 'zod';
import { ResultadoLiberacion } from '@/features/cxc/api/types';

/**
 * Search params de la bandeja de liberaciones (<c>/cxc/liberaciones</c>).
 * <c>resultado</c> y <c>clienteId</c> filtran server-side; <c>q</c>
 * (folio de pedido, topbar §3) se aplica client-side porque el backend
 * solo soporta match exacto de <c>pedidoRef</c>. <c>tab</c> alterna
 * entre decisiones y autorizaciones consumibles.
 */
export const LiberacionesSearchSchema = z.object({
  resultado: z
    .union([
      z.literal(ResultadoLiberacion.Liberado),
      z.literal(ResultadoLiberacion.Retenido),
      z.literal(ResultadoLiberacion.LiberadoConOverride),
    ])
    .optional(),
  clienteId: z.string().optional(),
  q: z.string().optional(),
  tab: z.enum(['decisiones', 'autorizaciones']).optional(),
  offset: z.number().int().min(0).optional(),
  limit: z.number().int().min(1).max(500).optional(),
});

export type LiberacionesSearch = z.infer<typeof LiberacionesSearchSchema>;
