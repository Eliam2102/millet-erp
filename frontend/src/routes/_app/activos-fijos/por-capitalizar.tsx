import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
export const Route = createFileRoute('/_app/activos-fijos/por-capitalizar')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(P.ActivosFijosCapitalizar))
      throw redirect({ to: '/' });
    throw redirect({ to: '/activos-fijos/activos', search: { estado: 0 } });
  },
});
