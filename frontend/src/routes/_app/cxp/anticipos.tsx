import { createFileRoute, redirect } from '@tanstack/react-router';
import { AnticiposPage } from '@/features/cxp/pages/AnticiposPage';
import {
  AnticiposSearchSchema,
  type AnticiposSearch,
} from '@/features/cxp/lib/notas-y-anticipos-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/anticipos')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarAnticiposLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: AnticiposPage,
  validateSearch: (input: Record<string, unknown>): AnticiposSearch =>
    AnticiposSearchSchema.parse(input),
});
