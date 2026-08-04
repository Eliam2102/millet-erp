import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReporteMovimientosTcPendientesPage } from '@/features/cxp/pages/ReporteMovimientosTcPendientesPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/reportes/tc-pendientes')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarReportesTc)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: ReporteMovimientosTcPendientesPage,
});
