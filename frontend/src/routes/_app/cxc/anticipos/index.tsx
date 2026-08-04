import { createFileRoute, redirect } from '@tanstack/react-router';
import { AnticiposCxcPage } from '@/features/cxc/pages/AnticiposCxcPage';
import {
  AnticiposCxcSearchSchema,
  type AnticiposCxcSearch,
} from '@/features/cxc/lib/cartera-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Saldos de anticipo por cliente — <c>/cxc/anticipos</c> (CXC-FE-PR5,
 * read port de Facturación). Gate espejo del backend
 * (<c>cartera.leer</c>).
 */
export const Route = createFileRoute('/_app/cxc/anticipos/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorCobrarCarteraLeer)) {
      throw redirect({ to: '/cxc' });
    }
  },
  component: AnticiposCxcPage,
  validateSearch: (input: Record<string, unknown>): AnticiposCxcSearch =>
    AnticiposCxcSearchSchema.parse(input),
});
