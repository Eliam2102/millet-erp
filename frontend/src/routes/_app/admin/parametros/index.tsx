import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ParametrosPage } from '@/modules/administracion/components/ParametrosPage';

/**
 * <c>/admin/parametros</c> — bandeja P1 (form list) de parámetros
 * globales del sistema (UF-Admin-PR7 §2).
 *
 * <para><b>Guard</b>: requiere <c>admin.parametros.leer</c>. Sin
 * permiso, redirige a <c>/</c>. El permiso de edición
 * <c>admin.parametros.editar</c> se valida adentro del componente para
 * mostrar/ocultar el botón "Guardar"; los usuarios con solo lectura
 * ven valores read-only.</para>
 */
export const Route = createFileRoute('/_app/admin/parametros/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AdminParametrosLeer)) {
      throw redirect({ to: '/' });
    }
  },
  component: ParametrosIndexRoute,
});

function ParametrosIndexRoute() {
  return <ParametrosPage />;
}
