import { z } from 'zod';

/**
 * Schema del form de configuración del PAC (PR-8). Mirror del
 * <c>GuardarConfiguracionPacValidator</c> del backend.
 *
 * <para><b>Nota</b>: <c>apiKey</c> NO vive en el schema — se maneja con
 * estado local del componente (input controlado) porque tiene
 * semántica especial (null = no rotar). El componente la inyecta al
 * payload al momento del submit.</para>
 */
/**
 * Identidad de prueba de la LCO sintética del SAT (mirror de
 * <c>IdentidadSandboxDtoValidator</c>). Solo aplica en modo sandbox.
 */
export const IdentidadSandboxSchema = z.object({
  rfc: z
    .string({ error: 'RFC obligatorio.' })
    .trim()
    .min(12, 'RFC debe tener 12 (moral) o 13 (física) caracteres.')
    .max(13, 'RFC debe tener 12 (moral) o 13 (física) caracteres.'),

  razonSocial: z
    .string({ error: 'Razón social obligatoria.' })
    .trim()
    .min(1, 'Razón social obligatoria (sin régimen societario).')
    .max(254, 'Máximo 254 caracteres.'),

  regimenFiscal: z
    .string({ error: 'Régimen fiscal obligatorio.' })
    .trim()
    .min(1, 'Régimen fiscal obligatorio.')
    .max(10),

  codigoPostal: z
    .string({ error: 'Código postal obligatorio.' })
    .trim()
    .regex(/^\d{5}$/, 'CP debe ser 5 dígitos.'),
});

export type IdentidadSandboxValues = z.infer<typeof IdentidadSandboxSchema>;

export const GuardarConfiguracionPacSchema = z.object({
  baseUrl: z
    .string({ error: 'BaseUrl obligatoria.' })
    .trim()
    .url('Debe ser una URL absoluta http(s).')
    .max(500, 'Máximo 500 caracteres.'),

  activo: z.boolean(),

  // Solo presentes cuando el toggle "identidades de prueba" está activo
  // (y por ende en modo sandbox); el componente los quita del form state
  // al desactivarlo.
  emisorSandbox: IdentidadSandboxSchema.optional(),
  receptorSandbox: IdentidadSandboxSchema.optional(),
});

export type GuardarConfiguracionPacValues = z.infer<
  typeof GuardarConfiguracionPacSchema
>;

/**
 * Schema del form de RFC receptor. Mirror del
 * <c>AgregarRfcReceptorValidator</c>: 12-13 chars, normaliza UPPER en
 * el backend.
 */
export const AgregarRfcReceptorSchema = z.object({
  rfc: z
    .string({ error: 'RFC obligatorio.' })
    .trim()
    .min(12, 'RFC debe tener 12 (moral) o 13 (física) caracteres.')
    .max(13, 'RFC debe tener 12 (moral) o 13 (física) caracteres.'),
});

export type AgregarRfcReceptorValues = z.infer<typeof AgregarRfcReceptorSchema>;

/**
 * Schema del form de subir FIEL. <c>cerFile</c> y <c>keyFile</c> son
 * archivos client-side; el componente los lee como base64 antes de
 * mandar al backend.
 */
export const SubirFielReceptorSchema = z.object({
  legalName: z
    .string({ error: 'Razón social obligatoria.' })
    .trim()
    .min(1, 'Razón social obligatoria.')
    .max(254, 'Máximo 254 caracteres.'),

  zipCode: z
    .string({ error: 'Código postal obligatorio.' })
    .trim()
    .regex(/^\d{5}$/, 'CP debe ser 5 dígitos.'),

  satTaxRegimeCode: z
    .string({ error: 'Régimen fiscal obligatorio.' })
    .trim()
    .min(1, 'Régimen fiscal obligatorio.')
    .max(10),

  email: z
    .string({ error: 'Email obligatorio.' })
    .trim()
    .email('Email inválido.')
    .max(254),

  password: z
    .string({ error: 'Password obligatoria.' })
    .min(1, 'Password obligatoria.')
    .max(500),
});

export type SubirFielReceptorValues = z.infer<typeof SubirFielReceptorSchema>;
