import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ExpedienteDetallePage } from '@/features/comercio-exterior/pages/ExpedienteDetallePage';
export const Route = createFileRoute('/_app/comercio-exterior/expedientes/$id')({
  beforeLoad: () => {
    if (
      !useAuthStore.getState().permisos.includes(PermisosCanonicos.ComercioExteriorExpedientesLeer)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: Detalle,
});
function Detalle() {
  const { id } = Route.useParams();
  return <ExpedienteDetallePage key={id} id={id} />;
}
