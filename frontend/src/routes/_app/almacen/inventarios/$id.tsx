import { createFileRoute, redirect, useMatchRoute, useParams } from '@tanstack/react-router';
import { MasterDetailAlmacen, ListaConteosCompacta } from '@/features/almacen/components/MasterDetailAlmacen';
import { InventarioDetallePage } from '@/features/almacen/pages/InventarioDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P8 — Detalle de conteo (doc 07 §FE-F5-PR1).
 * Mismo gate que la bandeja: <c>almacen.inventarios.leer</c>.
 */
export const Route = createFileRoute('/_app/almacen/inventarios/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenInventariosLeer)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({ strict: false }) as { id: string };
  const matchRoute = useMatchRoute();
  // Captura (sin sesgo) y aprobación son pantallas de ancho completo: sin lista lateral.
  if (matchRoute({ to: '/almacen/inventarios/$id/captura' }) || matchRoute({ to: '/almacen/inventarios/$id/aprobacion' }))
    return <InventarioDetallePage />;
  return (
    <MasterDetailAlmacen titulo="Conteos" bandejaTo="/almacen/inventarios" lista={<ListaConteosCompacta idActivo={id} />}>
      <InventarioDetallePage />
    </MasterDetailAlmacen>
  );
}
