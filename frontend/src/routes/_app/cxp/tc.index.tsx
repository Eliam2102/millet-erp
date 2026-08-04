import { createFileRoute, redirect } from '@tanstack/react-router';
import { TcLandingPage } from '@/features/cxp/pages/TcLandingPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Landing del sub-módulo TC (<c>/cxp/tc/</c>). Gate: TC.Leer.
 */
export const Route = createFileRoute('/_app/cxp/tc/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarTcLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: TcLandingPage,
});
