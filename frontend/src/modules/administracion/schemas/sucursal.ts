import { z } from 'zod';
import { TipoSucursal } from '@/modules/administracion/api/types';

/**
 * Schema Zod compartido para crear/actualizar Sucursal. Mirror de
 * <c>CrearSucursalValidator</c> backend (clave 1..20, nombre 1..254).
 *
 * <para>La clave se UPPERCASE-a para alinearse con la convención del
 * catálogo (ej. "MID", "MX", "CAN") — el backend no enforce el case
 * pero los selectores del módulo Compras asumen mayúsculas.</para>
 */
export const SucursalSchema = z.object({
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
  tipo: z.nativeEnum(TipoSucursal, {
    message: 'El tipo debe ser Taller o Planta',
  }),
  // Clave con la que A+W refiere la sucursal (ingesta de pedidos,
  // ADR-0048). Vacía/ausente = la sucursal no recibe pedidos de A+W.
  // .optional() y NO .default(''): el default hace divergir el tipo de
  // entrada del de salida y zodResolver deja de tipar con useForm.
  claveAw: z.string().trim().max(40, 'Máximo 40 caracteres').optional(),
});

export type SucursalValues = z.infer<typeof SucursalSchema>;
