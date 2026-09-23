import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { DepartamentosTopLevelPage } from '@/modules/administracion/components/DepartamentosTopLevelPage';

/**
 * <c>/admin/departamentos</c> — catálogo top-level de Departamentos
 * (ADR-0051). Reemplaza al tab "Departamentos" de
 * <c>/admin/empresas/$id</c> — mismo patrón que <c>/admin/puestos</c>.
 *
 * <para><b>Guard</b>: requiere <c>admin.departamentos.gestionar</c>,
 * mismo permiso que ya gestiona <c>DepartamentosPanel</c>.</para>
 */
export const Route = createFileRoute('/_app/admin/departamentos/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AdminDepartamentosGestionar)) {
      throw redirect({ to: '/' });
    }
  },
  component: DepartamentosIndexRoute,
});

function DepartamentosIndexRoute() {
  return <DepartamentosTopLevelPage />;
}
