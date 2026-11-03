import { z } from 'zod';
import { OrigenProveedor, TipoPersonaProveedor } from '@/modules/datos-maestros/api/types';

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

/** Clave de A+W: entero positivo opcional (mirror del validator backend). */
const claveAwSchema = z
  .number('Escribe un número entero')
  .int('Debe ser un número entero')
  .positive('Debe ser mayor que 0')
  .nullable();

const tipoPersonaSchema = z.nativeEnum(TipoPersonaProveedor);

/**
 * Origen del proveedor dentro del form (null = por revisar). Un proveedor
 * <b>extranjero</b> no tiene RFC: puede quedar vacío y el backend guarda el
 * genérico <c>XEXX010101000</c> (86e3pdmee · ADM-01).
 */
const origenSchema = z.nativeEnum(OrigenProveedor).nullable();

/** Valida el RFC salvo que sea extranjero y esté vacío. */
function validarRfc(rfc: string, origen: OrigenProveedor | null, ctx: z.RefinementCtx) {
  if (rfc === '' && origen === OrigenProveedor.Extranjero) return;
  if (rfc.length < 12 || rfc.length > 13) {
    ctx.addIssue({
      code: 'custom',
      path: ['rfc'],
      message: 'El RFC debe tener 12 o 13 caracteres',
    });
  } else if (!RFC_RE.test(rfc)) {
    ctx.addIssue({
      code: 'custom',
      path: ['rfc'],
      message: 'Formato de RFC inválido (solo mayúsculas, dígitos, & y Ñ)',
    });
  }
}

export const CrearProveedorSchema = z
  .object({
    calle: z.string().max(254, 'Máximo 254 caracteres').optional(),
    numeroExterior: z.string().max(20, 'Máximo 20 caracteres').optional(),
    numeroInterior: z.string().max(20, 'Máximo 20 caracteres').optional(),
    colonia: z.string().max(254, 'Máximo 254 caracteres').optional(),
    ciudad: z.string().max(100, 'Máximo 100 caracteres').optional(),
    municipio: z.string().max(100, 'Máximo 100 caracteres').optional(),
    estado: z.string().max(100, 'Máximo 100 caracteres').optional(),
    codigoPostal: z.string().max(20, 'Máximo 20 caracteres').optional(),
    pais: z.string().regex(/^$|^[A-Za-z]{3}$/, 'Escribe el código ISO de tres letras, por ejemplo MEX').optional(),
    taxId: z.string().max(40, 'Máximo 40 caracteres').optional(),
    claveAw: claveAwSchema,

    clave: z.string().trim().min(1, 'Clave requerida').max(20, 'Máximo 20 caracteres'),
    razonSocial: z
      .string()
      .trim()
      .min(1, 'Razón social requerida')
      .max(254, 'Máximo 254 caracteres'),
    rfc: z.string().trim().toUpperCase(),
    origen: origenSchema,
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
  })
  .superRefine((v, ctx) => validarRfc(v.rfc, v.origen, ctx));

export type CrearProveedorValues = z.infer<typeof CrearProveedorSchema>;

/**
 * Schema del PATCH /catalogos/proveedores/{id}. Inmutables: clave.
 * Mantiene las mismas reglas de longitud que el alta — el backend
 * acepta payload parcial pero si el campo está, debe validar.
 */
export const ActualizarProveedorSchema = z
  .object({
    calle: z.string().max(254, 'Máximo 254 caracteres').optional(),
    numeroExterior: z.string().max(20, 'Máximo 20 caracteres').optional(),
    numeroInterior: z.string().max(20, 'Máximo 20 caracteres').optional(),
    colonia: z.string().max(254, 'Máximo 254 caracteres').optional(),
    ciudad: z.string().max(100, 'Máximo 100 caracteres').optional(),
    municipio: z.string().max(100, 'Máximo 100 caracteres').optional(),
    estado: z.string().max(100, 'Máximo 100 caracteres').optional(),
    codigoPostal: z.string().max(20, 'Máximo 20 caracteres').optional(),
    pais: z.string().regex(/^$|^[A-Za-z]{3}$/, 'Escribe el código ISO de tres letras, por ejemplo MEX').optional(),
    taxId: z.string().max(40, 'Máximo 40 caracteres').optional(),
    claveAw: claveAwSchema,

    razonSocial: z
      .string()
      .trim()
      .min(1, 'Razón social requerida')
      .max(254, 'Máximo 254 caracteres'),
    rfc: z.string().trim().toUpperCase(),
    origen: origenSchema,
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
  })
  .superRefine((v, ctx) => validarRfc(v.rfc, v.origen, ctx));

export type ActualizarProveedorValues = z.infer<typeof ActualizarProveedorSchema>;
