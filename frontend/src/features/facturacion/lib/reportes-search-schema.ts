import { z } from 'zod';
import {
  EstadoAnticipo,
  EstadoComparativaAnticipo,
} from '@/features/facturacion/api/types';

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

/** Search params de la Comparativa de anticipos vs aplicaciones (F6.2). */
export const ComparativaAnticiposSearchSchema = z.object({
  sucursalId: z.string().optional(),
  clienteId: z.string().optional(),
  desde: z.string().optional(),
  hasta: z.string().optional(),
  corte: z.string().optional(),
  estado: z
    .union([
      z.literal(EstadoComparativaAnticipo.Pendiente),
      z.literal(EstadoComparativaAnticipo.Parcial),
      z.literal(EstadoComparativaAnticipo.Aplicado),
      z.literal(EstadoComparativaAnticipo.Cancelado),
    ])
    .optional(),
});

export type ComparativaAnticiposSearch = z.infer<
  typeof ComparativaAnticiposSearchSchema
>;
