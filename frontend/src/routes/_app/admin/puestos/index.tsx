import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { PuestosPage } from '@/modules/administracion/components/PuestosPage';

/**
 * <c>/admin/puestos</c> — catálogo de puestos organizacionales
 * (ADM-FE-PR1, doc 10-catalogo-puestos-empleados). Lista + alta +
 * edición inline + desactivar/reactivar, patrón de Canales de venta.
 *
 * <para><b>Guard</b>: requiere <c>admin.puestos.gestionar</c>. Sin
 * permiso, redirige a <c>/</c>.</para>
 */
export const Route = createFileRoute('/_app/admin/puestos/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AdminPuestosGestionar)) {
      throw redirect({ to: '/' });
    }
  },
  component: PuestosIndexRoute,
});

function PuestosIndexRoute() {
  return <PuestosPage />;
}
