import { z } from 'zod';

export const SaldosSearchSchema = z.object({
  subAlmacenId: z.string().min(1).optional(),
  articuloId: z.string().min(1).optional(),
  soloConStock: z.boolean().optional(),
});

export type SaldosSearch = z.infer<typeof SaldosSearchSchema>;

export const DEFAULT_SALDOS_SEARCH: SaldosSearch = {};
