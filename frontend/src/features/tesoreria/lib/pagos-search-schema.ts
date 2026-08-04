import { z } from 'zod';

/**
 * Search params de la bandeja de pasivos pendientes
 * (<c>/tesoreria/pagos</c>, P2). Filtros server-side del
 * <c>BandejaPasivosPendientesQuery</c>; <c>q</c> se aplica client-side
 * (razón social / clave / folio del proveedor).
 */
export const PagosSearchSchema = z.object({
  moneda: z.string().length(3).optional(),
  venceDesde: z.string().optional(),
  venceHasta: z.string().optional(),
  q: z.string().optional(),
  offset: z.number().int().min(0).optional(),
  limit: z.number().int().min(1).max(500).optional(),
});

export type PagosSearch = z.infer<typeof PagosSearchSchema>;
