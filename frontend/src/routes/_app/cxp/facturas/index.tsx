import { createFileRoute, redirect } from '@tanstack/react-router';
import { FacturasPage } from '@/features/cxp/pages/FacturasPage';
import {
  FacturasSearchSchema,
  type FacturasSearch,
} from '@/features/cxp/lib/facturas-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P1 — Bandeja de Facturas (doc 07 §FE-F2-PR1).
 * Gate: <c>cuentas_por_pagar.facturas.leer</c>. Sin permiso, redirige
 * a la landing del módulo.
 */
export const Route = createFileRoute('/_app/cxp/facturas/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarFacturasLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: FacturasPage,
  validateSearch: (input: Record<string, unknown>): FacturasSearch =>
    FacturasSearchSchema.parse(input),
});
