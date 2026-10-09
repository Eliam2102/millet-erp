import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReporteAuxiliarPage } from '@/features/cxp/pages/ReporteAuxiliarPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/reportes/auxiliar')({
  beforeLoad: () => {
    if (
      !useAuthStore.getState().permisos.includes(PermisosCanonicos.CuentasPorPagarReportesCartera)
    )
      throw redirect({ to: '/cxp' });
  },
  component: ReporteAuxiliarPage,
});
