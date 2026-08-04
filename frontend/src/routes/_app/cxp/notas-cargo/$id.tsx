import { createFileRoute, redirect } from '@tanstack/react-router';
import { NotaCargoDetallePage } from '@/features/cxp/pages/NotaCargoDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P3 — Detalle de Nota de Cargo (serie detalles CxP).
 * Gate: <c>cuentas_por_pagar.notas-cargo.leer</c>.
 */
export const Route = createFileRoute('/_app/cxp/notas-cargo/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarNotasCargoLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: NotaCargoDetallePage,
});
