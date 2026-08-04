import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ArticulosLayout } from '@/modules/datos-maestros/components/ArticulosLayout';
import { NuevoArticuloProvider } from '@/modules/datos-maestros/components/SheetNuevoArticulo';

/**
 * <c>/admin/datos-maestros/articulos</c> — bandeja master-detail
 * de artículos (UF-Admin-PR4.5). Análogo a la ruta de proveedores.
 */
export const Route = createFileRoute('/_app/admin/datos-maestros/articulos/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.DatosMaestrosArticulosGestionar)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: ArticulosIndexRoute,
});

function ArticulosIndexRoute() {
  return (
    <NuevoArticuloProvider>
      <ArticulosLayout idActivo={null} />
    </NuevoArticuloProvider>
  );
}
