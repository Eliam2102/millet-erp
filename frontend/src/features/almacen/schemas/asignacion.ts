import { z } from 'zod';

/**
 * Schema Zod de "Asignar artículo a una ubicación" (código = asignación N4,
 * ADR-0047 PR C). Solo la llave: artículo + ubicación (tras PR C ya no hay
 * min/máx/reorden en N4). La existencia del artículo/ubicación y la unicidad las
 * valida el backend.
 */

/**
 * Regex laxo de UUID (8-4-4-4-12 hex) — los seeds del backend usan GUIDs
 * deterministas que no califican como v1-v8 estricto. Mismo patrón que
 * <c>almacen/schemas/reorden.ts</c>.
 */
const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const AsignarSchema = z.object({
  articuloId: z.string().regex(UUID_SHAPE_RE, 'Artículo requerido'),
  ubicacionId: z.string().regex(UUID_SHAPE_RE, 'Ubicación requerida'),
});

export type AsignarValues = z.infer<typeof AsignarSchema>;
