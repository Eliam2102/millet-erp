import { createFileRoute, redirect } from '@tanstack/react-router';
import { PoliticasViaticosPage } from '@/features/cxp/pages/PoliticasViaticosPage';
import {
  PoliticasViaticosSearchSchema,
  type PoliticasViaticosSearch,
} from '@/features/cxp/lib/admin-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/admin/politicas-viaticos')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(
        PermisosCanonicos.CuentasPorPagarCatalogosPoliticasAdministrar,
      )
    ) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: PoliticasViaticosPage,
  validateSearch: (input: Record<string, unknown>): PoliticasViaticosSearch =>
    PoliticasViaticosSearchSchema.parse(input),
});
