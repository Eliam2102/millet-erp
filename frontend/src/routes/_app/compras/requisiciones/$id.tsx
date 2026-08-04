import { createFileRoute, useParams } from '@tanstack/react-router';
import { RequisicionesLayout } from '@/features/compras/pages/RequisicionesLayout';
import { DetalleRequisicion } from '@/features/compras/pages/DetalleRequisicion';

/**
 * Ruta P3 — Detalle de requisición dentro del layout master-detail
 * (design/frontend-polish).
 *
 * <para>El layout <c>&lt;RequisicionesLayout/&gt;</c> renderiza la
 * lista compacta a la izquierda (con highlight del id activo) y el
 * <c>&lt;DetalleRequisicion/&gt;</c> en el panel derecho. En mobile
 * sub-md, la lista se oculta y el detalle ocupa toda la pantalla.</para>
 *
 * <para>Sin <c>validateSearch</c> propio: el detalle hereda el shape
 * del search del padre (bandeja). Para preservar los filtros de la
 * bandeja al cerrar, el detalle lee
 * <c>useSearch({ from: '/_app/compras/requisiciones/', strict: false })</c>.</para>
 */
export const Route = createFileRoute('/_app/compras/requisiciones/$id')({
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({ from: '/_app/compras/requisiciones/$id' });
  return (
    <RequisicionesLayout idActivo={id} detalle={<DetalleRequisicion />} />
  );
}
