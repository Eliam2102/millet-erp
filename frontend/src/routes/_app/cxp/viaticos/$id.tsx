import { createFileRoute, redirect } from '@tanstack/react-router';
import { ViaticoDetallePage } from '@/features/cxp/pages/ViaticoDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P3 — Detalle de Solicitud de Viáticos (serie detalles CxP).
 * Gate: <c>cuentas_por_pagar.viaticos.leer</c>.
 */
export const Route = createFileRoute('/_app/cxp/viaticos/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarViaticosLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: ViaticoDetallePage,
});
