import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ProveedoresLayout } from '@/modules/datos-maestros/components/ProveedoresLayout';
import { NuevoProveedorProvider } from '@/modules/datos-maestros/components/SheetNuevoProveedor';

/**
 * <c>/admin/datos-maestros/proveedores</c> — bandeja master-detail
 * de proveedores (UF-Admin-PR4.5). Sin <c>$id</c> en la URL, el
 * panel detalle muestra el placeholder.
 *
 * <para><b>Guard</b>: requiere
 * <c>datos_maestros.proveedores.gestionar</c>. Sin permiso, redirige
 * a <c>/</c>.</para>
 */
export const Route = createFileRoute('/_app/admin/datos-maestros/proveedores/')(
  {
    beforeLoad: () => {
      const permisos = useAuthStore.getState().permisos;
      if (
        !permisos.includes(PermisosCanonicos.DatosMaestrosProveedoresGestionar)
      ) {
        throw redirect({ to: '/' });
      }
    },
    component: ProveedoresIndexRoute,
  },
);

function ProveedoresIndexRoute() {
  return (
    <NuevoProveedorProvider>
      <ProveedoresLayout idActivo={null} />
    </NuevoProveedorProvider>
  );
}
