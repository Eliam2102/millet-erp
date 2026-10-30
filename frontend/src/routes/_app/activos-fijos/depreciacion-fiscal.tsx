import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { DepreciacionFiscalPage } from '@/features/activos-fijos/pages/DepreciacionFiscalPage';
export const Route = createFileRoute('/_app/activos-fijos/depreciacion-fiscal')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(P.ActivosFijosActivosVer))
      throw redirect({ to: '/' });
  },
  component: DepreciacionFiscalPage,
});
