import { z } from 'zod';
import { Naturaleza } from '@/modules/datos-maestros/api/types';

/**
 * Schemas Zod del recurso Artículos. Mirror de los validators
 * FluentValidation backend (<c>CrearArticuloValidator</c>,
 * <c>ActualizarArticuloValidator</c>).
 */

const naturalezaSchema = z.nativeEnum(Naturaleza);
const MONEDA_RE = /^[A-Z]{3}$/;

export const CrearArticuloSchema = z.object({
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
  unidadMedidaId: z.string().min(1, 'Selecciona una unidad de medida'),
  naturaleza: naturalezaSchema,
  descripcionLarga: z
    .string()
    .trim()
    .nullable()
    .transform((v) => (v != null && v.length === 0 ? null : v)),
  // FK del catálogo de categorías (ADR-0046 PR2). null = sin categoría / no
  // reconciliado. El nombre legacy viaja en el read DTO (para el cold value).
  categoriaId: z.string().nullable(),
  precioReferenciaMonto: z
    .number()
    .min(0, 'Mínimo 0')
    .nullable(),
  precioReferenciaMoneda: z
    .string()
    .trim()
    .toUpperCase()
    .regex(MONEDA_RE, 'Código ISO de 3 letras (ej. MXN, USD)')
    .nullable()
    .or(z.literal('').transform(() => null)),
});

export type CrearArticuloValues = z.infer<typeof CrearArticuloSchema>;

export const ActualizarArticuloSchema = z.object({
  nombre: z
    .string()
    .trim()
    .min(1, 'Nombre requerido')
    .max(254, 'Máximo 254 caracteres'),
  // Reasignación de unidad (FK). null = no tocar / artículo legacy sin asignar.
  unidadMedidaId: z.string().nullable(),
  naturaleza: naturalezaSchema,
  descripcionLarga: z
    .string()
    .trim()
    .nullable()
    .transform((v) => (v != null && v.length === 0 ? null : v)),
  // FK del catálogo de categorías (ADR-0046 PR2). null = sin categoría / no
  // reconciliado. El nombre legacy viaja en el read DTO (para el cold value).
  categoriaId: z.string().nullable(),
  precioReferenciaMonto: z
    .number()
    .min(0, 'Mínimo 0')
    .nullable(),
  precioReferenciaMoneda: z
    .string()
    .trim()
    .toUpperCase()
    .regex(MONEDA_RE, 'Código ISO de 3 letras (ej. MXN, USD)')
    .nullable()
    .or(z.literal('').transform(() => null)),
});

export type ActualizarArticuloValues = z.infer<typeof ActualizarArticuloSchema>;
