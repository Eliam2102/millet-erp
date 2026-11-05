import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { AutorizacionesPage } from '@/features/activos-fijos/pages/AutorizacionesPage';
export const Route = createFileRoute('/_app/activos-fijos/autorizaciones')({
  beforeLoad: () => {
    if (
      ![
        P.ActivosFijosBajasAutorizar,
        P.ActivosFijosReversasAutorizar,
        P.ActivosFijosAjustesAutorizar,
      ].some((p) => useAuthStore.getState().permisos.includes(p))
    )
      throw redirect({ to: '/' });
  },
  component: AutorizacionesPage,
});
