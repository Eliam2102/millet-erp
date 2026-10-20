import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';

import { ClasesPage } from '@/features/activos-fijos/pages/ClasesPage';
export const Route = createFileRoute('/_app/activos-fijos/clases')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(P.ActivosFijosClasesAdministrar))
      throw redirect({ to: '/' });
  },

  component: ClasesPage,
});
