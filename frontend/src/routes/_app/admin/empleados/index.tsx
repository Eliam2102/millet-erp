import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EmpleadosPage } from '@/modules/administracion/components/EmpleadosPage';

interface EmpleadosSearch {
  /** <c>sin-empleado</c> abre la vista «Cuentas sin empleado» (86e3x6cpw). */
  vista?: 'sin-empleado';
}

/**
 * <c>/admin/empleados</c> — master de empleados (ADM-FE-PR1,
 * doc 10-catalogo-puestos-empleados). Lista + alta + edición inline +
 * baja/reactivación, patrón de Canales de venta. Desde 86e3x6cpw también
 * es el único lugar para las cuentas Microsoft sin empleado.
 *
 * <para><b>Guard</b>: requiere <c>admin.empleados.gestionar</c>. Sin
 * permiso, redirige a <c>/</c>.</para>
 */
export const Route = createFileRoute('/_app/admin/empleados/')({
  validateSearch: (search: Record<string, unknown>): EmpleadosSearch =>
    search.vista === 'sin-empleado' ? { vista: 'sin-empleado' } : {},
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AdminEmpleadosGestionar)) {
      throw redirect({ to: '/' });
    }
  },
  component: EmpleadosIndexRoute,
});

function EmpleadosIndexRoute() {
  const { vista } = Route.useSearch();
  return <EmpleadosPage vistaInicial={vista === 'sin-empleado' ? 'sin-empleado' : 'empleados'} />;
}
