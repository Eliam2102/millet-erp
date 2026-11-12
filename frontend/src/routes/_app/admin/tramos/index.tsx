import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { TramosPage } from '@/features/tramos/TramosPage';

/** `/admin/tramos` — detener y reanudar tramos de la operación (M-303). Requiere `compartido.tramos.gestionar`. */
export const Route = createFileRoute('/_app/admin/tramos/')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(P.CompartidoTramosGestionar)) throw redirect({ to: '/' });
  },
  component: TramosPage,
});
