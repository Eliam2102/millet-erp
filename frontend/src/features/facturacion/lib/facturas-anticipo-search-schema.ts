import { z } from 'zod';
import { EstadoTimbrado } from '@/features/facturacion/api/types';

/**
 * Search params de la bandeja de facturas de anticipo
 * (<c>/facturacion/anticipos/facturas</c>, ANT-PR2 doc 13). Mismo shape que
 * <c>FacturasSearchSchema</c>: filtro por estado de timbrado (server-side) +
 * búsqueda libre client-side (folio / UUID / receptor) + bucket "Sin
 * asignar" de la Capa A.
 */
export const FacturasAnticipoSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoTimbrado.Borrador),
      z.literal(EstadoTimbrado.TimbradoEnProceso),
      z.literal(EstadoTimbrado.Timbrado),
      z.literal(EstadoTimbrado.TimbradoFallido),
      z.literal(EstadoTimbrado.CancelacionPendiente),
      z.literal(EstadoTimbrado.Cancelado),
      z.literal(EstadoTimbrado.Descartada),
    ])
    .optional(),
  q: z.string().optional(),
  alcance: z.literal('sin-asignar').optional(),
  offset: z.number().int().min(0).optional(),
  limit: z.number().int().min(1).max(200).optional(),
});

export type FacturasAnticipoSearch = z.infer<typeof FacturasAnticipoSearchSchema>;
