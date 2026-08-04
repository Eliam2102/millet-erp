import { z } from 'zod';
import { EstadoTimbrado } from '@/features/facturacion/api/types';

/**
 * Search params de la bandeja de comprobantes (<c>/facturacion/facturas</c>).
 * Filtro por estado de timbrado (server-side) + búsqueda libre client-side
 * (folio / UUID / receptor). La ruta aplica
 * <c>validateSearch: FacturasSearchSchema.parse</c>.
 */
export const FacturasSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoTimbrado.Borrador),
      z.literal(EstadoTimbrado.PendientePedimento),
      z.literal(EstadoTimbrado.TimbradoEnProceso),
      z.literal(EstadoTimbrado.Timbrado),
      z.literal(EstadoTimbrado.TimbradoFallido),
      z.literal(EstadoTimbrado.CancelacionPendiente),
      z.literal(EstadoTimbrado.Cancelado),
      z.literal(EstadoTimbrado.Descartada),
    ])
    .optional(),
  q: z.string().optional(),
  // Bucket "Sin asignar" de la Capa A (solo caja.leer-todas, CAJAS-PR6).
  alcance: z.literal('sin-asignar').optional(),
  offset: z.number().int().min(0).optional(),
  limit: z.number().int().min(1).max(200).optional(),
});

export type FacturasSearch = z.infer<typeof FacturasSearchSchema>;
