import { z } from 'zod';
import { ResultadoCobranza } from '@/features/cxc/api/types';

/**
 * Search params de cobranza (<c>/cxc/cobranza</c>). El cliente es el
 * filtro PRIMARIO y obligatorio para consultar (05-frontend-diseno §2);
 * sin él la página muestra el estado "elige un cliente". <c>q</c>
 * (topbar) filtra client-side sobre las notas.
 */
export const CobranzaSearchSchema = z.object({
  clienteId: z.string().optional(),
  resultado: z
    .union([
      z.literal(ResultadoCobranza.PromesaPago),
      z.literal(ResultadoCobranza.SinRespuesta),
      z.literal(ResultadoCobranza.Excusa),
      z.literal(ResultadoCobranza.Otro),
    ])
    .optional(),
  q: z.string().optional(),
  offset: z.number().int().min(0).optional(),
  limit: z.number().int().min(1).max(500).optional(),
});

export type CobranzaSearch = z.infer<typeof CobranzaSearchSchema>;
