import { z } from 'zod';

const CODIGO_RE = /^[A-Z]{2,4}$/;

export const CrearIncotermSchema = z.object({
  codigo: z
    .string()
    .trim()
    .toUpperCase()
    .regex(CODIGO_RE, '2-4 letras mayúsculas'),
  nombre: z
    .string()
    .trim()
    .min(1, 'Nombre requerido')
    .max(100, 'Máximo 100 caracteres'),
});
export type CrearIncotermValues = z.infer<typeof CrearIncotermSchema>;

export const ActualizarIncotermSchema = z.object({
  nombre: z
    .string()
    .trim()
    .min(1, 'Nombre requerido')
    .max(100, 'Máximo 100 caracteres'),
});
export type ActualizarIncotermValues = z.infer<typeof ActualizarIncotermSchema>;
