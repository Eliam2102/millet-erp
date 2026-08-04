import { z } from 'zod';
import { MONEDAS_LINEA_CREDITO } from '@/features/cxc/api/types';

/**
 * Search params de los reportes de cartera (CXC-FE-PR5).
 */

/** Antigüedad de saldos (<c>/cxc/cartera</c>). */
export const CarteraSearchSchema = z.object({
  /** "yyyy-MM-dd"; omitido = hoy (default backend). */
  fechaCorte: z.string().optional(),
  clienteId: z.string().optional(),
  moneda: z.enum(MONEDAS_LINEA_CREDITO).optional(),
});
export type CarteraSearch = z.infer<typeof CarteraSearchSchema>;

/** Estado de cuenta (<c>/cxc/estado-cuenta</c>) — cliente obligatorio. */
export const EstadoCuentaSearchSchema = z.object({
  clienteId: z.string().optional(),
});
export type EstadoCuentaSearch = z.infer<typeof EstadoCuentaSearchSchema>;

/** Anticipos (<c>/cxc/anticipos</c>) — cliente obligatorio en el endpoint. */
export const AnticiposCxcSearchSchema = z.object({
  clienteId: z.string().optional(),
});
export type AnticiposCxcSearch = z.infer<typeof AnticiposCxcSearchSchema>;
