import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { IncotermsPage } from '@/modules/catalogos/components/IncotermsPage';
import { NuevoIncotermProvider } from '@/modules/catalogos/components/SheetNuevoIncoterm';

/**
 * <c>/admin/catalogos/incoterms</c> — bandeja P1 (UF-Admin-PR5.2).
 */
export const Route = createFileRoute('/_app/admin/catalogos/incoterms/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.CompartidoCatalogosLeer) &&
      !permisos.includes(PermisosCanonicos.CatalogosIncotermsGestionar)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: IncotermsIndexRoute,
});

function IncotermsIndexRoute() {
  return (
    <NuevoIncotermProvider>
      <IncotermsPage />
    </NuevoIncotermProvider>
  );
}
