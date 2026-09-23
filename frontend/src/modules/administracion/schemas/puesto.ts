import { z } from 'zod';

/**
 * Schema Zod para crear/actualizar Puesto (ADM-FE-PR1). Mirror de
 * <c>CrearPuestoValidator</c> backend (clave 1..20, nombre 1..254).
 * La clave es business key inmutable: en modo editar el input va
 * disabled y el PATCH solo manda nombre.
 */
export const PuestoSchema = z.object({
  clave: z
    .string()
    .trim()
    .min(1, 'Clave requerida')
    .max(20, 'Máximo 20 caracteres'),
  nombre: z
    .string()
    .trim()
    .min(1, 'Nombre requerido')
    .max(254, 'Máximo 254 caracteres'),
  rolSugeridoId: z.string().nullable().optional(),
  departamentoId: z.string().nullable().optional(),
});

export type PuestoValues = z.infer<typeof PuestoSchema>;
