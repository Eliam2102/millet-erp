import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { ReppLayout } from '@/features/facturacion/pages/ReppLayout';
import { DetalleRepp } from '@/features/facturacion/pages/DetalleRepp';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Detalle de un complemento de pago — <c>/facturacion/repp/$id</c>.
 * FAC-UX-PR6: master-detail (patrón <c>compras/requisiciones/$id</c>).
 */
export const Route = createFileRoute('/_app/facturacion/repp/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const permitido = [
      PermisosCanonicos.FacturacionReppEmitir,
      PermisosCanonicos.FacturacionFacturasLeer,
    ].some((p) => permisos.includes(p));
    if (!permitido) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({ from: '/_app/facturacion/repp/$id' });
  return <ReppLayout idActivo={id} detalle={<DetalleRepp />} />;
}
