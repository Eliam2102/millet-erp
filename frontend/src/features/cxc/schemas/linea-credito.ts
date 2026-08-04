import { z } from 'zod';
import {
  CLASIFICACIONES_CREDITO,
  MONEDAS_LINEA_CREDITO,
  OrigenLineaCredito,
} from '@/features/cxc/api/types';

/**
 * Schemas de los forms de línea de crédito (CXC-FE-PR2). Espejo de las
 * validaciones del backend (<c>CrearLineaCreditoValidator</c> /
 * <c>ActualizarLineaCreditoValidator</c>): límite &gt; 0, plazo 1..365,
 * clasificación A/B/C/E opcional, moneda MXN/USD.
 */
const origenValues = Object.values(OrigenLineaCredito) as [number, ...number[]];

/** Sentinel del select de clasificación — se traduce a null al enviar. */
export const SIN_CLASIFICACION = '__sin__';

export const NuevaLineaCreditoSchema = z.object({
  clienteId: z.string().uuid('Selecciona un cliente'),
  moneda: z.enum(MONEDAS_LINEA_CREDITO),
  limite: z.number().positive('El límite de crédito debe ser > 0'),
  origen: z
    .number()
    .refine((v) => origenValues.includes(v), 'Selecciona el origen del límite'),
  plazoDias: z
    .number()
    .int('Días enteros')
    .positive('El plazo debe ser > 0')
    .max(365, 'El plazo máximo es 365 días'),
  clasificacion: z.enum(CLASIFICACIONES_CREDITO).nullable(),
});

export type NuevaLineaCreditoValues = z.infer<typeof NuevaLineaCreditoSchema>;

export const EditarLineaCreditoSchema = NuevaLineaCreditoSchema.pick({
  limite: true,
  plazoDias: true,
  clasificacion: true,
});

export type EditarLineaCreditoValues = z.infer<typeof EditarLineaCreditoSchema>;
