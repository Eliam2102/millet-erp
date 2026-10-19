import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { OtrBandejaPage } from '@/features/comercio-exterior/pages/OtrBandejaPage';
export const Route = createFileRoute('/_app/comercio-exterior/otr')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(P.ComercioExteriorExpedientesLeer))
      throw redirect({ to: '/' });
  },
  component: OtrBandejaPage,
});
