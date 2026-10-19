import { createFileRoute, redirect } from '@tanstack/react-router';
import { MaterialEnRevisionPage } from '@/features/almacen/pages/MaterialEnRevisionPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta ALM-04 — Material en revisión (bandeja de recepciones con daño).
 * Gate: <c>almacen.entradas.leer</c>.
 */
export const Route = createFileRoute('/_app/almacen/revision')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenEntradasLeer)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: MaterialEnRevisionPage,
});
