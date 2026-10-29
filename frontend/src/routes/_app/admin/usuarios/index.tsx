import { createFileRoute, redirect } from '@tanstack/react-router';

/**
 * <c>/admin/usuarios</c> — ruta vieja de «Cuentas de acceso». Desde
 * 86e3x6cpw las personas se administran sólo en Empleados: redirige a la
 * vista «Cuentas sin empleado» (el guard de Empleados aplica sus permisos).
 */
export const Route = createFileRoute('/_app/admin/usuarios/')({
  beforeLoad: () => {
    throw redirect({ to: '/admin/empleados', search: { vista: 'sin-empleado' }, replace: true });
  },
});
