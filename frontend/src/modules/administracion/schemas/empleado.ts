import { z } from 'zod';

/**
 * Schema Zod para crear/actualizar Empleado (ADM-FE-PR1). Mirror de
 * <c>CrearEmpleadoValidator</c> backend (clave 1..20, nombre 1..254,
 * email opcional válido ≤254, código de nómina ≤20). Los ids de
 * puesto/jefe/sucursal/departamento/usuario los ponen los selectores
 * (string vacío ≡ sin valor; se normaliza a null en el submit).
 */
export const EmpleadoSchema = z.object({
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
  email: z
    .string()
    .trim()
    .max(254, 'Máximo 254 caracteres')
    .refine((v) => v === '' || z.email().safeParse(v).success, {
      message: 'Email inválido',
    })
    .optional(),
  puestoId: z.string().optional(),
  jefeDirectoId: z.string().optional(),
  sucursalId: z.string().optional(),
  departamentoId: z.string().optional(),
  usuarioId: z.string().optional(),
  codigoNomina: z
    .string()
    .trim()
    .max(20, 'Máximo 20 caracteres')
    .optional(),
});

export type EmpleadoValues = z.infer<typeof EmpleadoSchema>;
