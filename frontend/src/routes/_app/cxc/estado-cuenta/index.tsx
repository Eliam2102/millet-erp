import { createFileRoute, redirect } from '@tanstack/react-router';
import { EstadoCuentaPage } from '@/features/cxc/pages/EstadoCuentaPage';
import {
  EstadoCuentaSearchSchema,
  type EstadoCuentaSearch,
} from '@/features/cxc/lib/cartera-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Estado de cuenta por cliente — <c>/cxc/estado-cuenta</c> (CXC-FE-PR5,
 * ADR-0036). Gate espejo del backend (<c>cartera.leer</c>).
 */
export const Route = createFileRoute('/_app/cxc/estado-cuenta/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorCobrarCarteraLeer)) {
      throw redirect({ to: '/cxc' });
    }
  },
  component: EstadoCuentaPage,
  validateSearch: (input: Record<string, unknown>): EstadoCuentaSearch =>
    EstadoCuentaSearchSchema.parse(input),
});
