import { createFileRoute, redirect } from '@tanstack/react-router';
import { CfdisPage } from '@/features/cxp/pages/CfdisPage';
import {
  CfdisSearchSchema,
  type CfdisSearch,
} from '@/features/cxp/lib/cfdis-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P2 — Bandeja de CFDIs recibidos (doc 07 §FE-F1-PR1).
 * Gate: <c>cuentas_por_pagar.cfdis.leer</c>. Sin permiso, redirige a la
 * landing del módulo.
 */
export const Route = createFileRoute('/_app/cxp/cfdis')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarCfdisLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: CfdisPage,
  validateSearch: (input: Record<string, unknown>): CfdisSearch =>
    CfdisSearchSchema.parse(input),
});
