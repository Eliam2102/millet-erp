import { createFileRoute, redirect } from '@tanstack/react-router';
import { CarteraPage } from '@/features/cxc/pages/CarteraPage';
import {
  CarteraSearchSchema,
  type CarteraSearch,
} from '@/features/cxc/lib/cartera-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Antigüedad de saldos — <c>/cxc/cartera</c> (CXC-FE-PR5, ADR-0036).
 * Gate espejo del backend (<c>cartera.leer</c>).
 */
export const Route = createFileRoute('/_app/cxc/cartera/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorCobrarCarteraLeer)) {
      throw redirect({ to: '/cxc' });
    }
  },
  component: CarteraPage,
  validateSearch: (input: Record<string, unknown>): CarteraSearch =>
    CarteraSearchSchema.parse(input),
});
