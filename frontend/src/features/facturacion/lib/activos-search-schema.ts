import { z } from 'zod';
import { EstadoAutorizacionActivo } from '@/features/facturacion/api/types';

/** Search params de la bandeja de autorizaciones de activos. */
export const ActivosSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoAutorizacionActivo.Autorizada),
      z.literal(EstadoAutorizacionActivo.Usada),
      z.literal(EstadoAutorizacionActivo.Cancelada),
    ])
    .optional(),
});

export type ActivosSearch = z.infer<typeof ActivosSearchSchema>;
