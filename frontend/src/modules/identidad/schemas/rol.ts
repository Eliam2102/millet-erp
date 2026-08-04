import { z } from 'zod';

/**
 * Schemas Zod de los forms del módulo Identidad → Roles. Mirror de los
 * validators FluentValidation backend (<c>CrearRolValidator</c>,
 * <c>ActualizarRolValidator</c>).
 *
 * <para><b>Codigo</b>: 1..64, kebab-lowercase comenzando con letra
 * (regex <c>^[a-z][a-z0-9-]*$</c>). Es el natural-key del dominio de
 * identidad — no editable después de crear el rol.</para>
 *
 * <para><b>Nombre</b>: 1..100. <b>Descripcion</b>: 0..500.</para>
 */

const CODIGO_RE = /^[a-z][a-z0-9-]*$/;

export const CrearRolSchema = z.object({
  codigo: z
    .string()
    .trim()
    .toLowerCase()
    .min(1, 'Código requerido')
    .max(64, 'Máximo 64 caracteres')
    .regex(
      CODIGO_RE,
      'Solo letras minúsculas, dígitos y guiones; debe comenzar con letra',
    ),
  nombre: z
    .string()
    .trim()
    .min(1, 'Nombre requerido')
    .max(100, 'Máximo 100 caracteres'),
  descripcion: z
    .string()
    .trim()
    .max(500, 'Máximo 500 caracteres')
    .nullable()
    .transform((v) => (v != null && v.length === 0 ? null : v)),
});

export type CrearRolValues = z.infer<typeof CrearRolSchema>;

/**
 * Schema para el PATCH /roles/{id}. El backend acepta payloads
 * parciales, pero acá enviamos siempre Nombre + Descripcion. El
 * <c>Codigo</c> no se edita.
 */
export const ActualizarRolSchema = z.object({
  nombre: z
    .string()
    .trim()
    .min(1, 'Nombre requerido')
    .max(100, 'Máximo 100 caracteres'),
  descripcion: z
    .string()
    .trim()
    .max(500, 'Máximo 500 caracteres')
    .nullable()
    .transform((v) => (v != null && v.length === 0 ? null : v)),
});

export type ActualizarRolValues = z.infer<typeof ActualizarRolSchema>;
