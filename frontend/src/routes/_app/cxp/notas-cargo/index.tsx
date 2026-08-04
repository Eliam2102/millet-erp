import { createFileRoute, redirect } from '@tanstack/react-router';
import { NotasCargoPage } from '@/features/cxp/pages/NotasCargoPage';
import {
  NotasCargoSearchSchema,
  type NotasCargoSearch,
} from '@/features/cxp/lib/notas-y-anticipos-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/notas-cargo/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarNotasCargoLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: NotasCargoPage,
  validateSearch: (input: Record<string, unknown>): NotasCargoSearch =>
    NotasCargoSearchSchema.parse(input),
});
