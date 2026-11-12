import { createFileRoute, redirect } from '@tanstack/react-router';
import { OrigenCapturaMpPage } from '@/features/almacen/pages/OrigenCapturaMpPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/** M-335: origen de captura de materia prima (ERP o app de Almacenes). Gate: autoridad del cierre mensual de Almacén. */
export const Route = createFileRoute('/_app/almacen/origen-captura')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenCierreMesGestionarTodasSucursales)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: OrigenCapturaMpPage,
});
