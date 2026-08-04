import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReporteCarteraPage } from '@/features/cxp/pages/ReporteCarteraPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/reportes/cartera')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarReportesCartera)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: ReporteCarteraPage,
});
