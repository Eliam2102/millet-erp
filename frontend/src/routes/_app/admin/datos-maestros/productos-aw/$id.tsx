import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ProductosAwLayout } from '@/modules/datos-maestros/components/ProductosAwLayout';
import { ProductoAwDetalle } from '@/modules/datos-maestros/components/ProductoAwDetalle';
import { NuevoProductoAwProvider } from '@/modules/datos-maestros/components/SheetNuevoProductoAw';

/**
 * <c>/admin/datos-maestros/productos-aw/$id</c> — detalle dentro del
 * layout master-detail (ADR-0048). <c>key={id}</c> fuerza re-mount al
 * navegar entre dos productos para evitar estado stale.
 */
export const Route = createFileRoute(
  '/_app/admin/datos-maestros/productos-aw/$id',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.DatosMaestrosProductosAwGestionar)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({
    from: '/_app/admin/datos-maestros/productos-aw/$id',
  });
  return (
    <NuevoProductoAwProvider>
      <ProductosAwLayout
        idActivo={id}
        detalle={<ProductoAwDetalle key={id} />}
      />
    </NuevoProductoAwProvider>
  );
}
