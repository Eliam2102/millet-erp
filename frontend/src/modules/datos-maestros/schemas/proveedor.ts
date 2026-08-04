import { z } from 'zod';
import { TipoPersonaProveedor } from '@/modules/datos-maestros/api/types';

/**
 * Schemas Zod del recurso Proveedores. Mirror de los validators
 * FluentValidation backend (<c>CrearProveedorValidator</c>,
 * <c>ActualizarProveedorValidator</c>).
 *
 * <para><b>RFC mexicano</b>: 12 caracteres (persona moral) o 13
 * (persona física). El backend valida estrictamente
 * <c>Length(12, 13)</c>; acá agregamos un regex laxo para detectar
 * inputs claramente incorrectos (alfanumérico mayúscula + & y Ñ).</para>
 */

const RFC_RE = /^[A-Z&Ñ0-9]{12,13}$/;

const tipoPersonaSchema = z.nativeEnum(TipoPersonaProveedor);

export const CrearProveedorSchema = z.object({
  clave: z
    .string()
    .trim()
    .min(1, 'Clave requerida')
    .max(20, 'Máximo 20 caracteres'),
  razonSocial: z
    .string()
    .trim()
    .min(1, 'Razón social requerida')
    .max(254, 'Máximo 254 caracteres'),
  rfc: z
    .string()
    .trim()
    .toUpperCase()
    .min(12, 'El RFC debe tener 12 o 13 caracteres')
    .max(13, 'El RFC debe tener 12 o 13 caracteres')
    .regex(
      RFC_RE,
      'Formato de RFC inválido (solo mayúsculas, dígitos, & y Ñ)',
    ),
  tipoPersona: tipoPersonaSchema,
  nombreComercial: z
    .string()
    .trim()
    .max(254, 'Máximo 254 caracteres')
    .nullable()
    .transform((v) => (v != null && v.length === 0 ? null : v)),
  email: z
    .string()
    .trim()
    .max(254, 'Máximo 254 caracteres')
    .email('Email inválido')
    .nullable()
    .or(z.literal('').transform(() => null)),
  telefono: z
    .string()
    .trim()
    .max(50, 'Máximo 50 caracteres')
    .nullable()
    .transform((v) => (v != null && v.length === 0 ? null : v)),
  condicionesPagoDias: z
    .number()
    .int()
    .min(0, 'Mínimo 0 días')
    .max(365, 'Máximo 365 días')
    .nullable(),
});

export type CrearProveedorValues = z.infer<typeof CrearProveedorSchema>;

/**
 * Schema del PATCH /catalogos/proveedores/{id}. Inmutables: clave.
 * Mantiene las mismas reglas de longitud que el alta — el backend
 * acepta payload parcial pero si el campo está, debe validar.
 */
export const ActualizarProveedorSchema = z.object({
  razonSocial: z
    .string()
    .trim()
    .min(1, 'Razón social requerida')
    .max(254, 'Máximo 254 caracteres'),
  rfc: z
    .string()
    .trim()
    .toUpperCase()
    .min(12, 'El RFC debe tener 12 o 13 caracteres')
    .max(13, 'El RFC debe tener 12 o 13 caracteres')
    .regex(
      RFC_RE,
      'Formato de RFC inválido (solo mayúsculas, dígitos, & y Ñ)',
    ),
  tipoPersona: tipoPersonaSchema,
  nombreComercial: z
    .string()
    .trim()
    .max(254, 'Máximo 254 caracteres')
    .nullable()
    .transform((v) => (v != null && v.length === 0 ? null : v)),
  email: z
    .string()
    .trim()
    .max(254, 'Máximo 254 caracteres')
    .email('Email inválido')
    .nullable()
    .or(z.literal('').transform(() => null)),
  telefono: z
    .string()
    .trim()
    .max(50, 'Máximo 50 caracteres')
    .nullable()
    .transform((v) => (v != null && v.length === 0 ? null : v)),
  condicionesPagoDias: z
    .number()
    .int()
    .min(0, 'Mínimo 0 días')
    .max(365, 'Máximo 365 días')
    .nullable(),
});

export type ActualizarProveedorValues = z.infer<
  typeof ActualizarProveedorSchema
>;
