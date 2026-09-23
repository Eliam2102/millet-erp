import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { SucursalesLayout } from '@/modules/administracion/components/SucursalesLayout';
import { SucursalDetalle } from '@/modules/administracion/components/SucursalDetalle';

/**
 * <c>/admin/sucursales/$id</c> — detalle master-detail de una sucursal
 * (F1-ADM-01.3.1). Muestra la lista de sucursales en la columna
 * izquierda y las tabs de asignación (Departamentos, Puestos, Usuarios)
 * en el panel derecho.
 */
export const Route = createFileRoute('/_app/admin/sucursales/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CompartidoCatalogosLeer)) {
      throw redirect({ to: '/' });
    }
  },
  component: SucursalDetalleRoute,
});

function SucursalDetalleRoute() {
  const { id } = Route.useParams();
  return <SucursalesLayout idActivo={id} detalle={<SucursalDetalle key={id} />} />;
}

