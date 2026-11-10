import { createFileRoute, redirect } from '@tanstack/react-router';
import { TableroIntegraciones } from '@/features/integraciones/pages/TableroIntegraciones';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * <c>/admin/integraciones/tablero</c> — I13.6: estado de cada integración, reintento
 * manual y alertas. Lo ve quien puede leer la salud del sistema.
 */
export const Route = createFileRoute('/_app/admin/integraciones/tablero')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.InfraHealthLeer)) {
      throw redirect({ to: '/' });
    }
  },
  component: TableroIntegraciones,
});
