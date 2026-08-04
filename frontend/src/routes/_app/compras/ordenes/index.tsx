import { createFileRoute } from '@tanstack/react-router';
import { BandejaOrdenesCompra } from '@/features/compras/ordenes/pages/BandejaOrdenesCompra';
import {
  BandejaOcSearchSchema,
  type BandejaOcSearch,
} from '@/features/compras/ordenes/lib/bandeja-oc-search-schema';

/**
 * Ruta P1 — Bandeja general de OCs (doc 05 §10.1).
 *
 * <para>UF1-PR3 reemplaza el master-detail-con-placeholder de UF1-PR2
 * por la vista tabular full-page (<c>&lt;BandejaOrdenesCompra/&gt;</c>),
 * alineando OC con el patrón cross-módulo de RQ
 * (frontend/docs/patrones-compras.md §6.6): tabla escaneable con sort
 * + paginación; click en "Ver" navega a <c>/compras/ordenes/$id</c>
 * que monta el master-detail
 * (<c>OrdenesCompraLayout + DetalleOrdenCompra</c>) preservando los
 * filtros via <c>state.bandejaOcSearch</c>.</para>
 */
export const Route = createFileRoute('/_app/compras/ordenes/')({
  component: BandejaOrdenesCompra,
  validateSearch: (input: Record<string, unknown>): BandejaOcSearch =>
    BandejaOcSearchSchema.parse(input),
});
