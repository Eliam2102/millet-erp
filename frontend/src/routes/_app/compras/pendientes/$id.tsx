import { createFileRoute, useParams } from '@tanstack/react-router';
import { PendientesLayout } from '@/features/compras/pages/PendientesLayout';
import { DetalleRequisicion } from '@/features/compras/pages/DetalleRequisicion';
import {
  PendientesSearchSchema,
  type PendientesSearch,
} from '@/features/compras/lib/pendientes-search-schema';

/**
 * Ruta P2-detalle — Detalle de RQ dentro del layout master-detail
 * de pendientes (design/frontend-polish).
 *
 * <para>Análogo a <c>/compras/requisiciones/$id</c> pero la lista
 * compacta a la izquierda usa <c>usePendientesAutorizacion</c>. El
 * <c>&lt;DetalleRequisicion/&gt;</c> es el mismo componente — lee
 * <c>useParams</c> sin acoplarse a una ruta específica y el botón X
 * infiere a cuál bandeja volver leyendo el pathname.</para>
 */
export const Route = createFileRoute('/_app/compras/pendientes/$id')({
  component: DetalleRoute,
  validateSearch: (input: Record<string, unknown>): PendientesSearch =>
    PendientesSearchSchema.parse(input),
});

function DetalleRoute() {
  const { id } = useParams({ from: '/_app/compras/pendientes/$id' });
  return <PendientesLayout idActivo={id} detalle={<DetalleRequisicion />} />;
}
