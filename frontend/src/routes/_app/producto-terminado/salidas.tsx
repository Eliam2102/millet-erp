import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { SalidasPendientesPage } from '@/features/producto-terminado/pages/SalidasPendientesPage';

export const Route = createFileRoute('/_app/producto-terminado/salidas')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(P.ProductoTerminadoPiezasVer))
      throw redirect({ to: '/' });
  },
  component: SalidasPendientesPage,
});
