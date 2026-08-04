import { z } from 'zod';

/**
 * Schemas Zod del recurso Monedas. Mirror de los validators
 * FluentValidation backend (<c>CrearMonedaValidator</c>,
 * <c>ActualizarMonedaValidator</c>).
 */

const CODIGO_RE = /^[A-Z]{3}$/;

export const CrearMonedaSchema = z.object({
  codigo: z
    .string()
    .trim()
    .toUpperCase()
    .regex(CODIGO_RE, 'Código ISO de 3 letras (ej. MXN, USD)'),
  nombre: z
    .string()
    .trim()
    .min(1, 'Nombre requerido')
    .max(100, 'Máximo 100 caracteres'),
  decimales: z
    .number()
    .int('Decimales debe ser entero')
    .min(0, 'Mínimo 0')
    .max(6, 'Máximo 6'),
  activa: z.boolean(),
});

export type CrearMonedaValues = z.infer<typeof CrearMonedaSchema>;

export const ActualizarMonedaSchema = z.object({
  nombre: z
    .string()
    .trim()
    .min(1, 'Nombre requerido')
    .max(100, 'Máximo 100 caracteres'),
  decimales: z.number().int('Decimales debe ser entero').min(0).max(6),
  activa: z.boolean(),
});

export type ActualizarMonedaValues = z.infer<typeof ActualizarMonedaSchema>;
