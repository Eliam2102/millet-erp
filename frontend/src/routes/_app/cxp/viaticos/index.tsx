import { createFileRoute, redirect } from '@tanstack/react-router';
import { ViaticosPage } from '@/features/cxp/pages/ViaticosPage';
import {
  ViaticosSearchSchema,
  type ViaticosSearch,
} from '@/features/cxp/lib/viaticos-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/viaticos/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarViaticosLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: ViaticosPage,
  validateSearch: (input: Record<string, unknown>): ViaticosSearch =>
    ViaticosSearchSchema.parse(input),
});
