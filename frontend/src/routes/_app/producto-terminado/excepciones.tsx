import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { ExcepcionesPage } from '@/features/producto-terminado/pages/ExcepcionesPage';

export const Route = createFileRoute('/_app/producto-terminado/excepciones')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(P.ProductoTerminadoExcepcionesGestionar))
      throw redirect({ to: '/' });
  },
  component: ExcepcionesPage,
});
