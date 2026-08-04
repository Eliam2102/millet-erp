import { createFileRoute, useParams } from '@tanstack/react-router';
import { OrdenesCompraLayout } from '@/features/compras/ordenes/pages/OrdenesCompraLayout';
import { DetalleOrdenCompra } from '@/features/compras/ordenes/pages/DetalleOrdenCompra';

/**
 * Ruta P3 — Detalle de OC dentro del layout master-detail
 * (frontend/docs/patrones-compras.md §6.1).
 *
 * <para>El layout <c>&lt;OrdenesCompraLayout/&gt;</c> renderiza la
 * lista compacta a la izquierda (con highlight del id activo) y
 * <c>&lt;DetalleOrdenCompra/&gt;</c> en el panel derecho. En mobile
 * sub-md, la lista se oculta y el detalle ocupa toda la pantalla.</para>
 *
 * <para>Sin <c>validateSearch</c> propio: el detalle hereda el shape
 * del search del padre (bandeja). Para preservar los filtros de la
 * bandeja al cerrar, el detalle lee
 * <c>useLocation().state.bandejaOcSearch</c> que la lista compacta
 * pasa al <c>&lt;Link/&gt;</c>.</para>
 */
export const Route = createFileRoute('/_app/compras/ordenes/$id')({
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({ from: '/_app/compras/ordenes/$id' });
  return (
    <OrdenesCompraLayout idActivo={id} detalle={<DetalleOrdenCompra />} />
  );
}
