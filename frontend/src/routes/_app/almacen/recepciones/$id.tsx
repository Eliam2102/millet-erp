import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { MasterDetailAlmacen, ListaRecepcionesCompacta } from '@/features/almacen/components/MasterDetailAlmacen';
import { RecepcionDetallePage } from '@/features/almacen/pages/RecepcionDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P2 — Detalle de recepción (doc 07 §FE-F2-PR1). Mismo gate
 * que la bandeja: <c>almacen.entradas.leer</c>. Sin permiso, redirige
 * a la landing.
 */
export const Route = createFileRoute('/_app/almacen/recepciones/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenEntradasLeer)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({ strict: false }) as { id: string };
  return (
    <MasterDetailAlmacen titulo="Recepciones" bandejaTo="/almacen/recepciones" lista={<ListaRecepcionesCompacta idActivo={id} />}>
      <RecepcionDetallePage />
    </MasterDetailAlmacen>
  );
}
