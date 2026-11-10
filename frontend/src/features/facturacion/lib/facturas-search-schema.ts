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
      z.literal(EstadoTimbrado.PorAutorizar),
    ])
    .optional()
    // Prueba manual S3-4 (M17): un estado desconocido en la URL cae a «Todos» en vez de romper la página.
    .catch(undefined),
  q: z.string().optional(),
  // Bucket "Sin asignar" de la Capa A (solo caja.leer-todas, CAJAS-PR6).
  alcance: z.literal('sin-asignar').optional(),
  /** F6.1: clase del pedido manual de origen (server-side). */
  clase: z.enum(['VentaDirecta', 'Emergencia']).optional(),
  /** U-1: solo facturas con una nota de crédito pendiente de timbrar (server-side). */
  ncPendiente: z.boolean().optional(),
  offset: z.number().int().min(0).optional(),
  limit: z.number().int().min(1).max(200).optional(),
});

export type FacturasSearch = z.infer<typeof FacturasSearchSchema>;
