import { z } from 'zod';

/**
 * Schema Zod del body POST <c>/{id}/duplicar</c> (UF5-PR2, F6-PR2).
 *
 * <para>El comprador puede tomar valores default (sucursal/año/fecha
 * de la OC origen) o sobrescribir si necesita duplicar a otra sucursal
 * o cambiar de año fiscal.</para>
 */
export const DuplicarOcSchema = z.object({
  /** Código (clave) de la sucursal — el folio se forma con clave+año+sec. */
  sucursalCodigo: z
    .string()
    .min(2, 'El código de sucursal es obligatorio.')
    .max(20),
  folioAnio: z.number().int().min(2020).max(2099),
  /** Fecha de calendario YYYY-MM-DD (DateOnly, ADR-0040). */
  fechaDocumento: z
    .string()
    .regex(/^\d{4}-\d{2}-\d{2}$/, 'Fecha requerida (YYYY-MM-DD).'),
});

export type DuplicarOcValues = z.infer<typeof DuplicarOcSchema>;
