import { z } from 'zod';

/**
 * Schema Zod compartido para crear/actualizar Departamento. Mirror de
 * <c>CrearDepartamentoValidator</c> backend (clave 1..20, nombre
 * 1..254). Igual que sucursales: la clave se uppercase-a por
 * convención del catálogo.
 */
export const DepartamentoSchema = z.object({
  clave: z
    .string()
    .trim()
    .toUpperCase()
    .min(1, 'Clave requerida')
    .max(20, 'Máximo 20 caracteres'),
  nombre: z
    .string()
    .trim()
    .min(1, 'Nombre requerido')
    .max(254, 'Máximo 254 caracteres'),
});

export type DepartamentoValues = z.infer<typeof DepartamentoSchema>;
