import { createFileRoute, redirect } from '@tanstack/react-router';
import { FacturaDetallePage } from '@/features/cxp/pages/FacturaDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P2 — Detalle de Factura (doc 07 §FE-F2-PR1).
 * Gate: <c>cuentas_por_pagar.facturas.leer</c>.
 */
export const Route = createFileRoute('/_app/cxp/facturas/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarFacturasLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: FacturaDetallePage,
});
