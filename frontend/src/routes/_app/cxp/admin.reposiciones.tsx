import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReposicionesCajaPage } from '@/features/cxp/pages/ReposicionesCajaPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/admin/reposiciones')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarReposicionesLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: ReposicionesCajaPage,
});
