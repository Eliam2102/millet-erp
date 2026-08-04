import { z } from 'zod';

const claveSchema = z
  .string()
  .trim()
  .min(1, 'Clave requerida')
  .max(20, 'Máximo 20 caracteres');

const nombreSchema = z
  .string()
  .trim()
  .min(1, 'Nombre requerido')
  .max(100, 'Máximo 100 caracteres');

export const CrearUsoPrincipalSchema = z.object({
  clave: claveSchema,
  nombre: nombreSchema,
});
export type CrearUsoPrincipalValues = z.infer<typeof CrearUsoPrincipalSchema>;

export const ActualizarUsoPrincipalSchema = z.object({
  nombre: nombreSchema,
});
export type ActualizarUsoPrincipalValues = z.infer<
  typeof ActualizarUsoPrincipalSchema
>;
