import { z } from 'zod';
import { EstadoPasivo } from '@/features/cxp/api/types';

/**
 * Schema de los <c>search params</c> de la bandeja de Facturas
 * (FE-F2-PR1). TanStack Router lo usa en <c>validateSearch</c>.
 */
export const FacturasSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoPasivo.Capturada),
      z.literal(EstadoPasivo.EnRevision),
      z.literal(EstadoPasivo.Autorizada),
      z.literal(EstadoPasivo.Pagada),
      z.literal(EstadoPasivo.Cancelada),
    ])
    .optional(),
  proveedorId: z.string().min(1).optional(),
  sucursalId: z.string().min(1).optional(),
  q: z.string().min(1).max(200).optional(),
});

export type FacturasSearch = z.infer<typeof FacturasSearchSchema>;

export const DEFAULT_FACTURAS_SEARCH: FacturasSearch = {};
