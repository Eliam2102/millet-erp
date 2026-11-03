import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { obtenerEmpleadoPorUsuario } from '@/modules/administracion/api/empleados';

/**
 * <c>/admin/usuarios/$id</c> — ruta vieja del detalle de cuenta. Desde
 * 86e3x6cpw redirige a la ficha del empleado ligado a la cuenta
 * (<c>GET /admin/colaboradores/por-usuario/{id}</c>); si la cuenta no tiene
 * empleado (404), a la vista «Cuentas sin empleado».
 */
export const Route = createFileRoute('/_app/admin/usuarios/$id')({
  beforeLoad: async ({ params }) => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.AdminEmpleadosGestionar)) {
      throw redirect({ to: '/' });
    }
    const empleadoId = await obtenerEmpleadoPorUsuario(params.id);
    if (empleadoId) {
      throw redirect({ to: '/admin/empleados/$id', params: { id: empleadoId }, replace: true });
    }
    throw redirect({ to: '/admin/empleados', search: { vista: 'sin-empleado' }, replace: true });
  },
});
