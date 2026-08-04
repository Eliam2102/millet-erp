import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { MonedasLayout } from '@/modules/catalogos/components/MonedasLayout';
import { NuevaMonedaProvider } from '@/modules/catalogos/components/SheetNuevaMoneda';

/**
 * <c>/admin/catalogos/monedas</c> — bandeja master-detail de monedas
 * (UF-Admin-PR5.1). Sin <c>$id</c> en URL, panel detalle muestra
 * placeholder.
 */
export const Route = createFileRoute('/_app/admin/catalogos/monedas/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    // Lectura del catálogo solo requiere `compartido.catalogos.leer`;
    // si el usuario solo tiene mutación (Gestionar) sin lectura, igual
    // dejamos pasar — Gestionar implica Leer en la práctica.
    if (
      !permisos.includes(PermisosCanonicos.CompartidoCatalogosLeer) &&
      !permisos.includes(PermisosCanonicos.CatalogosMonedasGestionar)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: MonedasIndexRoute,
});

function MonedasIndexRoute() {
  return (
    <NuevaMonedaProvider>
      <MonedasLayout idActivo={null} />
    </NuevaMonedaProvider>
  );
}
