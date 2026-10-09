import { createFileRoute, redirect } from '@tanstack/react-router';
import { RetencionesPage } from '@/features/cxp/pages/RetencionesPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/admin/retenciones')({
  beforeLoad: () => {
    if (
      !useAuthStore.getState().permisos.includes(PermisosCanonicos.CuentasPorPagarRetencionesLeer)
    )
      throw redirect({ to: '/cxp' });
  },
  component: RetencionesPage,
});
