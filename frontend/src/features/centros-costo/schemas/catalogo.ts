import { z } from 'zod';

/**
 * Schemas zod de los modales del catálogo (CECO-FE-PR2). Longitudes
 * espejo del dominio backend: Dim1 clave ≤10; Dim2/Dim3 clave ≤20;
 * nombres ≤254; grupos ≤100. El padre NO está en el schema de edición
 * (inmutable por diseño — reubicar = baja + alta, 01-diseno §5).
 */

const claveDim1 = z
  .string()
  .trim()
  .min(1, 'La clave es requerida.')
  .max(10, 'Máximo 10 caracteres.');

const claveDim23 = z
  .string()
  .trim()
  .min(1, 'La clave es requerida.')
  .max(20, 'Máximo 20 caracteres.');

const nombreNivel = z
  .string()
  .trim()
  .min(1, 'El nombre es requerido.')
  .max(254, 'Máximo 254 caracteres.');

/**
 * GUID plano (formato 8-4-4-4-12) — NO <c>z.string().uuid()</c>: zod
 * valida version/variant RFC 4122 y los GUIDs CONGELADOS de la siembra
 * (<c>0000000c-…</c>, bloques estructurados) no las cumplen a propósito;
 * <c>.uuid()</c> rechazaría los grupos reales del catálogo. Hallazgo del
 * smoke de FE-PR2.
 */
const guid = (mensaje: string) =>
  z
    .string()
    .regex(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i,
      mensaje,
    );

export const Dim1Schema = z.object({
  clave: claveDim1,
  nombre: nombreNivel,
});
export type Dim1Values = z.infer<typeof Dim1Schema>;

export const Dim2Schema = z.object({
  clave: claveDim23,
  nombre: nombreNivel,
  grupoDim2Id: guid('Selecciona un grupo.'),
});
export type Dim2Values = z.infer<typeof Dim2Schema>;

export const Dim3Schema = z.object({
  clave: claveDim23,
  nombre: nombreNivel,
  grupoDim3Id: guid('Selecciona un grupo.'),
});
export type Dim3Values = z.infer<typeof Dim3Schema>;

export const GrupoDimSchema = z.object({
  nombre: z
    .string()
    .trim()
    .min(1, 'El nombre es requerido.')
    .max(100, 'Máximo 100 caracteres.'),
});
export type GrupoDimValues = z.infer<typeof GrupoDimSchema>;
