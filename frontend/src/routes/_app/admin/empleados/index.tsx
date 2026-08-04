import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EmpleadosPage } from '@/modules/administracion/components/EmpleadosPage';

/**
 * <c>/admin/empleados</c> — master de empleados (ADM-FE-PR1,
 * doc 10-catalogo-puestos-empleados). Lista + alta + edición inline +
 * baja/reactivación, patrón de Canales de venta.
 *
 * <para><b>Guard</b>: requiere <c>admin.empleados.gestionar</c>. Sin
 * permiso, redirige a <c>/</c>.</para>
 */
export const Route = createFileRoute('/_app/admin/empleados/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AdminEmpleadosGestionar)) {
      throw redirect({ to: '/' });
    }
  },
  component: EmpleadosIndexRoute,
});

function EmpleadosIndexRoute() {
  return <EmpleadosPage />;
}
