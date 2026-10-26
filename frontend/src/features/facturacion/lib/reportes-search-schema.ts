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

/** Vistas (pestañas) del Cierre del día por sucursal (F6.7). */
export const VISTAS_CIERRE_DIA = ['monitor', 'conciliacion'] as const;
export type VistaCierreDia = (typeof VISTAS_CIERRE_DIA)[number];

/**
 * Search params del Cierre del día por sucursal (F6.7). `z.guid()` acepta
 * cualquier GUID con forma 8-4-4-4-12 (incluye ids sembrados que no son
 * RFC 4122). Valores inválidos se descartan en lugar de romper la ruta.
 * Sin `dia` la pantalla usa hoy (hora local); sin `vista`, el monitor.
 */
export const CierreDiaSearchSchema = z.object({
  sucursalId: z.guid().optional().catch(undefined),
  dia: z
    .string()
    .regex(/^\d{4}-\d{2}-\d{2}$/)
    .optional()
    .catch(undefined),
  cajeroId: z.guid().optional().catch(undefined),
  vista: z.enum(VISTAS_CIERRE_DIA).optional().catch(undefined),
});

export type CierreDiaSearch = z.infer<typeof CierreDiaSearchSchema>;
