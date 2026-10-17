import { z } from 'zod';
import { PaginaSearchShape } from '@/features/almacen/lib/paginacion';

export const SaldosSearchSchema = z.object({
  subAlmacenId: z.string().min(1).optional(),
  articuloId: z.string().min(1).optional(),
  soloConStock: z.boolean().optional(),
  ...PaginaSearchShape,
});

export type SaldosSearch = z.infer<typeof SaldosSearchSchema>;

export const DEFAULT_SALDOS_SEARCH: SaldosSearch = {};
