import { z } from 'zod';

/**
 * Search params de la consulta jerárquica (PR6). Dos modos sobre el mismo
 * árbol: `articulo` (elige artículo → su distribución por niveles) y
 * `ubicacion` (browse desde la raíz; almacén/sub-almacén son atajos de
 * salto). Default: modo ubicación desde la raíz.
 */
export const SaldosJerarquiaSearchSchema = z.object({
  modo: z.enum(['articulo', 'ubicacion']).optional(),
  articuloId: z.string().min(1).optional(),
  almacenId: z.string().min(1).optional(),
  subAlmacenId: z.string().min(1).optional(),
  incluirVacios: z.boolean().optional(),
});

export type SaldosJerarquiaSearch = z.infer<typeof SaldosJerarquiaSearchSchema>;

export const DEFAULT_SALDOS_JERARQUIA_SEARCH: SaldosJerarquiaSearch = {};
