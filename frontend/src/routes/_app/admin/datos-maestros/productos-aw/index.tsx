import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ProductosAwLayout } from '@/modules/datos-maestros/components/ProductosAwLayout';
import { NuevoProductoAwProvider } from '@/modules/datos-maestros/components/SheetNuevoProductoAw';

/**
 * <c>/admin/datos-maestros/productos-aw</c> — bandeja master-detail de
 * productos de venta A+W (ADR-0048). Sin <c>$id</c> en la URL, el
 * panel detalle muestra el placeholder.
 *
 * <para><b>Guard</b>: requiere
 * <c>datos_maestros.productos-aw.gestionar</c>. Sin permiso, redirige
 * a <c>/</c>.</para>
 */
export const Route = createFileRoute(
  '/_app/admin/datos-maestros/productos-aw/',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.DatosMaestrosProductosAwGestionar)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: ProductosAwIndexRoute,
});

function ProductosAwIndexRoute() {
  return (
    <NuevoProductoAwProvider>
      <ProductosAwLayout idActivo={null} />
    </NuevoProductoAwProvider>
  );
}
