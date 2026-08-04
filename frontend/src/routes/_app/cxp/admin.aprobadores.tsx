import { createFileRoute, redirect } from '@tanstack/react-router';
import { AprobadoresLimitesPage } from '@/features/cxp/pages/AprobadoresLimitesPage';
import {
  AprobadoresLimitesSearchSchema,
  type AprobadoresLimitesSearch,
} from '@/features/cxp/lib/admin-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/admin/aprobadores')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(
        PermisosCanonicos.CuentasPorPagarCatalogosAprobadoresAdministrar,
      )
    ) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: AprobadoresLimitesPage,
  validateSearch: (input: Record<string, unknown>): AprobadoresLimitesSearch =>
    AprobadoresLimitesSearchSchema.parse(input),
});
