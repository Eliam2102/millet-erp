import { z } from 'zod';
import { TipoAlertaCartera } from '@/features/cxc/api/types';

/**
 * Search params de la bandeja de alertas (<c>/cxc/alertas</c>).
 * <c>pendientes</c> (default true) mapea a <c>atendida=false</c> en el
 * backend; <c>q</c> (topbar) filtra client-side sobre el detalle.
 */
export const AlertasSearchSchema = z.object({
  pendientes: z.boolean().optional(),
  tipo: z
    .union([
      z.literal(TipoAlertaCartera.Solunion90d),
      z.literal(TipoAlertaCartera.ExcesoCredito),
      z.literal(TipoAlertaCartera.AutoBloqueoVencimiento),
    ])
    .optional(),
  clienteId: z.string().optional(),
  q: z.string().optional(),
  offset: z.number().int().min(0).optional(),
  limit: z.number().int().min(1).max(500).optional(),
});

export type AlertasSearch = z.infer<typeof AlertasSearchSchema>;
