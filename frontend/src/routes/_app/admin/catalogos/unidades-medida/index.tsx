import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { UnidadesMedidaPage } from '@/modules/catalogos/components/UnidadesMedidaPage';
import { NuevaUnidadMedidaProvider } from '@/modules/catalogos/components/SheetNuevaUnidadMedida';

/**
 * <c>/admin/catalogos/unidades-medida</c> — bandeja del catálogo de
 * unidades de medida (ADR-0046 Etapa 1a).
 */
export const Route = createFileRoute(
  '/_app/admin/catalogos/unidades-medida/',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.CompartidoCatalogosLeer) &&
      !permisos.includes(PermisosCanonicos.CatalogosUnidadesMedidaGestionar)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: UnidadesMedidaIndexRoute,
});

function UnidadesMedidaIndexRoute() {
  return (
    <NuevaUnidadMedidaProvider>
      <UnidadesMedidaPage />
    </NuevaUnidadMedidaProvider>
  );
}
