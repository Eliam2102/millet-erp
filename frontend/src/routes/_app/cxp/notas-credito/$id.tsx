import { createFileRoute, redirect } from '@tanstack/react-router';
import { NotaCreditoDetallePage } from '@/features/cxp/pages/NotaCreditoDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P3 — Detalle de Nota de Crédito (serie detalles CxP).
 * Gate: <c>cuentas_por_pagar.notas-credito.leer</c>.
 */
export const Route = createFileRoute('/_app/cxp/notas-credito/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarNotasCreditoLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: NotaCreditoDetallePage,
});
