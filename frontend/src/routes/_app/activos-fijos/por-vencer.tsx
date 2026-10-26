import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';

import { PorVencerPage } from '@/features/activos-fijos/pages/PorVencerPage';
export const Route = createFileRoute('/_app/activos-fijos/por-vencer')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(P.ActivosFijosActivosVer))
      throw redirect({ to: '/' });
  },

  component: PorVencerPage,
});
