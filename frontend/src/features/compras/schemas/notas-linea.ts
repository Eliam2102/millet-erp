import { z } from 'zod';

/**
 * Schema Zod del PATCH de notas de línea
 * (<c>PATCH /requisiciones/{id}/lineas/{lineaId}/notas</c>). Doc 05
 * §11.2.
 *
 * <para>Solo el campo <c>notas</c> en el body. <c>null</c> = limpiar
 * (en cualquier estado no-terminal). Tope 500 caracteres (mirror del
 * CHECK de BD).</para>
 */
export const ActualizarNotasLineaSchema = z.object({
  notas: z.string().max(500, 'Máximo 500 caracteres').nullable(),
});

export type ActualizarNotasLineaValues = z.infer<
  typeof ActualizarNotasLineaSchema
>;
