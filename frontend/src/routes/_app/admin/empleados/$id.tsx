import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EmpleadoDetalle } from '@/modules/administracion/components/EmpleadoDetalle';

export const Route = createFileRoute('/_app/admin/empleados/$id')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.AdminEmpleadosGestionar)) {
      throw redirect({ to: '/' });
    }
  },
  component: EmpleadoDetalle,
});
