import { z } from 'zod';

/**
 * Search params de los reportes de Tesorería (TES-FE-PR6). Fechas como
 * DateOnly ISO; la cuenta es opcional en flujo de efectivo y requerida
 * (a nivel UI) en el auxiliar.
 */
export const ReportesTesoreriaSearchSchema = z.object({
  desde: z.string().optional(),
  hasta: z.string().optional(),
  cuentaBancariaId: z.string().optional(),
  moneda: z.string().length(3).optional(),
});

export type ReportesTesoreriaSearch = z.infer<typeof ReportesTesoreriaSearchSchema>;
