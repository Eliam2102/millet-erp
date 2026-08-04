import { z } from 'zod';

const nombreSchema = z
  .string()
  .trim()
  .min(1, 'Nombre requerido')
  .max(100, 'Máximo 100 caracteres');

export const CrearCategoriaArticuloSchema = z.object({
  nombre: nombreSchema,
});
export type CrearCategoriaArticuloValues = z.infer<
  typeof CrearCategoriaArticuloSchema
>;

export const ActualizarCategoriaArticuloSchema = z.object({
  nombre: nombreSchema,
});
export type ActualizarCategoriaArticuloValues = z.infer<
  typeof ActualizarCategoriaArticuloSchema
>;
