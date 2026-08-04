import { createFileRoute, redirect } from '@tanstack/react-router';
import { EstadosCuentaTcPage } from '@/features/cxp/pages/EstadosCuentaTcPage';
import {
  EstadosCuentaTcSearchSchema,
  type EstadosCuentaTcSearch,
} from '@/features/cxp/lib/tc-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/tc/estados-cuenta')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarTcLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: EstadosCuentaTcPage,
  validateSearch: (input: Record<string, unknown>): EstadosCuentaTcSearch =>
    EstadosCuentaTcSearchSchema.parse(input),
});
