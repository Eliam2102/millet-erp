import { z } from 'zod';
import { EstadoRequisicion } from '@/features/compras/api/types';

/**
 * Schema Zod de los <c>search params</c> de la bandeja P1
 * (<c>routes/_app/compras/requisiciones/index.tsx</c>). TanStack
 * Router los valida al montar; valores inválidos en la URL se filtran
 * silenciosamente al default.
 *
 * <para>Doc 05 §13.9 — los filtros se preservan al volver del detalle
 * vía <c>useSearch</c>. Persistencia entre sesiones queda en URL
 * solo (no <c>localStorage</c>).</para>
 *
 * <para>El estado se valida contra los valores numéricos del enum
 * <see cref="EstadoRequisicion"/>; los demás filtros son strings/UUIDs
 * sin validación estricta de UUID (el backend rechaza si no calza,
 * UI degrade elegantemente).</para>
 */
export const BandejaSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoRequisicion.Borrador),
      z.literal(EstadoRequisicion.EnAutorizacion),
      z.literal(EstadoRequisicion.Autorizada),
      z.literal(EstadoRequisicion.EnSurtido),
      z.literal(EstadoRequisicion.Cerrada),
      z.literal(EstadoRequisicion.Cancelada),
      z.literal(EstadoRequisicion.Rechazada),
      z.literal(EstadoRequisicion.Eliminada),
      // ADR-0043: terminales de cierre manual. Válidos en la URL (el
      // schema espeja el enum completo), pero todavía NO se ofrecen en el
      // dropdown de FiltrosBandeja — eso llega en el PR #2, cuando el cierre
      // manual los hace alcanzables. En el PR #1 ninguna RQ está en ellos.
      z.literal(EstadoRequisicion.CerradaSinSurtir),
      z.literal(EstadoRequisicion.CerradaSurtidaParcial),
    ])
    .optional(),
  departamentoId: z.string().min(1).optional(),
  requisitanteId: z.string().min(1).optional(),
  /** Búsqueda client-side por folio dentro de la página actual. */
  q: z.string().min(1).optional(),
  /** Offset-based paging (doc 05 §7). */
  offset: z.coerce.number().int().min(0).optional().default(0),
  limit: z.coerce.number().int().min(1).max(200).optional().default(50),
});

export type BandejaSearch = z.infer<typeof BandejaSearchSchema>;

/**
 * Default puro (todos los filtros vacíos, paginación al inicio). Útil
 * para tests y para "limpiar todos los filtros" en la UI.
 */
export const DEFAULT_BANDEJA_SEARCH: BandejaSearch = {
  offset: 0,
  limit: 50,
};
