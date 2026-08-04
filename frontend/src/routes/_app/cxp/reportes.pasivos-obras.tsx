import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReportePasivosObrasPage } from '@/features/cxp/pages/ReportePasivosObrasPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/reportes/pasivos-obras')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarReportesCartera)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: ReportePasivosObrasPage,
});
