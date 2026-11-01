import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReglasSeriePage } from '@/features/cxc/pages/ReglasSeriePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Reglas de liberación por serie — <c>/cxc/reglas-serie</c> (C7.2).
 * Solo con <c>reglas-serie.gestionar</c>; el backend gatea igual.
 */
export const Route = createFileRoute('/_app/cxc/reglas-serie/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorCobrarReglasSerieGestionar)) {
      throw redirect({ to: '/cxc' });
    }
  },
  component: ReglasSeriePage,
});
