import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { CajasLayout } from '@/features/facturacion/pages/CajasLayout';
import { DetalleCaja } from '@/features/facturacion/pages/DetalleCaja';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Detalle de caja — <c>/facturacion/cajas/$id</c> (CAJAS-PR5).
 * Master-detail (patrón <c>facturas/$id</c>): lista compacta a la
 * izquierda + editor de alcances en el panel; mobile drill-down.
 */
export const Route = createFileRoute('/_app/facturacion/cajas/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionCajaAdministrar)) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({ from: '/_app/facturacion/cajas/$id' });
  return <CajasLayout idActivo={id} detalle={<DetalleCaja />} />;
}
