import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { UsosPrincipalesPage } from '@/modules/catalogos/components/UsosPrincipalesPage';
import { NuevoUsoPrincipalProvider } from '@/modules/catalogos/components/SheetNuevoUsoPrincipal';

/**
 * <c>/admin/catalogos/usos-principales</c> — bandeja P1 (UF-Admin-PR5.2).
 * El backend usa el permiso grueso para mutación.
 */
export const Route = createFileRoute(
  '/_app/admin/catalogos/usos-principales/',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.CompartidoCatalogosLeer) &&
      !permisos.includes(PermisosCanonicos.CompartidoCatalogosAdministrar)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: UsosPrincipalesIndexRoute,
});

function UsosPrincipalesIndexRoute() {
  return (
    <NuevoUsoPrincipalProvider>
      <UsosPrincipalesPage />
    </NuevoUsoPrincipalProvider>
  );
}
