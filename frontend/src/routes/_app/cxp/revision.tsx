import { createFileRoute, redirect } from '@tanstack/react-router';
import { RevisionPage } from '@/features/cxp/pages/RevisionPage';
import {
  RevisionSearchSchema,
  type RevisionSearch,
} from '@/features/cxp/lib/revision-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P2 — Bandeja "Revisión por área" (doc 07 §FE-F3-PR1).
 * Gate: <c>cuentas_por_pagar.facturas.liberar-revision</c>.
 */
export const Route = createFileRoute('/_app/cxp/revision')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(
        PermisosCanonicos.CuentasPorPagarFacturasLiberarRevision,
      )
    ) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: RevisionPage,
  validateSearch: (input: Record<string, unknown>): RevisionSearch =>
    RevisionSearchSchema.parse(input),
});
