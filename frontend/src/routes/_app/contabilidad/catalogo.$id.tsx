import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { CuentaDetallePage } from '@/features/contabilidad/pages/CuentaDetallePage';

/** Guard de UX: sin ContabilidadCatalogoLeer se redirige; la autorización real es del API (403). */
export const Route = createFileRoute('/_app/contabilidad/catalogo/$id')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadCatalogoLeer)) throw redirect({ to: '/' });
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = Route.useParams();
  return <CuentaDetallePage id={id} />;
}
