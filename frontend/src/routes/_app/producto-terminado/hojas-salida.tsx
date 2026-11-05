import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { HojasSalidaPage } from '@/features/producto-terminado/pages/HojasSalidaPage';

export const Route = createFileRoute('/_app/producto-terminado/hojas-salida')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(P.ProductoTerminadoPiezasVer))
      throw redirect({ to: '/' });
  },
  component: HojasSalidaPage,
});
