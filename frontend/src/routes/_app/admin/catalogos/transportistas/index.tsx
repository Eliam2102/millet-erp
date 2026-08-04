import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { TransportistasPage } from '@/modules/catalogos/components/TransportistasPage';
import { NuevoTransportistaProvider } from '@/modules/catalogos/components/SheetNuevoTransportista';

/**
 * <c>/admin/catalogos/transportistas</c> — bandeja P1 (UF-Admin-PR5.2).
 */
export const Route = createFileRoute('/_app/admin/catalogos/transportistas/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.CompartidoCatalogosLeer) &&
      !permisos.includes(PermisosCanonicos.CatalogosTransportistasGestionar)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: TransportistasIndexRoute,
});

function TransportistasIndexRoute() {
  return (
    <NuevoTransportistaProvider>
      <TransportistasPage />
    </NuevoTransportistaProvider>
  );
}
