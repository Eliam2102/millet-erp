import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { PedidosLayout } from '@/features/facturacion/pages/PedidosLayout';
import { DetallePedido } from '@/features/facturacion/pages/DetallePedido';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Detalle de un pedido facturable — <c>/facturacion/pedidos/$id</c>.
 * FAC-UX-PR5: master-detail (patrón <c>compras/requisiciones/$id</c>):
 * lista compacta a la izquierda con highlight del activo +
 * <c>&lt;DetallePedido/&gt;</c> en el panel; mobile drill-down. Hereda
 * el search-schema del index para preservar filtros.
 */
export const Route = createFileRoute('/_app/facturacion/pedidos/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const permitido = [
      PermisosCanonicos.FacturacionFacturasEmitir,
      PermisosCanonicos.FacturacionPedidosCapturar,
      PermisosCanonicos.FacturacionFacturasLeer,
    ].some((p) => permisos.includes(p));
    if (!permitido) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({ from: '/_app/facturacion/pedidos/$id' });
  return <PedidosLayout idActivo={id} detalle={<DetallePedido />} />;
}
