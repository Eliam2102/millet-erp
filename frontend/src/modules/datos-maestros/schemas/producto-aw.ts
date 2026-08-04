import { z } from 'zod';

/**
 * Schemas Zod del recurso Productos A+W (ADR-0048). Mirror de los
 * validators FluentValidation backend (<c>CrearProductoAwValidator</c>,
 * <c>ActualizarProductoAwValidator</c>).
 *
 * <para>Las claves SAT son <b>opcionales</b> — su ausencia NO bloquea
 * el guardado, bloquea el timbrado (bandeja "Fiscales incompletos").
 * Si el campo viene, debe ser válido.</para>
 */

const CLAVE_PROD_SERV_RE = /^\d{8}$/;
const CLAVE_UNIDAD_RE = /^[A-Z0-9]{1,5}$/;

const claveProdServOpcional = z
  .string()
  .trim()
  .regex(CLAVE_PROD_SERV_RE, 'Clave SAT c_ClaveProdServ de 8 dígitos')
  .nullable()
  .or(z.literal('').transform(() => null));

const claveUnidadOpcional = z
  .string()
  .trim()
  .toUpperCase()
  .regex(CLAVE_UNIDAD_RE, 'Clave SAT c_ClaveUnidad (ej. H87, M2)')
  .nullable()
  .or(z.literal('').transform(() => null));

// c_ObjetoImp vigente llega a 08 (backend valida ^0[1-8]$ desde
// FAC-DET-PR1); el catálogo se resuelve en vivo vía <ClaveSatSelector>.
const objetoImpOpcional = z
  .string()
  .regex(/^0[1-8]$/, 'Clave SAT c_ObjetoImp (01–08)')
  .nullable();

const tasaOpcional = z
  .number()
  .min(0, 'Mínimo 0')
  .max(1, 'Máximo 1 (fracción, ej. 0.16)')
  .nullable();

// Datos de aduana (CCE): fracción 8-10 dígitos, unidad aduanera c_UnidadAduana
// (≤3), peso ≥ 0. Todos opcionales — su ausencia no bloquea el guardado.
const fraccionAduanaOpcional = z
  .string()
  .trim()
  .regex(/^\d{8,10}$/, 'Fracción arancelaria de 8 a 10 dígitos')
  .nullable()
  .or(z.literal('').transform(() => null));

const unidadAduanaOpcional = z
  .string()
  .trim()
  .min(1)
  .max(3, 'Máximo 3 caracteres (c_UnidadAduana)')
  .nullable()
  .or(z.literal('').transform(() => null));

const pesoOpcional = z.number().min(0, 'Mínimo 0').nullable();

export const CrearProductoAwSchema = z.object({
  referenciaExterna: z
    .string()
    .trim()
    .min(1, 'Referencia requerida')
    .max(50, 'Máximo 50 caracteres'),
  descripcion: z
    .string()
    .trim()
    .min(1, 'Descripción requerida')
    .max(254, 'Máximo 254 caracteres'),
  unidadMedida: z
    .string()
    .trim()
    .min(1, 'Unidad de medida requerida')
    .max(20, 'Máximo 20 caracteres'),
  unidadMedidaId: z.string().nullable(),
  categoriaId: z.string().nullable(),
  claveProdServSat: claveProdServOpcional,
  claveUnidadSat: claveUnidadOpcional,
});

export type CrearProductoAwValues = z.infer<typeof CrearProductoAwSchema>;

/**
 * Schema del PATCH /datos-maestros/productos-aw/{id}. Inmutables:
 * referenciaExterna (correlación A+W) y origen. Las claves SAT y el
 * objeto de impuesto NO tienen flag <c>limpiarX</c> en el backend
 * (vacío = no tocar); las tasas sí (vacío = limpiar).
 */
export const ActualizarProductoAwSchema = z.object({
  descripcion: z
    .string()
    .trim()
    .min(1, 'Descripción requerida')
    .max(254, 'Máximo 254 caracteres'),
  unidadMedidaId: z.string().nullable(),
  categoriaId: z.string().nullable(),
  claveProdServSat: claveProdServOpcional,
  claveUnidadSat: claveUnidadOpcional,
  objetoImp: objetoImpOpcional,
  tasaIvaTraslado: tasaOpcional,
  tasaRetencionIva: tasaOpcional,
  tasaRetencionIsr: tasaOpcional,
  fraccionArancelaria: fraccionAduanaOpcional,
  unidadAduana: unidadAduanaOpcional,
  pesoUnitarioKg: pesoOpcional,
});

export type ActualizarProductoAwValues = z.infer<
  typeof ActualizarProductoAwSchema
>;
