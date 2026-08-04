import { createFileRoute, redirect } from '@tanstack/react-router';
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
  component: RecepcionDetallePage,
});
