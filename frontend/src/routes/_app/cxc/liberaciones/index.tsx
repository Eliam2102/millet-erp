import { createFileRoute, redirect } from '@tanstack/react-router';
import { BandejaLiberaciones } from '@/features/cxc/pages/BandejaLiberaciones';
import {
  LiberacionesSearchSchema,
  type LiberacionesSearch,
} from '@/features/cxc/lib/liberaciones-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Bandeja de liberaciones — <c>/cxc/liberaciones</c> (CXC-FE-PR3, P2).
 * El backend gatea la lectura con <c>liberacion.decidir</c>;
 * <c>liberacion.override</c> habilita además la gestión de
 * autorizaciones consumibles.
 */
export const Route = createFileRoute('/_app/cxc/liberaciones/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const permitido = [
      PermisosCanonicos.CuentasPorCobrarLiberacionDecidir,
      PermisosCanonicos.CuentasPorCobrarLiberacionOverride,
    ].some((p) => permisos.includes(p));
    if (!permitido) {
      throw redirect({ to: '/cxc' });
    }
  },
  component: BandejaLiberaciones,
  validateSearch: (input: Record<string, unknown>): LiberacionesSearch =>
    LiberacionesSearchSchema.parse(input),
});
