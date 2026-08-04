import { z } from 'zod';

/**
 * Schemas Zod de los forms del módulo Administración → Empresas.
 * Mirror de los validators FluentValidation backend
 * (<c>CrearEmpresaValidator</c>, <c>ActualizarEmpresaCommand</c>).
 *
 * <para><b>RFC mexicano</b>: 12 caracteres (persona moral) o 13
 * (persona física). El backend valida estrictamente <c>Length(12, 13)</c>;
 * acá agregamos un regex laxo para detectar inputs claramente
 * incorrectos (solo alfanumérico mayúscula). El backend rechaza shapes
 * más finos (homoclave, etc.) si llega a ser necesario.</para>
 */

const RFC_RE = /^[A-Z&Ñ0-9]{12,13}$/;

export const CrearEmpresaSchema = z.object({
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
  razonSocial: z
    .string()
    .trim()
    .min(1, 'Razón social requerida')
    .max(254, 'Máximo 254 caracteres'),
  regimenFiscal: z
    .string()
    .trim()
    .min(1, 'Régimen fiscal requerido')
    .max(10, 'Máximo 10 caracteres'),
  nombreComercial: z
    .string()
    .trim()
    .max(254, 'Máximo 254 caracteres')
    .nullable()
    .transform((v) => (v != null && v.length === 0 ? null : v)),
});

export type CrearEmpresaValues = z.infer<typeof CrearEmpresaSchema>;

/**
 * Schema para el PATCH /empresas/{id}. Todos los campos opcionales
 * porque el backend acepta payloads parciales; pero si el campo está
 * presente, mantenemos las mismas validaciones de longitud.
 */
export const ActualizarEmpresaSchema = z.object({
  razonSocial: z
    .string()
    .trim()
    .min(1, 'Razón social requerida')
    .max(254, 'Máximo 254 caracteres'),
  regimenFiscal: z
    .string()
    .trim()
    .min(1, 'Régimen fiscal requerido')
    .max(10, 'Máximo 10 caracteres'),
  nombreComercial: z
    .string()
    .trim()
    .max(254, 'Máximo 254 caracteres')
    .nullable()
    .transform((v) => (v != null && v.length === 0 ? null : v)),
  // IVA default para captura manual en Facturación (FAC-DET-PR2/PR3).
  tasaIvaDefault: z
    .number()
    .min(0, 'Mínimo 0')
    .max(1, 'Máximo 1 (fracción, ej. 0.16)')
    .nullable(),
  // CP fiscal = LugarExpedicion del CFDI 4.0 (F12-PR1). null = sin capturar
  // (la emisión de CFDI falla hasta capturarlo); el form mapea '' → null.
  codigoPostal: z
    .string()
    .trim()
    .regex(/^\d{5}$/, 'Código postal de 5 dígitos')
    .nullable(),
});

export type ActualizarEmpresaValues = z.infer<typeof ActualizarEmpresaSchema>;
