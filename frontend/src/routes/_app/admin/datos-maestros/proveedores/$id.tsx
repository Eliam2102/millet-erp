import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ProveedoresLayout } from '@/modules/datos-maestros/components/ProveedoresLayout';
import { ProveedorDetalle } from '@/modules/datos-maestros/components/ProveedorDetalle';
import { NuevoProveedorProvider } from '@/modules/datos-maestros/components/SheetNuevoProveedor';

/**
 * <c>/admin/datos-maestros/proveedores/$id</c> — detalle dentro del
 * layout master-detail. <c>key={id}</c> fuerza re-mount al navegar
 * entre dos proveedores para evitar estado stale.
 */
export const Route = createFileRoute(
  '/_app/admin/datos-maestros/proveedores/$id',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.DatosMaestrosProveedoresGestionar)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({
    from: '/_app/admin/datos-maestros/proveedores/$id',
  });
  return (
    <NuevoProveedorProvider>
      <ProveedoresLayout
        idActivo={id}
        detalle={<ProveedorDetalle key={id} />}
      />
    </NuevoProveedorProvider>
  );
}
