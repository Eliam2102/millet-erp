import { createFileRoute, redirect } from '@tanstack/react-router';
import { ComprobacionDetallePage } from '@/features/cxp/pages/ComprobacionDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P3 — Detalle de Comprobación de Gastos (serie detalles CxP).
 * Gate: <c>cuentas_por_pagar.comprobaciones.leer</c>.
 */
export const Route = createFileRoute('/_app/cxp/comprobaciones/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.CuentasPorPagarComprobacionesLeer)
    ) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: ComprobacionDetallePage,
});
