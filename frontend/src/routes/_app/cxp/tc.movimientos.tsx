import { createFileRoute, redirect } from '@tanstack/react-router';
import { MovimientosTcPage } from '@/features/cxp/pages/MovimientosTcPage';
import {
  MovimientosTcSearchSchema,
  type MovimientosTcSearch,
} from '@/features/cxp/lib/tc-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/tc/movimientos')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarTcLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: MovimientosTcPage,
  validateSearch: (input: Record<string, unknown>): MovimientosTcSearch =>
    MovimientosTcSearchSchema.parse(input),
});
