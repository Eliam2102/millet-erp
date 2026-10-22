import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { ConfiguracionLibroPage } from '@/features/activos-fijos/pages/ConfiguracionLibroPage';
export const Route = createFileRoute('/_app/activos-fijos/configuracion-libro')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(P.ActivosFijosClasesAdministrar))
      throw redirect({ to: '/' });
  },
  component: ConfiguracionLibroPage,
});
