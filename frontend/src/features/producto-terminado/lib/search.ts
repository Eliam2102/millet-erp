import { z } from 'zod';
import { zId } from '@/lib/z-id';
export const ptSearchSchema = z.object({
  pedido: z.string().trim().max(100).optional().catch(undefined),
  sucursalId: zId().optional().catch(undefined),
  page: z.coerce.number().int().min(1).max(100000).catch(1),
});
export type PtSearch = z.infer<typeof ptSearchSchema>;
