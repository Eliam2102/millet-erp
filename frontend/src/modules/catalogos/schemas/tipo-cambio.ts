import { z } from 'zod';
import { OrigenTipoCambio } from '@/modules/catalogos/api/types';

/**
 * Schema Zod para registrar un tipo de cambio. Backend valida
 * <c>ValorEnMxn &gt; 0</c>; replica acá.
 */
const FECHA_ISO_RE = /^\d{4}-\d{2}-\d{2}$/;

export const RegistrarTipoCambioSchema = z.object({
  fecha: z
    .string()
    .regex(FECHA_ISO_RE, 'Fecha requerida (YYYY-MM-DD)'),
  valorEnMxn: z.number().gt(0, 'Debe ser mayor a 0'),
  origen: z.nativeEnum(OrigenTipoCambio),
});

export type RegistrarTipoCambioValues = z.infer<
  typeof RegistrarTipoCambioSchema
>;
