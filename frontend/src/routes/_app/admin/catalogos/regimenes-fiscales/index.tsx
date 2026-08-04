import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { RegimenesFiscalesPage } from '@/modules/catalogos/components/RegimenesFiscalesPage';

/**
 * <c>/admin/catalogos/regimenes-fiscales</c> — read-only SAT
 * (UF-Admin-PR5.3).
 */
export const Route = createFileRoute(
  '/_app/admin/catalogos/regimenes-fiscales/',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CompartidoCatalogosLeer)) {
      throw redirect({ to: '/' });
    }
  },
  component: RegimenesFiscalesPage,
});
