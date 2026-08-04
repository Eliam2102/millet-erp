import { z } from 'zod';
import { EstadoAnticipo } from '@/features/facturacion/api/types';

/**
 * Search params del Control de Anticipos (<c>/facturacion/anticipos</c>).
 * Filtros del resumen: cliente, estado, obra, rango de fechas.
 */
export const AnticiposSearchSchema = z.object({
  clienteId: z.string().optional(),
  estado: z
    .union([
      z.literal(EstadoAnticipo.Abierto),
      z.literal(EstadoAnticipo.Amortizado),
      z.literal(EstadoAnticipo.Cancelado),
    ])
    .optional(),
  obraId: z.number().int().positive().optional(),
  desde: z.string().optional(),
  hasta: z.string().optional(),
});

export type AnticiposSearch = z.infer<typeof AnticiposSearchSchema>;
