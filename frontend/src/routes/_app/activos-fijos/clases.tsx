import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';

import { ClasesPage } from '@/features/activos-fijos/pages/ClasesPage';
export const Route = createFileRoute('/_app/activos-fijos/clases')({
  beforeLoad: () => {
    if (
      ![P.ActivosFijosClasesAdministrar, P.ActivosFijosClasesAutorizar].some((p) =>
        useAuthStore.getState().permisos.includes(p),
      )
    )
      throw redirect({ to: '/' });
  },

  component: ClasesPage,
});
