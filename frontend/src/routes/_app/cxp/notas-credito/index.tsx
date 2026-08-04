import { createFileRoute, redirect } from '@tanstack/react-router';
import { NotasCreditoPage } from '@/features/cxp/pages/NotasCreditoPage';
import {
  NotasCreditoSearchSchema,
  type NotasCreditoSearch,
} from '@/features/cxp/lib/notas-y-anticipos-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export const Route = createFileRoute('/_app/cxp/notas-credito/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarNotasCreditoLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: NotasCreditoPage,
  validateSearch: (input: Record<string, unknown>): NotasCreditoSearch =>
    NotasCreditoSearchSchema.parse(input),
});
