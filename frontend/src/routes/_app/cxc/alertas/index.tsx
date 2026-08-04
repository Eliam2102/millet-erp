import { createFileRoute, redirect } from '@tanstack/react-router';
import { AlertasPage } from '@/features/cxc/pages/AlertasPage';
import {
  AlertasSearchSchema,
  type AlertasSearch,
} from '@/features/cxc/lib/alertas-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Bandeja de alertas de cartera — <c>/cxc/alertas</c> (CXC-FE-PR7, P2).
 * Lectura gateada por <c>cartera.leer</c> (espejo del backend); atender
 * exige <c>cobranza.registrar</c>.
 */
export const Route = createFileRoute('/_app/cxc/alertas/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorCobrarCarteraLeer)) {
      throw redirect({ to: '/cxc' });
    }
  },
  component: AlertasPage,
  validateSearch: (input: Record<string, unknown>): AlertasSearch =>
    AlertasSearchSchema.parse(input),
});
