import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { IncidenciasPage } from '@/features/comercio-exterior/pages/IncidenciasPage';
export const Route = createFileRoute('/_app/comercio-exterior/incidencias')({
  beforeLoad: () => {
    if (
      !useAuthStore.getState().permisos.includes(PermisosCanonicos.ComercioExteriorExpedientesLeer)
    )
      throw redirect({ to: '/' });
  },
  component: IncidenciasPage,
});
