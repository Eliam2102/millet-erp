import { createFileRoute } from '@tanstack/react-router';
import { BandejaRequisiciones } from '@/features/compras/pages/BandejaRequisiciones';
import {
  BandejaSearchSchema,
  type BandejaSearch,
} from '@/features/compras/lib/bandeja-search-schema';

/**
 * Ruta P1 — Bandeja general de requisiciones (doc 05 §5).
 *
 * <para>Renderiza la bandeja tabular completa (folio, fecha,
 * requisitante, depto, estado, ver). El botón "Ver" de cada fila
 * navega a <c>/compras/requisiciones/$id</c>, que activa el layout
 * master-detail (lista compacta a la izquierda + detalle a la
 * derecha) sin perder los filtros — la bandeja pasa
 * <c>state.bandejaSearch</c> al <c>&lt;Link/&gt;</c>.</para>
 */
export const Route = createFileRoute('/_app/compras/requisiciones/')({
  component: BandejaRequisiciones,
  validateSearch: (input: Record<string, unknown>): BandejaSearch =>
    BandejaSearchSchema.parse(input),
});
