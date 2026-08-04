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
  .max(254, 'Máximo 254 caracteres');

const emailSchema = z
  .string()
  .trim()
  .email('Email inválido')
  .max(254, 'Máximo 254 caracteres')
  .nullable()
  .or(z.literal('').transform(() => null));

const telefonoSchema = z
  .string()
  .trim()
  .max(50, 'Máximo 50 caracteres')
  .nullable()
  .or(z.literal('').transform(() => null));

export const CrearTransportistaSchema = z.object({
  clave: claveSchema,
  nombre: nombreSchema,
  email: emailSchema,
  telefono: telefonoSchema,
});
export type CrearTransportistaValues = z.infer<typeof CrearTransportistaSchema>;

export const ActualizarTransportistaSchema = z.object({
  nombre: nombreSchema,
  email: emailSchema,
  telefono: telefonoSchema,
});
export type ActualizarTransportistaValues = z.infer<
  typeof ActualizarTransportistaSchema
>;
