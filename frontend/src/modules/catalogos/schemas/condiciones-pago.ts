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

const diasSchema = z
  .number()
  .int('Días debe ser entero')
  .min(0, 'Mínimo 0')
  .max(365, 'Máximo 365');

export const CrearCondicionesPagoSchema = z.object({
  clave: claveSchema,
  nombre: nombreSchema,
  diasCredito: diasSchema,
});
export type CrearCondicionesPagoValues = z.infer<
  typeof CrearCondicionesPagoSchema
>;

export const ActualizarCondicionesPagoSchema = z.object({
  nombre: nombreSchema,
  diasCredito: diasSchema,
});
export type ActualizarCondicionesPagoValues = z.infer<
  typeof ActualizarCondicionesPagoSchema
>;
