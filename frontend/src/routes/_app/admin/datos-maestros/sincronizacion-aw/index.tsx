import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { SincronizacionAwPage } from '@/modules/datos-maestros/components/SincronizacionAwPage';

/**
 * <c>/admin/datos-maestros/sincronizacion-aw</c> — conflictos de maestros A+W y matriz campo → dueño.
 * Guard: requiere <c>datos_maestros.clientes.sincronizar</c>; sin él redirige a <c>/</c>.
 */
export const Route = createFileRoute('/_app/admin/datos-maestros/sincronizacion-aw/')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.DatosMaestrosClientesSincronizar)) {
      throw redirect({ to: '/' });
    }
  },
  component: SincronizacionAwPage,
});
