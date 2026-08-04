import { z } from 'zod';
import { EstatusCatalogo } from '@/features/almacen/api/types';

/**
 * Regex laxo de UUID (8-4-4-4-12 hex) — los seeds del backend usan
 * GUIDs deterministas que no califican como v1-v8 estricto. Mismo
 * patrón que <c>compras/schemas/crear-requisicion.ts</c>.
 */
const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Schema Zod del comando <c>POST /api/v1/almacen/almacenes</c>.
 * Mirror de <c>CrearAlmacenCommand</c> (backend F1-PR1). Solo
 * validación estructural: la unicidad de <c>clave</c> y la
 * existencia/estado de <c>sucursalId</c> las decide el backend.
 */
export const CrearAlmacenSchema = z.object({
  clave: z
    .string()
    .min(1, 'La clave es requerida')
    .max(20, 'Máximo 20 caracteres'),
  nombre: z
    .string()
    .min(1, 'El nombre es requerido')
    .max(254, 'Máximo 254 caracteres'),
  sucursalId: z.string().regex(UUID_SHAPE_RE, 'Sucursal requerida'),
  estatus: z.union([
    z.literal(EstatusCatalogo.Activo),
    z.literal(EstatusCatalogo.Inactivo),
    z.literal(EstatusCatalogo.Borrador),
  ]),
});

export type CrearAlmacenValues = z.infer<typeof CrearAlmacenSchema>;

/**
 * Schema Zod del comando <c>PATCH /api/v1/almacen/almacenes/{id}</c>.
 * Mirror de <c>EditarAlmacenCommand</c>. El <c>id</c> no va en el
 * form (sale de la URL/contexto del item editado).
 */
export const EditarAlmacenSchema = CrearAlmacenSchema;

export type EditarAlmacenValues = z.infer<typeof EditarAlmacenSchema>;
