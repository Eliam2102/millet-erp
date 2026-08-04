import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { CategoriasArticuloPage } from '@/modules/catalogos/components/CategoriasArticuloPage';
import { NuevaCategoriaArticuloProvider } from '@/modules/catalogos/components/SheetNuevaCategoriaArticulo';

/**
 * <c>/admin/catalogos/categorias-articulo</c> — bandeja P1 (patrón ADR-0046).
 * El backend reusa el permiso grueso para mutación.
 */
export const Route = createFileRoute(
  '/_app/admin/catalogos/categorias-articulo/',
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
  component: CategoriasArticuloIndexRoute,
});

function CategoriasArticuloIndexRoute() {
  return (
    <NuevaCategoriaArticuloProvider>
      <CategoriasArticuloPage />
    </NuevaCategoriaArticuloProvider>
  );
}
