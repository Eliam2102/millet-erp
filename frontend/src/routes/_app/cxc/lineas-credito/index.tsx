import { createFileRoute, redirect } from '@tanstack/react-router';
import { BandejaLineasCredito } from '@/features/cxc/pages/BandejaLineasCredito';
import {
  LineasSearchSchema,
  type LineasSearch,
} from '@/features/cxc/lib/lineas-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Bandeja de líneas de crédito — <c>/cxc/lineas-credito</c> (CXC-FE-PR2,
 * P1 tabular §6.6). Guard espejo del backend
 * (<c>lineas-credito.leer</c>); "Ver" monta el master-detail en
 * <c>/$id</c>.
 */
export const Route = createFileRoute('/_app/cxc/lineas-credito/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorCobrarLineasCreditoLeer)) {
      throw redirect({ to: '/cxc' });
    }
  },
  component: BandejaLineasCredito,
  validateSearch: (input: Record<string, unknown>): LineasSearch =>
    LineasSearchSchema.parse(input),
});
