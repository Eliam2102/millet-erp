import { createFileRoute, redirect } from '@tanstack/react-router';
import { ConciliacionPolizasPage } from '@/features/cxc/pages/ConciliacionPolizasPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Conciliación de cobros contra pólizas — <c>/cxc/conciliacion-polizas</c> (CXC-06, 86e3qddef). Gateada por
 * <c>cartera.leer</c>; el backend pide además leer todas las sucursales (reporte corporativo, ADR-0051).
 */
export const Route = createFileRoute('/_app/cxc/conciliacion-polizas/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorCobrarCarteraLeer)) {
      throw redirect({ to: '/cxc' });
    }
  },
  component: ConciliacionPolizasPage,
});
