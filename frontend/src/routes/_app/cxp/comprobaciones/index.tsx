import { createFileRoute, redirect } from '@tanstack/react-router';
import { ComprobacionesPage } from '@/features/cxp/pages/ComprobacionesPage';
import {
  ComprobacionesSearchSchema,
  type ComprobacionesSearch,
} from '@/features/cxp/lib/comprobaciones-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/comprobaciones/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.CuentasPorPagarComprobacionesLeer)
    ) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: ComprobacionesPage,
  validateSearch: (input: Record<string, unknown>): ComprobacionesSearch =>
    ComprobacionesSearchSchema.parse(input),
});
