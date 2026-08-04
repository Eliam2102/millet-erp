import { createFileRoute, redirect } from '@tanstack/react-router';
import { TarjetasPage } from '@/features/cxp/pages/TarjetasPage';
import {
  TarjetasSearchSchema,
  type TarjetasSearch,
} from '@/features/cxp/lib/tc-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/tc/tarjetas')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarTcLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: TarjetasPage,
  validateSearch: (input: Record<string, unknown>): TarjetasSearch =>
    TarjetasSearchSchema.parse(input),
});
