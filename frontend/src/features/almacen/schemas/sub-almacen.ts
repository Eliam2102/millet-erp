import { z } from 'zod';
import {
  EstatusCatalogo,
  TipoSubAlmacen,
} from '@/features/almacen/api/types';

const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Schema Zod del comando <c>POST /api/v1/almacen/sub-almacenes</c>.
 * Mirror de <c>CrearSubAlmacenCommand</c> (backend F1-PR1). La
 * unicidad de <c>(almacenId, clave)</c> la decide el backend.
 */
export const CrearSubAlmacenSchema = z.object({
  almacenId: z.string().regex(UUID_SHAPE_RE, 'Almacén requerido'),
  clave: z
    .string()
    .min(1, 'La clave es requerida')
    .max(20, 'Máximo 20 caracteres'),
  nombre: z
    .string()
    .min(1, 'El nombre es requerido')
    .max(254, 'Máximo 254 caracteres'),
  tipo: z.union([
    z.literal(TipoSubAlmacen.Insumos),
    z.literal(TipoSubAlmacen.MaterialesDirectos),
    z.literal(TipoSubAlmacen.MaterialEnRevision),
    z.literal(TipoSubAlmacen.Transitorio),
  ]),
  estatus: z.union([
    z.literal(EstatusCatalogo.Activo),
    z.literal(EstatusCatalogo.Inactivo),
    z.literal(EstatusCatalogo.Borrador),
  ]),
});

export type CrearSubAlmacenValues = z.infer<typeof CrearSubAlmacenSchema>;

/**
 * Schema Zod del comando <c>PATCH /api/v1/almacen/sub-almacenes/{id}</c>.
 * No incluye <c>almacenId</c> (no se puede mover un sub-almacén a
 * otro almacén; el backend lo conserva del registro).
 */
export const EditarSubAlmacenSchema = z.object({
  clave: z
    .string()
    .min(1, 'La clave es requerida')
    .max(20, 'Máximo 20 caracteres'),
  nombre: z
    .string()
    .min(1, 'El nombre es requerido')
    .max(254, 'Máximo 254 caracteres'),
  tipo: z.union([
    z.literal(TipoSubAlmacen.Insumos),
    z.literal(TipoSubAlmacen.MaterialesDirectos),
    z.literal(TipoSubAlmacen.MaterialEnRevision),
    z.literal(TipoSubAlmacen.Transitorio),
  ]),
  estatus: z.union([
    z.literal(EstatusCatalogo.Activo),
    z.literal(EstatusCatalogo.Inactivo),
    z.literal(EstatusCatalogo.Borrador),
  ]),
});

export type EditarSubAlmacenValues = z.infer<typeof EditarSubAlmacenSchema>;
