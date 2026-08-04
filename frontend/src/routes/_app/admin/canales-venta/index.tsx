import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { CanalesVentaPage } from '@/modules/administracion/components/CanalesVentaPage';

/**
 * <c>/admin/canales-venta</c> — catálogo administrable de Canales de
 * venta (FAC-ING-PR3; backend FAC-ING-PR2). Lista + alta + edición
 * inline de nombre/clave A+W/estatus, patrón de Sucursales.
 *
 * <para><b>Guard</b>: requiere <c>admin.empresas.sucursales-gestionar</c>
 * (mismo permiso que el CRUD de sucursales — decisión del backend).
 * Sin permiso, redirige a <c>/</c>.</para>
 */
export const Route = createFileRoute('/_app/admin/canales-venta/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.AdminEmpresasSucursalesGestionar)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: CanalesVentaIndexRoute,
});

function CanalesVentaIndexRoute() {
  return <CanalesVentaPage />;
}
