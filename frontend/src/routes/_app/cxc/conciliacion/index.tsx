import { createFileRoute, redirect } from '@tanstack/react-router';
import { ConciliacionCarteraPage } from '@/features/cxc/pages/ConciliacionCarteraPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Conciliación de cartera — <c>/cxc/conciliacion</c> (C7.7).
 * Consultar pide <c>cartera.leer</c>; conciliar a demanda se gatea en la página.
 */
export const Route = createFileRoute('/_app/cxc/conciliacion/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorCobrarCarteraLeer)) {
      throw redirect({ to: '/cxc' });
    }
  },
  component: ConciliacionCarteraPage,
});
