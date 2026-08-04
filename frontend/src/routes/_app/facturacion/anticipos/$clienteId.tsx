import { createFileRoute, redirect } from '@tanstack/react-router';
import { EstadoCuentaAnticipos } from '@/features/facturacion/pages/EstadoCuentaAnticipos';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Estado de cuenta de anticipos por cliente —
 * <c>/facturacion/anticipos/$clienteId</c> (FE-F4-PR2).
 */
export const Route = createFileRoute('/_app/facturacion/anticipos/$clienteId')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionAnticiposLeer)) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: EstadoCuentaAnticipos,
});
