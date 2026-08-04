import { z } from 'zod';
import { EstadoAnticipo } from '@/features/facturacion/api/types';

/** Search params del reporte de Liquidación de caja. */
export const LiquidacionCajaSearchSchema = z.object({
  sucursalId: z.string().optional(),
  desde: z.string().optional(),
  hasta: z.string().optional(),
});

export type LiquidacionCajaSearch = z.infer<typeof LiquidacionCajaSearchSchema>;

/** Search params del reporte de Estados de facturas de anticipo. */
export const EstadosAnticiposSearchSchema = z.object({
  clienteId: z.string().optional(),
  estado: z
    .union([
      z.literal(EstadoAnticipo.Abierto),
      z.literal(EstadoAnticipo.Amortizado),
      z.literal(EstadoAnticipo.Cancelado),
    ])
    .optional(),
});

export type EstadosAnticiposSearch = z.infer<typeof EstadosAnticiposSearchSchema>;
