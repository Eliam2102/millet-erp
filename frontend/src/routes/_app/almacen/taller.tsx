import { createFileRoute, redirect } from '@tanstack/react-router';
import { TallerCierrePage } from '@/features/almacen/pages/TallerCierrePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta PT5.10 — Taller de sucursal (cierre de día y factor de optimización).
 * Gate: <c>almacen.salidas.leer-todas</c>, como la bandeja de salidas.
 */
export const Route = createFileRoute('/_app/almacen/taller')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenSalidasLeerTodas)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: TallerCierrePage,
});
