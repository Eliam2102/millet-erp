import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { TraspasosPage } from '@/features/producto-terminado/pages/TraspasosPage';

export const Route = createFileRoute('/_app/producto-terminado/traspasos')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(P.ProductoTerminadoPiezasVer))
      throw redirect({ to: '/' });
  },
  component: TraspasosPage,
});
