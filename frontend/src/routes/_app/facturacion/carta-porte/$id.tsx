import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { CartaPorteLayout } from '@/features/facturacion/pages/CartaPorteLayout';
import { DetalleCartaPorte } from '@/features/facturacion/pages/DetalleCartaPorte';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Detalle de una Carta Porte — <c>/facturacion/carta-porte/$id</c>.
 * FAC-UX-PR6: master-detail (patrón <c>compras/requisiciones/$id</c>).
 */
export const Route = createFileRoute('/_app/facturacion/carta-porte/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionCartaPorteLeer)) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({ from: '/_app/facturacion/carta-porte/$id' });
  return <CartaPorteLayout idActivo={id} detalle={<DetalleCartaPorte />} />;
}
