import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { SucursalesTopLevelPage } from '@/modules/administracion/components/SucursalesTopLevelPage';

/**
 * <c>/admin/sucursales</c> — home top-level de Sucursales (ADR-0051).
 * Reemplaza al tab "Sucursales" de <c>/admin/empresas/$id</c> como
 * punto de entrada de navegación — Empresas deja de ser el eje
 * principal, Sucursales pasa a serlo.
 *
 * <para><b>Guard</b>: requiere <c>admin.empresas.sucursales-gestionar</c>,
 * mismo permiso que ya gestiona <c>SucursalesPanel</c>.</para>
 */
export const Route = createFileRoute('/_app/admin/sucursales/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AdminEmpresasSucursalesGestionar)) {
      throw redirect({ to: '/' });
    }
  },
  component: SucursalesIndexRoute,
});

function SucursalesIndexRoute() {
  return <SucursalesTopLevelPage />;
}
