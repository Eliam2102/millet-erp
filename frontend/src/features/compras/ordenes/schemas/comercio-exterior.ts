import { z } from 'zod';

/**
 * Schemas Zod de comercio exterior de la OC (mirror de los contratos
 * PATCH <c>/numero-pedimento</c> y PATCH <c>/lineas/{lineaId}</c>).
 *
 * <para>// PLATFORM-TODO(&lt;F1-COM-06&gt;): supuesto Vilo, pendiente lista
 * oficial de Millet. Longitudes máximas de BL y agente aduanal son
 * supuestos del frontend; el backend valida la regla de negocio.</para>
 */

/** Fracción arancelaria: exactamente 8 o 10 dígitos. */
export const FRACCION_ARANCELARIA_REGEX = /^\d{8}(\d{2})?$/;

/** Fecha <c>yyyy-MM-dd</c> (formato de <c>&lt;input type="date"&gt;</c>). */
export const FECHA_ETA_REGEX = /^\d{4}-\d{2}-\d{2}$/;

/**
 * Fracción arancelaria opcional. Vacía = sin capturar; si trae valor,
 * debe tener 8 o 10 dígitos (sin letras ni separadores).
 */
export const FraccionArancelariaSchema = z
  .string()
  .trim()
  .refine(
    (v) => v === '' || FRACCION_ARANCELARIA_REGEX.test(v),
    'La fracción arancelaria debe tener 8 o 10 dígitos.',
  );

/** Fecha estimada de arribo opcional (vacía o <c>yyyy-MM-dd</c>). */
export const FechaEstimadaArriboSchema = z
  .string()
  .refine(
    (v) => v === '' || FECHA_ETA_REGEX.test(v),
    'Usa la fecha en formato AAAA-MM-DD.',
  );

/**
 * Valores del control de pedimento (<c>PATCH /numero-pedimento</c>). El
 * backend REEMPLAZA los cuatro campos: el form debe enviar siempre los
 * cuatro (vacío = null = limpiar).
 */
export const NumeroPedimentoSchema = z.object({
  numeroPedimento: z
    .string()
    .trim()
    .max(40, 'Máximo 40 caracteres.'),
  numeroBl: z
    .string()
    .trim()
    .max(40, 'Máximo 40 caracteres.'),
  fechaEstimadaArribo: FechaEstimadaArriboSchema,
  agenteAduanalNaviera: z
    .string()
    .trim()
    .max(120, 'Máximo 120 caracteres.'),
});

export type NumeroPedimentoValues = z.infer<typeof NumeroPedimentoSchema>;
