import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';

import { ActivoDetallePage } from '@/features/activos-fijos/pages/ActivoDetallePage';
export const Route = createFileRoute('/_app/activos-fijos/activos/$id')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(P.ActivosFijosActivosVer))
      throw redirect({ to: '/' });
  },

  component: ActivoDetallePage,
});
