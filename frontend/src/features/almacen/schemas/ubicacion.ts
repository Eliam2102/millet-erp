import { z } from 'zod';

const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Schema Zod del form de ubicación N4 (racks/pasillos, ADR-0047 PR C7.1).
 * Sirve para crear y editar: el sub-almacén solo aplica al crear (una ubicación
 * no se muda; al editar el campo va disabled y el backend conserva su padre). El
 * estatus NO va en el form — una ubicación nueva nace Activa y la baja/reactivación
 * va por sus endpoints dedicados. Unicidad de <c>(subAlmacenId, clave)</c> la
 * decide el backend. Molde <c>CrearSubAlmacenSchema</c>.
 */
export const UbicacionFormSchema = z.object({
  subAlmacenId: z.string().regex(UUID_SHAPE_RE, 'Sub-almacén requerido'),
  clave: z
    .string()
    .min(1, 'La clave es requerida')
    .max(20, 'Máximo 20 caracteres'),
  nombre: z
    .string()
    .min(1, 'El nombre es requerido')
    .max(254, 'Máximo 254 caracteres'),
});

export type UbicacionFormValues = z.infer<typeof UbicacionFormSchema>;
