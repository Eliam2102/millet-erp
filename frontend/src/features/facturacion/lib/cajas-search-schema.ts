import { z } from 'zod';

/**
 * Search params de la bandeja de cajas (<c>/facturacion/cajas</c>,
 * CAJAS-PR5). Filtro de activas (server-side) + búsqueda libre client-side
 * por nombre/descripción + pestaña (cajas | alcances por usuario).
 */
export const CajasSearchSchema = z.object({
  soloActivas: z.boolean().optional(),
  q: z.string().optional(),
  tab: z.enum(['cajas', 'alcances']).optional(),
});

export type CajasSearch = z.infer<typeof CajasSearchSchema>;
