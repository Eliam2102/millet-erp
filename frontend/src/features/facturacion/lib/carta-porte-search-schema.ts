import { z } from 'zod';
import { EstadoTimbrado } from '@/features/facturacion/api/types';

/** Search params de la bandeja de Carta Porte (<c>/facturacion/carta-porte</c>). */
export const CartaPorteSearchSchema = z.object({
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
});

export type CartaPorteSearch = z.infer<typeof CartaPorteSearchSchema>;
