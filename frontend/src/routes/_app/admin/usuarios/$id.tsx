import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { obtenerEmpleadoPorUsuario } from '@/modules/administracion/api/empleados';
import { CuentaDetalle } from '@/modules/identidad/components/CuentaDetalle';

/**
 * <c>/admin/usuarios/$id</c> — ficha de una cuenta. Si la cuenta tiene empleado
 * redirige a la ficha del empleado (86e3x6cpw,
 * <c>GET /admin/colaboradores/por-usuario/{id}</c>). Si no tiene (404), muestra la
 * ficha de la cuenta con su rol y permisos (86e3yxe9m): así se ven y se
 * administran el administrador inicial y las demás cuentas técnicas o sin empleado.
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
  },
  component: CuentaSinEmpleadoRoute,
});

function CuentaSinEmpleadoRoute() {
  const { id } = Route.useParams();
  return <CuentaDetalle usuarioId={id} />;
}
