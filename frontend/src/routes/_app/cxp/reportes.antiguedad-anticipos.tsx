import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReporteAntiguedadAnticiposPage } from '@/features/cxp/pages/ReporteAntiguedadAnticiposPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute(
  '/_app/cxp/reportes/antiguedad-anticipos',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.CuentasPorPagarReportesAntiguedad)
    ) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: ReporteAntiguedadAnticiposPage,
});
