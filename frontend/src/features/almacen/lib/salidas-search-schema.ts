import { z } from 'zod';
import { EstadoMovimiento } from '@/features/almacen/api/types';

const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;

/**
 * Schema de los <c>search params</c> de la bandeja de Salidas
 * (FE-F3-PR1). TanStack Router lo usa en <c>validateSearch</c>.
 */
export const SalidasSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoMovimiento.Borrador),
      z.literal(EstadoMovimiento.Validado),
      z.literal(EstadoMovimiento.Registrado),
      z.literal(EstadoMovimiento.Cancelado),
    ])
    .optional(),
  subAlmacenId: z.string().min(1).optional(),
  rqId: z.string().min(1).optional(),
  desde: z.string().regex(DATE_ONLY_RE).optional(),
  hasta: z.string().regex(DATE_ONLY_RE).optional(),
  soloVales: z.boolean().optional(),
  noRegularizados: z.boolean().optional(),
  q: z.string().min(1).max(200).optional(),
});

export type SalidasSearch = z.infer<typeof SalidasSearchSchema>;

export const DEFAULT_SALIDAS_SEARCH: SalidasSearch = {};
