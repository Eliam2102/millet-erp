import { z } from 'zod';

/**
 * Schemas Zod del catálogo de unidades de medida (ADR-0046 Etapa 1a). La
 * validación estructural (decimales 0..6, factor &gt; 0, dimensión 0..4)
 * espeja los CHECK del DDL; el guardrail de "en uso" vive en el backend.
 */

const codigo = z
  .string()
  .trim()
  .toUpperCase()
  .min(1, 'Código requerido')
  .max(20, 'Máximo 20 caracteres');
const nombre = z
  .string()
  .trim()
  .min(1, 'Nombre requerido')
  .max(100, 'Máximo 100 caracteres');
const dimension = z.number()
  .int()
  .min(0)
  .max(4, 'Dimensión inválida');
const factorABase = z.number()
  .positive('El factor debe ser mayor a 0');
const decimales = z.number()
  .int('Debe ser entero')
  .min(0, 'Mínimo 0')
  .max(6, 'Máximo 6 decimales');

export const CrearUnidadMedidaSchema = z.object({
  codigo,
  nombre,
  dimension,
  factorABase,
  decimales,
  esBase: z.boolean(),
});
export type CrearUnidadMedidaValues = z.infer<typeof CrearUnidadMedidaSchema>;

export const ActualizarUnidadMedidaSchema = z.object({
  nombre,
  dimension,
  factorABase,
  decimales,
  esBase: z.boolean(),
});
export type ActualizarUnidadMedidaValues = z.infer<
  typeof ActualizarUnidadMedidaSchema
>;
