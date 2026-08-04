import { createFileRoute, redirect } from '@tanstack/react-router';
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
  component: InventarioDetallePage,
});
