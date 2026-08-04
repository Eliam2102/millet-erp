import { z } from 'zod';

/**
 * Schemas Zod del recurso Clientes (ADR-0048). Mirror de los validators
 * FluentValidation backend (<c>CrearClienteValidator</c>,
 * <c>ActualizarClienteValidator</c>).
 *
 * <para>Los datos fiscales (RFC, régimen, CP) son <b>opcionales</b> en
 * alta y edición — su ausencia NO bloquea el guardado, bloquea el
 * timbrado (bandeja "Fiscales incompletos"). Si el campo viene, debe
 * ser válido.</para>
 */

const RFC_RE = /^[A-Z&Ñ0-9]{12,13}$/;
const REGIMEN_RE = /^\d{3}$/;
const CP_RE = /^\d{5}$/;
const FORMA_PAGO_RE = /^\d{2}$/;
const USO_CFDI_RE = /^[A-Z0-9]{3,4}$/;
const MONEDA_RE = /^[A-Z]{3}$/;

const rfcOpcional = z
  .string()
  .trim()
  .toUpperCase()
  .regex(
    RFC_RE,
    'RFC inválido: 12 o 13 caracteres (mayúsculas, dígitos, & y Ñ)',
  )
  .nullable()
  .or(z.literal('').transform(() => null));

const regimenOpcional = z
  .string()
  .trim()
  .regex(REGIMEN_RE, 'Clave SAT de 3 dígitos (ej. 601)')
  .nullable()
  .or(z.literal('').transform(() => null));

const cpOpcional = z
  .string()
  .trim()
  .regex(CP_RE, 'Código postal de 5 dígitos')
  .nullable()
  .or(z.literal('').transform(() => null));

const usoCfdiOpcional = z
  .string()
  .trim()
  .toUpperCase()
  .regex(USO_CFDI_RE, 'Clave SAT c_UsoCFDI (ej. G03, S01)')
  .nullable()
  .or(z.literal('').transform(() => null));

const formaPagoOpcional = z
  .string()
  .trim()
  .regex(FORMA_PAGO_RE, 'Clave SAT c_FormaPago de 2 dígitos (ej. 03)')
  .nullable()
  .or(z.literal('').transform(() => null));

const metodoPagoOpcional = z.enum(['PUE', 'PPD']).nullable();

const emailOpcional = z
  .string()
  .trim()
  .max(254, 'Máximo 254 caracteres')
  .email('Email inválido')
  .nullable()
  .or(z.literal('').transform(() => null));

const telefonoOpcional = z
  .string()
  .trim()
  .max(50, 'Máximo 50 caracteres')
  .nullable()
  .transform((v) => (v != null && v.length === 0 ? null : v));

// Receptor extranjero (CCE) — todos opcionales.
const numRegIdTribOpcional = z
  .string()
  .trim()
  .max(40, 'Máximo 40 caracteres')
  .nullable()
  .transform((v) => (v != null && v.length === 0 ? null : v));

const paisResidenciaOpcional = z
  .string()
  .trim()
  .toUpperCase()
  .regex(/^[A-Z]{3}$/, 'Clave SAT c_Pais (ISO alfa-3, ej. USA)')
  .nullable()
  .or(z.literal('').transform(() => null));

const textoExtranjeroOpcional = (max: number) =>
  z
    .string()
    .trim()
    .max(max, `Máximo ${max} caracteres`)
    .nullable()
    .transform((v) => (v != null && v.length === 0 ? null : v));

export const CrearClienteSchema = z.object({
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
  referenciaExterna: z
    .string()
    .trim()
    .max(50, 'Máximo 50 caracteres')
    .nullable()
    .transform((v) => (v != null && v.length === 0 ? null : v)),
  rfc: rfcOpcional,
  regimenFiscal: regimenOpcional,
  codigoPostalFiscal: cpOpcional,
  email: emailOpcional,
  telefono: telefonoOpcional,
});

export type CrearClienteValues = z.infer<typeof CrearClienteSchema>;

/**
 * Schema del PATCH /datos-maestros/clientes/{id}. Inmutables: clave,
 * referenciaExterna (correlación A+W) y origen. Los nullables vacíos
 * viajan con su flag <c>limpiarX</c> (mapping en el form).
 */
export const ActualizarClienteSchema = z.object({
  razonSocial: z
    .string()
    .trim()
    .min(1, 'Razón social requerida')
    .max(254, 'Máximo 254 caracteres'),
  rfc: rfcOpcional,
  regimenFiscal: regimenOpcional,
  codigoPostalFiscal: cpOpcional,
  usoCfdiDefault: usoCfdiOpcional,
  formaPagoDefault: formaPagoOpcional,
  metodoPagoDefault: metodoPagoOpcional,
  monedaDefault: z
    .string()
    .trim()
    .toUpperCase()
    .regex(MONEDA_RE, 'Código ISO de 3 letras (ej. MXN, USD)'),
  esGenerico: z.boolean(),
  email: emailOpcional,
  telefono: telefonoOpcional,
  numRegIdTrib: numRegIdTribOpcional,
  paisResidencia: paisResidenciaOpcional,
  domicilioExtranjeroCalle: textoExtranjeroOpcional(200),
  domicilioExtranjeroEstado: textoExtranjeroOpcional(100),
  domicilioExtranjeroCodigoPostal: textoExtranjeroOpcional(12),
});

export type ActualizarClienteValues = z.infer<typeof ActualizarClienteSchema>;
