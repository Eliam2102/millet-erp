import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { FacturasLayout } from '@/features/facturacion/pages/FacturasLayout';
import { DetalleFactura } from '@/features/facturacion/pages/DetalleFactura';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Detalle del comprobante — <c>/facturacion/facturas/$id</c>.
 * FAC-UX-PR5: master-detail (patrón <c>compras/requisiciones/$id</c>):
 * lista compacta a la izquierda + <c>&lt;DetalleFactura/&gt;</c> en el
 * panel; mobile drill-down. Hereda el search-schema del index.
 */
export const Route = createFileRoute('/_app/facturacion/facturas/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionFacturasLeer)) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({ from: '/_app/facturacion/facturas/$id' });
  return <FacturasLayout idActivo={id} detalle={<DetalleFactura />} />;
}
