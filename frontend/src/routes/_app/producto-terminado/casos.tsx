import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { CasosDanoPage } from '@/features/producto-terminado/pages/CasosDanoPage';

export const Route = createFileRoute('/_app/producto-terminado/casos')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(P.ProductoTerminadoPiezasVer))
      throw redirect({ to: '/' });
  },
  component: CasosDanoPage,
});
