import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { FacturasAnticipoLayout } from '@/features/facturacion/pages/FacturasAnticipoLayout';
import { DetalleFacturaAnticipo } from '@/features/facturacion/pages/DetalleFacturaAnticipo';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Detalle de la factura de anticipo —
 * <c>/facturacion/anticipos/facturas/$id</c> (ANT-PR2, doc 13 §6). Patrón
 * master-detail P3 (molde <c>facturas/$id</c>): lista compacta a la
 * izquierda + <c>&lt;DetalleFacturaAnticipo/&gt;</c> en el panel.
 */
export const Route = createFileRoute('/_app/facturacion/anticipos/facturas/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionAnticiposLeer)) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({ from: '/_app/facturacion/anticipos/facturas/$id' });
  return <FacturasAnticipoLayout idActivo={id} detalle={<DetalleFacturaAnticipo />} />;
}
