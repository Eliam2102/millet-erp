import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { FormasPagoPage } from '@/modules/catalogos/components/FormasPagoPage';

/**
 * <c>/admin/catalogos/formas-pago</c> — read-only SAT (UF-Admin-PR5.3).
 * Guard <c>compartido.catalogos.leer</c>.
 */
export const Route = createFileRoute('/_app/admin/catalogos/formas-pago/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CompartidoCatalogosLeer)) {
      throw redirect({ to: '/' });
    }
  },
  component: FormasPagoPage,
});
