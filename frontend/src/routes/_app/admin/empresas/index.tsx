import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EmpresasLayout } from '@/modules/administracion/components/EmpresasLayout';
import { NuevaEmpresaProvider } from '@/modules/administracion/components/SheetNuevaEmpresa';

/**
 * <c>/admin/empresas</c> — bandeja master-detail de empresas
 * (UF-Admin-PR2). Sin <c>$id</c> en la URL, el panel detalle muestra
 * el placeholder "Selecciona una empresa…".
 *
 * <para><b>Guard</b>: requiere <c>admin.empresas.leer</c>. Sin
 * permiso, redirige a <c>/</c> — el gear de Topbar ya hace el mismo
 * gate; la URL directa también debe estar protegida.</para>
 *
 * <para>El <c>&lt;NuevaEmpresaProvider/&gt;</c> envuelve la página
 * (en lugar de vivir a nivel <c>_app</c>) porque el Sheet "Nueva
 * empresa" solo aparece dentro del módulo Administración — no es un
 * recurso transversal del shell como sí lo es la nueva requisición
 * de Compras (botón Quick Create).</para>
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
  return (
    <NuevaEmpresaProvider>
      <EmpresasLayout idActivo={null} />
    </NuevaEmpresaProvider>
  );
}
