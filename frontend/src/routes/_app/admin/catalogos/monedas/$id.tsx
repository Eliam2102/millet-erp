import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { MonedasLayout } from '@/modules/catalogos/components/MonedasLayout';
import { MonedaDetalle } from '@/modules/catalogos/components/MonedaDetalle';
import { NuevaMonedaProvider } from '@/modules/catalogos/components/SheetNuevaMoneda';

/**
 * <c>/admin/catalogos/monedas/$id</c> — detalle dentro del layout
 * master-detail. <c>key={id}</c> fuerza re-mount al navegar entre
 * monedas para evitar estado stale.
 */
export const Route = createFileRoute('/_app/admin/catalogos/monedas/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.CompartidoCatalogosLeer) &&
      !permisos.includes(PermisosCanonicos.CatalogosMonedasGestionar)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({ from: '/_app/admin/catalogos/monedas/$id' });
  return (
    <NuevaMonedaProvider>
      <MonedasLayout idActivo={id} detalle={<MonedaDetalle key={id} />} />
    </NuevaMonedaProvider>
  );
}
