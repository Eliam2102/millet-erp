import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EmpresasLayout } from '@/modules/administracion/components/EmpresasLayout';

/**
 * <c>/admin/empresas</c> — bandeja master-detail de empresas. Sin
 * card propia en el nav desde ADR-0051 (Millet opera con una sola
 * razón social) — solo alcanzable vía el link "avanzado" de
 * <c>/admin/sucursales</c> y <c>/admin/departamentos</c>. Sin
 * <c>$id</c> en la URL, el panel detalle muestra el placeholder
 * "Selecciona una empresa…". No hay alta de empresas desde la UI.
 *
 * <para><b>Guard</b>: requiere <c>admin.empresas.leer</c>. Sin
 * permiso, redirige a <c>/</c> — el gear de Topbar ya hace el mismo
 * gate; la URL directa también debe estar protegida.</para>
 */
export const Route = createFileRoute('/_app/admin/empresas/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AdminEmpresasLeer)) {
      throw redirect({ to: '/' });
    }
  },
  component: EmpresasIndexRoute,
});

function EmpresasIndexRoute() {
  return <EmpresasLayout idActivo={null} />;
}
