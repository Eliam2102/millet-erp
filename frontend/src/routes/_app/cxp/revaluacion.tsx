import { createFileRoute, redirect } from '@tanstack/react-router';
import { RevaluacionCierrePage } from '@/features/cxp/pages/RevaluacionCierrePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/revaluacion')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarRevaluacionConsultar)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: RevaluacionCierrePage,
});
