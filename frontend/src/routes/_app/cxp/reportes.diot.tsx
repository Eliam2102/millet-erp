import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReporteDiotPage } from '@/features/cxp/pages/ReporteDiotPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/reportes/diot')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarReportesDiot)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: ReporteDiotPage,
});
