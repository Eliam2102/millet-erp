import { z } from 'zod';
import { EstadoTimbrado } from '@/features/facturacion/api/types';

/** Search params de la bandeja de REPP (<c>/facturacion/repp</c>). */
export const ReppSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoTimbrado.Borrador),
      z.literal(EstadoTimbrado.TimbradoEnProceso),
      z.literal(EstadoTimbrado.Timbrado),
      z.literal(EstadoTimbrado.TimbradoFallido),
      z.literal(EstadoTimbrado.Cancelado),
    ])
    .optional(),
  q: z.string().optional(),
  // Bucket "Sin asignar" de la Capa A (solo caja.leer-todas, CAJAS-PR6).
  alcance: z.literal('sin-asignar').optional(),
});

export type ReppSearch = z.infer<typeof ReppSearchSchema>;
