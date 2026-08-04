import { z } from 'zod';
import { EstadoPropuestaAplicacion } from '@/features/cxc/api/types';

/**
 * Search params de la bandeja de aplicaciones (<c>/cxc/aplicaciones</c>).
 * <c>estado</c> y <c>clienteId</c> filtran server-side; <c>q</c>
 * (topbar) filtra client-side por referencia de depósito / remittance.
 */
export const AplicacionesSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoPropuestaAplicacion.Propuesta),
      z.literal(EstadoPropuestaAplicacion.Confirmada),
      z.literal(EstadoPropuestaAplicacion.Rechazada),
    ])
    .optional(),
  clienteId: z.string().optional(),
  q: z.string().optional(),
  offset: z.number().int().min(0).optional(),
  limit: z.number().int().min(1).max(500).optional(),
});

export type AplicacionesSearch = z.infer<typeof AplicacionesSearchSchema>;
