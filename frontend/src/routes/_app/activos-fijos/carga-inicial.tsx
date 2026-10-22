import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { CargaInicialPage } from '@/features/activos-fijos/pages/CargaInicialPage';
export const Route = createFileRoute('/_app/activos-fijos/carga-inicial')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(P.ActivosFijosCargaEjecutar))
      throw redirect({ to: '/' });
  },
  component: CargaInicialPage,
});
