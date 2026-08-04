import { z } from 'zod';
import { EstadoLineaCredito, MONEDAS_LINEA_CREDITO } from '@/features/cxc/api/types';

/**
 * Search params de la bandeja de líneas de crédito
 * (<c>/cxc/lineas-credito</c>). Filtros server-side: estado, moneda,
 * clienteId; <c>q</c> se aplica client-side (razón social / RFC / clave
 * del lookup). La ruta aplica
 * <c>validateSearch: LineasSearchSchema.parse</c>.
 */
export const LineasSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoLineaCredito.Activa),
      z.literal(EstadoLineaCredito.Bloqueada),
      z.literal(EstadoLineaCredito.Suspendida),
    ])
    .optional(),
  moneda: z.enum(MONEDAS_LINEA_CREDITO).optional(),
  clienteId: z.string().optional(),
  q: z.string().optional(),
  offset: z.number().int().min(0).optional(),
  limit: z.number().int().min(1).max(500).optional(),
});

export type LineasSearch = z.infer<typeof LineasSearchSchema>;
